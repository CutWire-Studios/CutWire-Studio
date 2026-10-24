using System.Security.Claims;
using System.Text.RegularExpressions;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Accounts;
using CutWire.Studio.Domain.Accounts;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Infrastructure.Identity;

public sealed record ExternalProviderDto(string Name, string DisplayName);

public sealed record PendingProfileDto(string Provider, string Email, string SuggestedHandle, string SuggestedDisplayName);

public sealed record ExternalCallbackResult(string Kind, string? ErrorCode = null)
{
    public const string SignedIn = "signed_in";
    public const string PendingProfile = "pending_profile";
    public const string Failed = "error";
}

public sealed partial class ExternalLoginService(
    UserManager<ApplicationUser> users,
    StudioSignInManager signIn,
    StudioDbContext db,
    IClock clock,
    IHttpContextAccessor http)
{
    public const string EmailVerifiedClaim = "studio:email_verified";

    public async Task<IReadOnlyList<ExternalProviderDto>> ListProvidersAsync() =>
        (await signIn.GetExternalAuthenticationSchemesAsync())
        .Select(s => new ExternalProviderDto(s.Name, s.DisplayName ?? s.Name))
        .ToList();

    public async Task<Result<(string Scheme, AuthenticationProperties Properties)>> ChallengeAsync(string provider, string redirectUrl)
    {
        var scheme = (await signIn.GetExternalAuthenticationSchemesAsync())
            .FirstOrDefault(s => string.Equals(s.Name, provider, StringComparison.OrdinalIgnoreCase));
        if (scheme is null) return Error.NotFound("external.unknown_provider", "Unknown or disabled provider.");

        return (scheme.Name, signIn.ConfigureExternalAuthenticationProperties(scheme.Name, redirectUrl));
    }

    public async Task<ExternalCallbackResult> CallbackAsync()
    {
        var info = await signIn.GetExternalLoginInfoAsync();
        if (info is null) return Failed("external_failed");

        var result = await signIn.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: true,
            bypassTwoFactor: true);
        if (result.Succeeded) return new(ExternalCallbackResult.SignedIn);
        if (result.IsLockedOut) return Failed("locked_out");

        var (address, verified) = Extract(info.Principal);
        if (address is null || !verified) return Failed("external_email_unverified");

        var user = await users.FindByEmailAsync(address);
        if (user is null) return new(ExternalCallbackResult.PendingProfile);
        if (!user.EmailConfirmed || AccountService.IsDeleted(user)) return Failed("external_email_conflict");

        var linked = await users.AddLoginAsync(user, info);
        if (!linked.Succeeded) return Failed("external_failed");

        await signIn.SignInAsync(user, isPersistent: true, info.LoginProvider);
        await SignOutExternalAsync();
        return new(ExternalCallbackResult.SignedIn);
    }

    public async Task<Result<PendingProfileDto>> PendingAsync()
    {
        var info = await signIn.GetExternalLoginInfoAsync();
        if (info is null) return SessionExpired;

        var (address, verified) = Extract(info.Principal);
        if (address is null || !verified) return SessionExpired;

        var name = info.Principal.FindFirstValue(ClaimTypes.Name);
        return new PendingProfileDto(info.LoginProvider, address, SuggestHandle(name, address),
            string.IsNullOrWhiteSpace(name) ? address.Split('@')[0] : name.Trim());
    }

    public async Task<Result> CompleteAsync(string? handleInput, string? displayNameInput, CancellationToken ct)
    {
        var info = await signIn.GetExternalLoginInfoAsync();
        if (info is null) return SessionExpired;
        var (address, verified) = Extract(info.Principal);
        if (address is null || !verified) return SessionExpired;

        var handle = UserHandle.Normalize(handleInput);
        if (handle.IsFailure) return handle.Error;

        var displayName = displayNameInput?.Trim();
        if (string.IsNullOrEmpty(displayName)) displayName = info.Principal.FindFirstValue(ClaimTypes.Name)?.Trim();
        if (string.IsNullOrEmpty(displayName)) displayName = handle.Value;
        if (displayName.Length > UserProfile.MaxDisplayNameLength) displayName = displayName[..UserProfile.MaxDisplayNameLength];

        if (await db.UserProfiles.AnyAsync(p => p.Handle == handle.Value, ct)) return UpdateProfileHandler.HandleTaken;
        if (await users.FindByEmailAsync(address) is not null)
            return Error.Conflict("external.email_conflict", "An account with this email already exists.");

        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = address, Email = address, EmailConfirmed = true };
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var created = await users.CreateAsync(user);
        if (!created.Succeeded) return created.ToError();
        var linked = await users.AddLoginAsync(user, info);
        if (!linked.Succeeded) return linked.ToError();

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
        await signIn.SignInAsync(user, isPersistent: true, info.LoginProvider);
        await SignOutExternalAsync();
        return Result.Success();
    }

    private static readonly Error SessionExpired =
        Error.Unauthorized("external.session_expired", "The external sign-in has expired. Start again.");

    private static ExternalCallbackResult Failed(string code) => new(ExternalCallbackResult.Failed, code);

    private Task SignOutExternalAsync() => http.HttpContext!.SignOutAsync(IdentityConstants.ExternalScheme);

    private static (string? Email, bool Verified) Extract(ClaimsPrincipal principal)
    {
        var address = principal.FindFirstValue(ClaimTypes.Email)?.Trim();
        var verified = string.Equals(principal.FindFirstValue(EmailVerifiedClaim), "true", StringComparison.OrdinalIgnoreCase);
        return (string.IsNullOrEmpty(address) ? null : address, verified);
    }

    private static string SuggestHandle(string? name, string address)
    {
        var source = string.IsNullOrWhiteSpace(name) ? address.Split('@')[0] : name;
        var cleaned = Invalid().Replace(source.ToLowerInvariant(), "-").Trim('-', '_');
        if (cleaned.Length < UserHandle.MinLength) cleaned = "user-" + cleaned;
        return cleaned.Length > UserHandle.MaxLength ? cleaned[..UserHandle.MaxLength] : cleaned;
    }

    [GeneratedRegex("[^a-z0-9_-]+")]
    private static partial Regex Invalid();
}
