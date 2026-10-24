using System.Text.Json;
using CutWire.Studio.Api.Auth;
using CutWire.Studio.Api.Http;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Accounts;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Infrastructure.Identity;

namespace CutWire.Studio.Api.Endpoints;

public sealed record CodeRequest(string? Code);

public sealed record RecoveryCodesResponse(string[] RecoveryCodes);

public sealed record PasskeyRegisterRequest(JsonElement Credential, string? Name);

public sealed record PasskeySignInRequest(JsonElement Credential);

public sealed record PasskeyRenameRequest(string? Name);

public static class SecurityEndpoints
{
    public static void MapTwoFactor(this RouteGroupBuilder account)
    {
        var group = account.MapGroup("/2fa").WithTags("Two-factor").RequireAuthorization(AuthPolicies.Session)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("", async (HttpContext http, TwoFactorService service) =>
                (await service.StatusAsync(http.User.GetUserId())).ToHttpResult(Results.Ok))
            .WithName("GetTwoFactor")
            .Produces<TwoFactorStatus>();

        group.MapPost("/setup", async (HttpContext http, TwoFactorService service) =>
                (await service.SetupAsync(http.User.GetUserId())).ToHttpResult(Results.Ok))
            .WithName("SetupTwoFactor")
            .Produces<TwoFactorSetup>()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/enable", async (CodeRequest body, HttpContext http, TwoFactorService service) =>
                (await service.EnableAsync(http.User.GetUserId(), body.Code))
                .ToHttpResult(codes => Results.Ok(new RecoveryCodesResponse(codes))))
            .WithName("EnableTwoFactor")
            .Produces<RecoveryCodesResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/disable", async (PasswordRequest body, HttpContext http, TwoFactorService service) =>
                (await service.DisableAsync(http.User.GetUserId(), body.Password)).ToHttpResult(() => Results.NoContent()))
            .WithName("DisableTwoFactor")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/recovery-codes", async (PasswordRequest body, HttpContext http, TwoFactorService service) =>
                (await service.RegenerateRecoveryCodesAsync(http.User.GetUserId(), body.Password))
                .ToHttpResult(codes => Results.Ok(new RecoveryCodesResponse(codes))))
            .WithName("RegenerateRecoveryCodes")
            .Produces<RecoveryCodesResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    public static void MapPasskeys(this RouteGroupBuilder account)
    {
        var anonymous = account.MapGroup("/passkeys").WithTags("Passkeys");

        anonymous.MapPost("/request-options", async (PasskeyService service) =>
                Results.Content(await service.RequestOptionsAsync(), "application/json"))
            .WithName("PasskeyRequestOptions")
            .RequireRateLimiting(RateLimitingSettings.StrictPolicy)
            .Produces<string>(contentType: "application/json");

        anonymous.MapPost("/sign-in", async (PasskeySignInRequest body, PasskeyService service) =>
                (await service.SignInAsync(body.Credential.GetRawText())).ToHttpResult(() => Results.Ok(new StatusResponse(SignInStatus.Ok))))
            .WithName("PasskeySignIn")
            .RequireRateLimiting(RateLimitingSettings.StrictPolicy)
            .Produces<StatusResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        var session = anonymous.MapGroup("").RequireAuthorization(AuthPolicies.Session)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        session.MapPost("/creation-options", async (HttpContext http, PasskeyService service,
                    IQueryHandler<GetProfileQuery, ProfileDto> profiles, CancellationToken ct) =>
            {
                var userId = http.User.GetUserId();
                var profile = await profiles.Handle(new GetProfileQuery(userId), ct);
                if (profile.IsFailure) return profile.Error.ToHttpResult();
                return (await service.CreationOptionsAsync(userId, profile.Value.DisplayName))
                    .ToHttpResult(json => Results.Content(json, "application/json"));
            })
            .WithName("PasskeyCreationOptions")
            .Produces<string>(contentType: "application/json");

        session.MapPost("", async (PasskeyRegisterRequest body, HttpContext http, PasskeyService service) =>
                (await service.RegisterAsync(http.User.GetUserId(), body.Credential.GetRawText(), body.Name))
                .ToHttpResult(passkey => Results.Created($"/api/v1/account/passkeys/{passkey.Id}", passkey)))
            .WithName("RegisterPasskey")
            .Produces<PasskeyDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        session.MapGet("", async (HttpContext http, PasskeyService service) =>
                (await service.ListAsync(http.User.GetUserId())).ToHttpResult(Results.Ok))
            .WithName("ListPasskeys")
            .Produces<IReadOnlyList<PasskeyDto>>();

        session.MapPatch("/{id}", async (string id, PasskeyRenameRequest body, HttpContext http, PasskeyService service) =>
                (await service.RenameAsync(http.User.GetUserId(), id, body.Name)).ToHttpResult(Results.Ok))
            .WithName("RenamePasskey")
            .Produces<PasskeyDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        session.MapDelete("/{id}", async (string id, HttpContext http, PasskeyService service) =>
                (await service.DeleteAsync(http.User.GetUserId(), id)).ToHttpResult(() => Results.NoContent()))
            .WithName("DeletePasskey")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
