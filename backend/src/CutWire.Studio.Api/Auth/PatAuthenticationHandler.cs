using System.Security.Claims;
using System.Text.Encodings.Web;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Domain.Accounts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace CutWire.Studio.Api.Auth;

public sealed class PatAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IAppDbContext db,
    IClock clock) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private static readonly TimeSpan LastUsedGranularity = TimeSpan.FromMinutes(5);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();

        var secret = header["Bearer ".Length..].Trim();
        if (!PatToken.HasPrefix(secret)) return AuthenticateResult.NoResult();

        var hash = PatToken.Hash(secret);
        var token = await db.PersonalAccessTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, Context.RequestAborted);
        var now = clock.UtcNow;
        if (token is null || !token.IsActive(now)) return AuthenticateResult.Fail("Invalid token.");

        if (token.LastUsedAt is null || now - token.LastUsedAt > LastUsedGranularity)
        {
            token.MarkUsed(now);
            await db.SaveChangesAsync(Context.RequestAborted);
        }

        var identity = new ClaimsIdentity(AuthSchemes.Pat, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, token.UserId.ToString());
        identity.SetScopes(token.Scopes);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
        return Task.CompletedTask;
    }
}
