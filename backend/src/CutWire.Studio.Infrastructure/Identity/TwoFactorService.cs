using CutWire.Studio.Domain.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Infrastructure.Identity;

public sealed record TwoFactorStatus(bool Enabled, bool HasAuthenticator, int RecoveryCodesLeft);

public sealed record TwoFactorSetup(string SharedKey, string OtpAuthUri);

public sealed class TwoFactorService(UserManager<ApplicationUser> users, IOptions<IdentityOptions> options)
{
    private const string Issuer = "CutWire Studio";
    private const int RecoveryCodeCount = 10;

    public async Task<Result<TwoFactorStatus>> StatusAsync(Guid userId)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return IdentityErrors.UserNotFound;

        return new TwoFactorStatus(
            await users.GetTwoFactorEnabledAsync(user),
            await users.GetAuthenticatorKeyAsync(user) is not null,
            await users.CountRecoveryCodesAsync(user));
    }

    public async Task<Result<TwoFactorSetup>> SetupAsync(Guid userId)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return IdentityErrors.UserNotFound;
        if (await users.GetTwoFactorEnabledAsync(user))
            return Error.Conflict("two_factor.already_enabled", "Two-factor authentication is already enabled.");

        await users.ResetAuthenticatorKeyAsync(user);
        var key = (await users.GetAuthenticatorKeyAsync(user))!;
        var label = Uri.EscapeDataString($"{Issuer}:{user.Email}");
        return new TwoFactorSetup(key,
            $"otpauth://totp/{label}?secret={key}&issuer={Uri.EscapeDataString(Issuer)}&digits=6");
    }

    public async Task<Result<string[]>> EnableAsync(Guid userId, string? code)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return IdentityErrors.UserNotFound;
        if (await users.GetTwoFactorEnabledAsync(user))
            return Error.Conflict("two_factor.already_enabled", "Two-factor authentication is already enabled.");
        if (await users.GetAuthenticatorKeyAsync(user) is null)
            return Error.Validation("two_factor.not_set_up", "Start setup first.");

        var valid = !string.IsNullOrWhiteSpace(code) && await users.VerifyTwoFactorTokenAsync(
            user, options.Value.Tokens.AuthenticatorTokenProvider, AccountService.NormalizeCode(code));
        if (!valid) return Error.Validation("two_factor.invalid_code", "The code is incorrect.");

        await users.SetTwoFactorEnabledAsync(user, true);
        return (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount))!.ToArray();
    }

    public async Task<Result> DisableAsync(Guid userId, string? password)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return IdentityErrors.UserNotFound;
        if (!await PasswordOkAsync(user, password)) return IdentityErrors.InvalidCredentials;

        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user);
        return Result.Success();
    }

    public async Task<Result<string[]>> RegenerateRecoveryCodesAsync(Guid userId, string? password)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return IdentityErrors.UserNotFound;
        if (!await users.GetTwoFactorEnabledAsync(user))
            return Error.Conflict("two_factor.not_enabled", "Two-factor authentication is not enabled.");
        if (!await PasswordOkAsync(user, password)) return IdentityErrors.InvalidCredentials;

        return (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount))!.ToArray();
    }

    private async Task<bool> PasswordOkAsync(ApplicationUser user, string? password) =>
        !await users.HasPasswordAsync(user) || (!string.IsNullOrEmpty(password) && await users.CheckPasswordAsync(user, password));
}
