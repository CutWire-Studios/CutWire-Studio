using System.Net.Mail;
using System.Text;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Accounts;
using CutWire.Studio.Domain.Accounts;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Infrastructure.Email;
using CutWire.Studio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace CutWire.Studio.Infrastructure.Identity;

public sealed class AccountService(
    UserManager<ApplicationUser> users,
    StudioSignInManager signIn,
    StudioDbContext db,
    IEmailSender email,
    PublicUrl publicUrl,
    BootstrapAdmin bootstrapAdmin,
    IClock clock,
    CutWire.Studio.Application.Catalog.BlobReferences blobRefs,
    IOpenIddictAuthorizationManager authorizations,
    IOpenIddictTokenManager tokens)
{
    public async Task<Result> SignUpAsync(string? address, string? password, string? handleInput, string? displayNameInput,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        address = address?.Trim();
        if (!IsValidEmail(address)) errors["email"] = ["Enter a valid email address."];
        if (string.IsNullOrEmpty(password)) errors["password"] = ["Password is required."];

        var handle = UserHandle.Normalize(handleInput);
        if (handle.IsFailure) errors["handle"] = handle.Error.Errors!["handle"];

        var displayName = displayNameInput?.Trim();
        if (string.IsNullOrEmpty(displayName)) displayName = handle.IsSuccess ? handle.Value : "";
        if (displayName.Length is 0 or > UserProfile.MaxDisplayNameLength)
            errors["displayName"] = [$"Must be 1-{UserProfile.MaxDisplayNameLength} characters."];

        if (errors.Count > 0) return Error.Validation("account.invalid", "Sign-up is invalid.", errors);

        if (await db.UserProfiles.AnyAsync(p => p.Handle == handle.Value, ct)) return UpdateProfileHandler.HandleTaken;
        if (await users.FindByEmailAsync(address!) is not null) return Result.Success();

        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = address, Email = address };
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var created = await users.CreateAsync(user, password!);
        if (!created.Succeeded) return created.ToError();

        db.UserProfiles.Add(new UserProfile(user.Id, handle.Value, displayName, clock.UtcNow));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return UpdateProfileHandler.HandleTaken;
        }

        await tx.CommitAsync(ct);
        await SendVerificationAsync(user, ct);
        return Result.Success();
    }

    public async Task<Result<string>> SignInAsync(string? address, string? password, bool rememberMe, CancellationToken ct)
    {
        var user = string.IsNullOrWhiteSpace(address) ? null : await users.FindByEmailAsync(address.Trim());
        if (user is null || IsDeleted(user) || string.IsNullOrEmpty(password)) return IdentityErrors.InvalidCredentials;

        var check = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (check.IsLockedOut) return IdentityErrors.LockedOut;
        if (!check.Succeeded) return IdentityErrors.InvalidCredentials;
        if (!user.EmailConfirmed) return SignInStatus.RequiresVerification;

        var result = await signIn.PasswordSignInAsync(user, password, rememberMe, lockoutOnFailure: false);
        if (result.RequiresTwoFactor) return SignInStatus.Requires2Fa;
        return result.Succeeded ? SignInStatus.Ok : IdentityErrors.InvalidCredentials;
    }

    public async Task<Result> SignInTwoFactorAsync(string? code, string? recoveryCode, bool rememberMe, bool rememberMachine)
    {
        SignInResult result;
        if (!string.IsNullOrWhiteSpace(recoveryCode))
            result = await signIn.TwoFactorRecoveryCodeSignInAsync(recoveryCode.Trim());
        else if (!string.IsNullOrWhiteSpace(code))
            result = await signIn.TwoFactorAuthenticatorSignInAsync(NormalizeCode(code), rememberMe, rememberMachine);
        else
            return Error.Validation("auth.code_required", "A code is required.");

        if (result.IsLockedOut) return IdentityErrors.LockedOut;
        return result.Succeeded
            ? Result.Success()
            : result.IsNotAllowed
                ? Error.Unauthorized("auth.two_factor_expired", "Sign in again to continue.")
                : Error.Unauthorized("auth.invalid_code", "The code is incorrect.");
    }

    public Task SignOutAsync() => signIn.SignOutAsync();

    public async Task<Result> VerifyEmailAsync(string? userId, string? token)
    {
        var user = await FindByIdAsync(userId);
        if (user is null || !TryDecode(token, out var decoded)) return IdentityErrors.InvalidToken;

        var result = await users.ConfirmEmailAsync(user, decoded);
        if (!result.Succeeded) return IdentityErrors.InvalidToken;

        await bootstrapAdmin.EnsureAsync(user);
        return Result.Success();
    }

    public async Task<Result> ResendVerificationAsync(string? address, CancellationToken ct)
    {
        var user = string.IsNullOrWhiteSpace(address) ? null : await users.FindByEmailAsync(address.Trim());
        if (user is { EmailConfirmed: false } && !IsDeleted(user)) await SendVerificationAsync(user, ct);
        return Result.Success();
    }

    public async Task<Result> ForgotPasswordAsync(string? address, CancellationToken ct)
    {
        var user = string.IsNullOrWhiteSpace(address) ? null : await users.FindByEmailAsync(address.Trim());
        if (user is { EmailConfirmed: true } && !IsDeleted(user))
        {
            var token = Encode(await users.GeneratePasswordResetTokenAsync(user));
            var link = publicUrl.Absolute($"/account/reset-password?email={Uri.EscapeDataString(user.Email!)}&token={token}");
            await email.SendAsync(EmailTemplates.ResetPassword(user.Email!, link), ct);
        }

        return Result.Success();
    }

    public async Task<Result> ResetPasswordAsync(string? address, string? token, string? newPassword)
    {
        var user = string.IsNullOrWhiteSpace(address) ? null : await users.FindByEmailAsync(address.Trim());
        if (user is null || IsDeleted(user) || !TryDecode(token, out var decoded)) return IdentityErrors.InvalidToken;
        if (string.IsNullOrEmpty(newPassword))
            return Error.Validation("account.invalid", "Password is required.",
                new Dictionary<string, string[]> { ["password"] = ["Password is required."] });

        var result = await users.ResetPasswordAsync(user, decoded, newPassword);
        if (!result.Succeeded)
            return result.Errors.Any(e => e.Code == "InvalidToken") ? IdentityErrors.InvalidToken : result.ToError();

        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        return Result.Success();
    }

    public async Task<Result> ChangePasswordAsync(Guid userId, string? current, string? newPassword)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return IdentityErrors.UserNotFound;
        if (string.IsNullOrEmpty(newPassword))
            return Error.Validation("account.invalid", "Password is required.",
                new Dictionary<string, string[]> { ["password"] = ["Password is required."] });

        IdentityResult result;
        if (await users.HasPasswordAsync(user))
        {
            if (string.IsNullOrEmpty(current)) return IdentityErrors.InvalidCredentials;
            result = await users.ChangePasswordAsync(user, current, newPassword);
            if (result.Errors.Any(e => e.Code == "PasswordMismatch")) return IdentityErrors.InvalidCredentials;
        }
        else
        {
            result = await users.AddPasswordAsync(user, newPassword);
        }

        if (!result.Succeeded) return result.ToError();
        await signIn.RefreshSignInAsync(user);
        return Result.Success();
    }

    public async Task<Result> ChangeEmailAsync(Guid userId, string? newAddress, string? password, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return IdentityErrors.UserNotFound;

        newAddress = newAddress?.Trim();
        if (!IsValidEmail(newAddress))
            return Error.Validation("account.invalid", "Email is invalid.",
                new Dictionary<string, string[]> { ["email"] = ["Enter a valid email address."] });

        if (await users.HasPasswordAsync(user) && (string.IsNullOrEmpty(password) || !await users.CheckPasswordAsync(user, password)))
            return IdentityErrors.InvalidCredentials;

        if (await users.FindByEmailAsync(newAddress!) is not null)
            return Error.Conflict("account.email_taken", "That email address is already in use.");

        var token = Encode(await users.GenerateChangeEmailTokenAsync(user, newAddress!));
        var link = publicUrl.Absolute(
            $"/account/confirm-email-change?userId={user.Id}&email={Uri.EscapeDataString(newAddress!)}&token={token}");
        await email.SendAsync(EmailTemplates.ChangeEmail(newAddress!, link), ct);
        return Result.Success();
    }

    public async Task<Result> ConfirmEmailChangeAsync(string? userId, string? newAddress, string? token)
    {
        var user = await FindByIdAsync(userId);
        if (user is null || string.IsNullOrWhiteSpace(newAddress) || !TryDecode(token, out var decoded))
            return IdentityErrors.InvalidToken;

        var result = await users.ChangeEmailAsync(user, newAddress.Trim(), decoded);
        if (!result.Succeeded)
            return result.Errors.Any(e => e.Code is "InvalidToken") ? IdentityErrors.InvalidToken : result.ToError();

        await users.SetUserNameAsync(user, newAddress.Trim());
        await bootstrapAdmin.EnsureAsync(user);
        return Result.Success();
    }

    public async Task<Result<MeDto>> GetMeAsync(Guid userId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        var profile = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == userId, ct);
        if (user is null || profile is null || IsDeleted(user)) return IdentityErrors.UserNotFound;

        var dto = ProfileDto.From(profile, clock.UtcNow);
        var roles = (await users.GetRolesAsync(user)).Order().ToArray();
        return new MeDto(user.Id, dto.Handle, dto.DisplayName, dto.AvatarUrl, user.Email!, user.EmailConfirmed, roles,
            dto.TrustLevel, dto.Banned);
    }

    public async Task<Result> SignOutEverywhereAsync(Guid userId)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return IdentityErrors.UserNotFound;

        await users.UpdateSecurityStampAsync(user);
        await signIn.RefreshSignInAsync(user);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid userId, string? password, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return IdentityErrors.UserNotFound;
        if (await users.HasPasswordAsync(user) && (string.IsNullOrEmpty(password) || !await users.CheckPasswordAsync(user, password)))
            return IdentityErrors.InvalidCredentials;

        var now = clock.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.Id == userId, ct);
        if (profile?.Anonymize($"deleted-{userId:N}"[..16], now) is { } avatar)
            await blobRefs.ReleaseAsync(avatar, ct);

        foreach (var pat in await db.PersonalAccessTokens.Where(t => t.UserId == userId).ToListAsync(ct)) pat.Revoke(now);
        await db.SaveChangesAsync(ct);

        foreach (var login in await users.GetLoginsAsync(user))
            await users.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey);
        foreach (var passkey in await users.GetPasskeysAsync(user))
            await users.RemovePasskeyAsync(user, passkey.CredentialId);
        if (await users.HasPasswordAsync(user)) await users.RemovePasswordAsync(user);
        await users.SetTwoFactorEnabledAsync(user, false);

        var anonymous = $"deleted-{userId:N}@deleted.invalid";
        await users.SetUserNameAsync(user, anonymous);
        await users.SetEmailAsync(user, anonymous);
        await users.SetLockoutEnabledAsync(user, true);
        await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        await users.UpdateSecurityStampAsync(user);

        var subject = userId.ToString();
        await authorizations.RevokeBySubjectAsync(subject, ct);
        await tokens.RevokeBySubjectAsync(subject, ct);

        await tx.CommitAsync(ct);
        await signIn.SignOutAsync();
        return Result.Success();
    }

    public static bool IsDeleted(ApplicationUser user) => user.LockoutEnd == DateTimeOffset.MaxValue;

    private async Task SendVerificationAsync(ApplicationUser user, CancellationToken ct)
    {
        var token = Encode(await users.GenerateEmailConfirmationTokenAsync(user));
        var link = publicUrl.Absolute($"/account/verify-email?userId={user.Id}&token={token}");
        await email.SendAsync(EmailTemplates.VerifyEmail(user.Email!, link), ct);
    }

    private async Task<ApplicationUser?> FindByIdAsync(string? id) =>
        Guid.TryParse(id, out _) ? await users.FindByIdAsync(id!) : null;

    internal static string NormalizeCode(string value) => value.Replace(" ", "").Replace("-", "");

    internal static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    internal static bool TryDecode(string? value, out string token)
    {
        token = "";
        if (string.IsNullOrEmpty(value)) return false;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(value));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    internal static bool IsValidEmail(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 254 && MailAddress.TryCreate(value, out var parsed) &&
        parsed.Address == value && parsed.Host.Contains('.');
}
