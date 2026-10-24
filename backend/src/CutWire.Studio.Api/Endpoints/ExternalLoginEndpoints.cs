using CutWire.Studio.Api.Auth;
using CutWire.Studio.Api.Http;
using CutWire.Studio.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;

namespace CutWire.Studio.Api.Endpoints;

public sealed record CompleteProfileRequest(string? Handle, string? DisplayName);

public static class ExternalLoginEndpoints
{
    public static void MapExternalLogins(this RouteGroupBuilder account)
    {
        var group = account.MapGroup("/external").WithTags("External logins");

        group.MapGet("", async (ExternalLoginService service) => Results.Ok(await service.ListProvidersAsync()))
            .WithName("ListExternalProviders")
            .Produces<IReadOnlyList<ExternalProviderDto>>();

        group.MapGet("/callback", async (string? @return, ExternalLoginService service) =>
            {
                var target = ResultExtensions.SafeLocalPath(@return);
                var result = await service.CallbackAsync();
                return result.Kind switch
                {
                    ExternalCallbackResult.SignedIn => Results.Redirect(target),
                    ExternalCallbackResult.PendingProfile =>
                        Results.Redirect($"/account/pending-profile?return={Uri.EscapeDataString(target)}"),
                    _ => Results.Redirect(
                        $"/account/sign-in?error={Uri.EscapeDataString(result.ErrorCode ?? "external_failed")}&return={Uri.EscapeDataString(target)}"),
                };
            })
            .WithName("ExternalLoginCallback")
            .Produces(StatusCodes.Status302Found);

        group.MapGet("/pending", async (ExternalLoginService service) => (await service.PendingAsync()).ToHttpResult(Results.Ok))
            .WithName("GetPendingProfile")
            .Produces<PendingProfileDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/complete", async (CompleteProfileRequest body, ExternalLoginService service, CancellationToken ct) =>
                (await service.CompleteAsync(body.Handle, body.DisplayName, ct))
                .ToHttpResult(() => Results.Ok(new StatusResponse(SignInStatus.Ok))))
            .WithName("CompleteExternalProfile")
            .RequireRateLimiting(RateLimitingSettings.StrictPolicy)
            .Produces<StatusResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{provider}", async (string provider, string? @return, ExternalLoginService service) =>
            {
                var target = ResultExtensions.SafeLocalPath(@return);
                var challenge = await service.ChallengeAsync(provider,
                    $"/api/v1/account/external/callback?return={Uri.EscapeDataString(target)}");
                return challenge.ToHttpResult(c => Results.Challenge(c.Properties, [c.Scheme]));
            })
            .WithName("ExternalLoginChallenge")
            .Produces(StatusCodes.Status302Found)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
