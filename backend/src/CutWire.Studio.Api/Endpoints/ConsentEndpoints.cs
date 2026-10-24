using CutWire.Studio.Api.Auth;
using CutWire.Studio.Api.Http;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Infrastructure.Identity;
using CutWire.Studio.Infrastructure.Oidc;

namespace CutWire.Studio.Api.Endpoints;

public sealed record ConsentRequest(string? Return, bool Approve);

public sealed record ConsentResponse(string Redirect);

public static class ConsentEndpoints
{
    public static void MapConsent(this RouteGroupBuilder account)
    {
        var group = account.MapGroup("").WithTags("Consent").RequireAuthorization(AuthPolicies.Session)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/consent-info", async (string? @return, HttpContext http, ConsentService consent, CancellationToken ct) =>
            {
                var (request, application) = await ResolveAsync(consent, @return, ct);
                if (application is null) return UnknownClient.ToHttpResult();
                return Results.Ok(await consent.DescribeAsync(http.User.GetUserId(), application, request!.Scopes, ct));
            })
            .WithName("GetConsentInfo")
            .Produces<ConsentInfo>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/consent", async (ConsentRequest body, HttpContext http, ConsentService consent, PublicUrl publicUrl, CancellationToken ct) =>
            {
                var (request, application) = await ResolveAsync(consent, body.Return, ct);
                if (application is null) return UnknownClient.ToHttpResult();

                if (body.Approve)
                {
                    await consent.GrantAsync(http.User.GetUserId(), application, request!.Scopes, ct);
                    return Results.Ok(new ConsentResponse(body.Return!));
                }

                var denied = await consent.BuildDeniedRedirectAsync(application, request!, publicUrl.Base, ct);
                return denied is null
                    ? Error.Validation("consent.invalid_redirect", "The redirect URI is not registered for this client.").ToHttpResult()
                    : Results.Ok(new ConsentResponse(denied));
            })
            .WithName("SubmitConsent")
            .Produces<ConsentResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static readonly Error UnknownClient = Error.NotFound("consent.unknown_client", "Unknown authorization request.");

    private static async Task<(AuthorizeRequestInfo? Request, object? Application)> ResolveAsync(
        ConsentService consent, string? returnUrl, CancellationToken ct)
    {
        var request = ConsentService.ParseReturn(returnUrl);
        return request is null ? (null, null) : (request, await consent.FindClientAsync(request.ClientId, ct));
    }
}
