using CutWire.Studio.Api.Auth;
using CutWire.Studio.Api.Http;
using CutWire.Studio.Infrastructure.Identity;

namespace CutWire.Studio.Api.Endpoints;

public sealed record SignUpRequest(string? Email, string? Password, string? Handle, string? DisplayName);

public sealed record SignInRequest(string? Email, string? Password, bool? RememberMe);

public sealed record SignInTwoFactorRequest(string? Code, string? RecoveryCode, bool? RememberMe, bool? RememberMachine);

public sealed record StatusResponse(string Status);

public sealed record VerifyEmailRequest(string? UserId, string? Token);

public sealed record EmailRequest(string? Email);

public sealed record ResetPasswordRequest(string? Email, string? Token, string? NewPassword);

public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

public sealed record ChangeEmailRequest(string? NewEmail, string? Password);

public sealed record ConfirmEmailChangeRequest(string? UserId, string? Email, string? Token);

public sealed record PasswordRequest(string? Password);

public static class AccountEndpoints
{
    public static void MapAccount(this RouteGroupBuilder api)
    {
        api.MapGet("/me", async (HttpContext http, AccountService accounts, CancellationToken ct) =>
                (await accounts.GetMeAsync(http.User.GetUserId(), ct)).ToHttpResult(Results.Ok))
            .WithName("GetMe")
            .WithTags("Account")
            .RequireAuthorization()
            .Produces<MeDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        var account = api.MapGroup("/account").WithTags("Account");

        account.MapPost("/sign-up", async (SignUpRequest body, AccountService accounts, CancellationToken ct) =>
                (await accounts.SignUpAsync(body.Email, body.Password, body.Handle, body.DisplayName, ct))
                .ToHttpResult(() => Results.Accepted(value: new StatusResponse("verification_sent"))))
            .WithName("SignUp")
            .RequireRateLimiting(RateLimitingSettings.StrictPolicy)
            .Produces<StatusResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        account.MapPost("/sign-in", async (SignInRequest body, AccountService accounts, CancellationToken ct) =>
                (await accounts.SignInAsync(body.Email, body.Password, body.RememberMe ?? true, ct))
                .ToHttpResult(status => Results.Ok(new StatusResponse(status))))
            .WithName("SignIn")
            .RequireRateLimiting(RateLimitingSettings.StrictPolicy)
            .Produces<StatusResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        account.MapPost("/sign-in-2fa", async (SignInTwoFactorRequest body, AccountService accounts) =>
                (await accounts.SignInTwoFactorAsync(body.Code, body.RecoveryCode, body.RememberMe ?? true, body.RememberMachine ?? false))
                .ToHttpResult(() => Results.Ok(new StatusResponse(SignInStatus.Ok))))
            .WithName("SignInTwoFactor")
            .RequireRateLimiting(RateLimitingSettings.StrictPolicy)
            .Produces<StatusResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        account.MapPost("/sign-out", async (AccountService accounts) =>
            {
                await accounts.SignOutAsync();
                return Results.NoContent();
            })
            .WithName("SignOut")
            .Produces(StatusCodes.Status204NoContent);

        account.MapPost("/verify-email", async (VerifyEmailRequest body, AccountService accounts) =>
                (await accounts.VerifyEmailAsync(body.UserId, body.Token)).ToHttpResult(() => Results.NoContent()))
            .WithName("VerifyEmail")
            .RequireRateLimiting(RateLimitingSettings.StrictPolicy)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        account.MapPost("/resend-verification", async (EmailRequest body, AccountService accounts, CancellationToken ct) =>
                (await accounts.ResendVerificationAsync(body.Email, ct)).ToHttpResult(() => Results.Accepted()))
            .WithName("ResendVerification")
            .RequireRateLimiting(RateLimitingSettings.StrictPolicy)
            .Produces(StatusCodes.Status202Accepted);

        account.MapPost("/forgot-password", async (EmailRequest body, AccountService accounts, CancellationToken ct) =>
                (await accounts.ForgotPasswordAsync(body.Email, ct)).ToHttpResult(() => Results.Accepted()))
            .WithName("ForgotPassword")
            .RequireRateLimiting(RateLimitingSettings.StrictPolicy)
            .Produces(StatusCodes.Status202Accepted);

        account.MapPost("/reset-password", async (ResetPasswordRequest body, AccountService accounts) =>
                (await accounts.ResetPasswordAsync(body.Email, body.Token, body.NewPassword)).ToHttpResult(() => Results.NoContent()))
            .WithName("ResetPassword")
            .RequireRateLimiting(RateLimitingSettings.StrictPolicy)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        account.MapPost("/confirm-email-change", async (ConfirmEmailChangeRequest body, AccountService accounts) =>
                (await accounts.ConfirmEmailChangeAsync(body.UserId, body.Email, body.Token)).ToHttpResult(() => Results.NoContent()))
            .WithName("ConfirmEmailChange")
            .RequireRateLimiting(RateLimitingSettings.StrictPolicy)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        var session = account.MapGroup("").RequireAuthorization(AuthPolicies.Session)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        session.MapPost("/change-password", async (ChangePasswordRequest body, HttpContext http, AccountService accounts) =>
                (await accounts.ChangePasswordAsync(http.User.GetUserId(), body.CurrentPassword, body.NewPassword))
                .ToHttpResult(() => Results.NoContent()))
            .WithName("ChangePassword")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        session.MapPost("/change-email", async (ChangeEmailRequest body, HttpContext http, AccountService accounts, CancellationToken ct) =>
                (await accounts.ChangeEmailAsync(http.User.GetUserId(), body.NewEmail, body.Password, ct))
                .ToHttpResult(() => Results.Accepted()))
            .WithName("ChangeEmail")
            .RequireRateLimiting(RateLimitingSettings.StrictPolicy)
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        session.MapPost("/sessions/sign-out-everywhere", async (HttpContext http, AccountService accounts) =>
                (await accounts.SignOutEverywhereAsync(http.User.GetUserId())).ToHttpResult(() => Results.NoContent()))
            .WithName("SignOutEverywhere")
            .Produces(StatusCodes.Status204NoContent);

        session.MapPost("/delete", async (PasswordRequest body, HttpContext http, AccountService accounts, CancellationToken ct) =>
                (await accounts.DeleteAsync(http.User.GetUserId(), body.Password, ct)).ToHttpResult(() => Results.NoContent()))
            .WithName("DeleteAccount")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}
