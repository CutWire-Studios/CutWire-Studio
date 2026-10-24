using CutWire.Studio.Api.Auth;
using CutWire.Studio.Api.Http;
using CutWire.Studio.Infrastructure.Identity;
using CutWire.Studio.Infrastructure.Oidc;
using CutWire.Studio.Infrastructure.Persistence;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace CutWire.Studio.Api.Endpoints;

public static class ConnectEndpoints
{
    private const string Server = OpenIddictServerAspNetCoreDefaults.AuthenticationScheme;
    private static readonly string[] ReadWrite = ["GET", "POST"];

    public static void MapConnect(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/connect/authorize", ReadWrite, Authorize).ExcludeFromDescription();
        app.MapPost("/connect/token", Exchange).ExcludeFromDescription();
        app.MapMethods("/connect/userinfo", ReadWrite, UserInfo).ExcludeFromDescription();
        app.MapMethods("/connect/end-session", ReadWrite, EndSession).ExcludeFromDescription();
        app.MapGet("/connect/verify", VerifyPage).ExcludeFromDescription();
        app.MapPost("/connect/verify", VerifyDecision).ExcludeFromDescription();

        app.MapPost("/connect/register", async (ClientRegistrationRequest body, DynamicClientRegistration registration, CancellationToken ct) =>
            {
                var result = await registration.RegisterAsync(body, ct);
                return result.IsSuccess
                    ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
                    : Results.Json(new { error = result.Error.Code, error_description = result.Error.Message },
                        statusCode: StatusCodes.Status400BadRequest);
            })
            .RequireRateLimiting(RateLimitingSettings.StrictPolicy)
            .ExcludeFromDescription();
    }

    private static async Task<IResult> Authorize(
        HttpContext http,
        IOpenIddictApplicationManager applications,
        IOpenIddictAuthorizationManager authorizations,
        IOpenIddictScopeManager scopeManager,
        UserManager<ApplicationUser> users,
        ConsentService consent,
        OidcPrincipalFactory principals,
        CancellationToken ct)
    {
        var request = http.GetOpenIddictServerRequest()
                      ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        var session = await http.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var user = session.Succeeded ? await users.GetUserAsync(session.Principal!) : null;
        if (user is null || AccountService.IsDeleted(user))
        {
            if (request.HasPromptValue(PromptValues.None))
                return Forbid(Errors.LoginRequired, "The user is not signed in.");
            return Results.Redirect($"/account/sign-in?return={Uri.EscapeDataString(CurrentAuthorizeUrl(http))}");
        }

        var application = await applications.FindByClientIdAsync(request.ClientId!, ct)
                          ?? throw new InvalidOperationException("The client application cannot be found.");
        var scopes = request.GetScopes();

        var grant = await consent.FindGrantAsync(user.Id, application, scopes, ct);
        if (grant is null)
        {
            if (request.HasPromptValue(PromptValues.None))
                return Forbid(Errors.ConsentRequired, "Interactive consent is required.");
            return Results.Redirect($"/account/consent?return={Uri.EscapeDataString(CurrentAuthorizeUrl(http))}");
        }

        var requestedResources = request.GetResources();
        IEnumerable<string> resources = requestedResources.Length > 0
            ? requestedResources
            : await scopeManager.ListResourcesAsync(scopes, ct).ToListAsync(ct);
        var principal = await principals.CreateAsync(user, scopes, resources, await authorizations.GetIdAsync(grant, ct), ct);
        return Results.SignIn(principal, properties: null, authenticationScheme: Server);
    }

    private static async Task<IResult> Exchange(HttpContext http, UserManager<ApplicationUser> users)
    {
        var request = http.GetOpenIddictServerRequest()
                      ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");
        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType() && !request.IsDeviceCodeGrantType())
            throw new InvalidOperationException("The specified grant type is not supported.");

        var result = await http.AuthenticateAsync(Server);
        var user = result.Principal is null ? null : await users.FindByIdAsync(result.Principal.GetClaim(Claims.Subject) ?? "");
        if (user is null || AccountService.IsDeleted(user))
            return Forbid(Errors.InvalidGrant, "The account is no longer available.");

        return Results.SignIn(result.Principal!, properties: null, authenticationScheme: Server);
    }

    private static async Task<IResult> UserInfo(HttpContext http, StudioDbContext db, UserManager<ApplicationUser> users,
        PublicUrl publicUrl, CancellationToken ct)
    {
        var result = await http.AuthenticateAsync(Server);
        var principal = result.Principal!;
        var user = result.Principal is null ? null : await users.FindByIdAsync(principal.GetClaim(Claims.Subject) ?? "");
        var profile = user is null ? null : await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == user.Id, ct);
        if (user is null || profile is null || AccountService.IsDeleted(user))
            return Forbid(Errors.InvalidToken, "The account is no longer available.");

        var claims = new Dictionary<string, object> { [Claims.Subject] = user.Id.ToString() };
        if (principal.HasScope(Scopes.Profile))
        {
            claims[Claims.Name] = profile.DisplayName;
            claims[Claims.PreferredUsername] = profile.Handle;
            if (profile.AvatarSha256 is { } avatar) claims[Claims.Picture] = publicUrl.Absolute($"/files/{avatar}");
        }

        if (principal.HasScope(Scopes.Email))
        {
            claims[Claims.Email] = user.Email!;
            claims[Claims.EmailVerified] = user.EmailConfirmed;
        }

        return Results.Ok(claims);
    }

    private static async Task<IResult> EndSession(HttpContext http, StudioSignInManager signIn)
    {
        await signIn.SignOutAsync();
        return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, [Server]);
    }

    private static async Task<IResult> VerifyPage(HttpContext http, IOpenIddictApplicationManager applications, CancellationToken ct)
    {
        var result = await http.AuthenticateAsync(Server);
        if (!result.Succeeded || result.Principal?.GetClaim(Claims.ClientId) is null)
            return Results.Redirect(http.Request.Query.ContainsKey(Parameters.UserCode)
                ? "/account/device?error=invalid_code"
                : "/account/device");

        var session = await http.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var userCode = http.Request.Query[Parameters.UserCode].ToString();
        if (!session.Succeeded)
            return Results.Redirect(
                $"/account/sign-in?return={Uri.EscapeDataString("/connect/verify?user_code=" + Uri.EscapeDataString(userCode))}");

        var clientId = result.Principal.GetClaim(Claims.ClientId)!;
        var application = await applications.FindByClientIdAsync(clientId, ct);
        var name = application is null ? "Application" : await applications.GetDisplayNameAsync(application, ct) ?? clientId;
        var scopes = string.Join(' ', result.Principal.GetScopes());
        return Results.Redirect(
            $"/account/device?user_code={Uri.EscapeDataString(userCode)}&client={Uri.EscapeDataString(name!)}&scope={Uri.EscapeDataString(scopes)}");
    }

    private static async Task<IResult> VerifyDecision(HttpContext http, UserManager<ApplicationUser> users,
        IOpenIddictScopeManager scopeManager, OidcPrincipalFactory principals, CancellationToken ct)
    {
        var form = await http.Request.ReadFormAsync(ct);
        var result = await http.AuthenticateAsync(Server);
        if (!result.Succeeded || result.Principal?.GetClaim(Claims.ClientId) is null)
            return Results.Redirect("/account/device?error=invalid_code");

        var session = await http.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var user = session.Succeeded ? await users.GetUserAsync(session.Principal!) : null;
        if (user is null) return Results.Unauthorized();

        if (form["submit"] != "accept")
            return Results.Forbid(new AuthenticationProperties { RedirectUri = "/account/device?result=denied" }, [Server]);

        var scopes = result.Principal.GetScopes();
        var resources = await scopeManager.ListResourcesAsync(scopes, ct).ToListAsync(ct);
        var principal = await principals.CreateAsync(user, scopes, resources, authorizationId: null, ct);
        return Results.SignIn(principal, new AuthenticationProperties { RedirectUri = "/account/device?result=approved" }, Server);
    }

    private static IResult Forbid(string error, string description) =>
        Results.Forbid(new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
        }), [Server]);

    private static string CurrentAuthorizeUrl(HttpContext http)
    {
        var parameters = http.Request.HasFormContentType
            ? http.Request.Form.SelectMany(p => p.Value.Select(v => KeyValuePair.Create(p.Key, v)))
            : http.Request.Query.SelectMany(p => p.Value.Select(v => KeyValuePair.Create(p.Key, v)));
        return "/connect/authorize" + QueryString.Create(parameters!);
    }
}
