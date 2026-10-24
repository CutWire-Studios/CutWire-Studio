using CutWire.Studio.Domain.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace CutWire.Studio.Infrastructure.Identity;

public sealed record PasskeyDto(string Id, string Name, DateTimeOffset CreatedAt, bool IsBackedUp, bool IsUserVerified);

public sealed class PasskeyService(UserManager<ApplicationUser> users, StudioSignInManager signIn)
{
    public const int MaxNameLength = 64;

    public async Task<Result<string>> CreationOptionsAsync(Guid userId, string displayName)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return IdentityErrors.UserNotFound;

        return await signIn.MakePasskeyCreationOptionsAsync(
            new PasskeyUserEntity { Id = user.Id.ToString(), Name = user.Email!, DisplayName = displayName });
    }

    public async Task<Result<PasskeyDto>> RegisterAsync(Guid userId, string credentialJson, string? name)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return IdentityErrors.UserNotFound;

        PasskeyAttestationResult attestation;
        try
        {
            attestation = await signIn.PerformPasskeyAttestationAsync(credentialJson);
        }
        catch (InvalidOperationException)
        {
            return NoChallenge;
        }

        if (!attestation.Succeeded)
            return Error.Validation("passkey.invalid", "The passkey could not be verified.");

        var passkey = attestation.Passkey;
        passkey.Name = CleanName(name) ?? "Passkey";
        var stored = await users.AddOrUpdatePasskeyAsync(user, passkey);
        return stored.Succeeded ? ToDto(passkey) : stored.ToError("passkey.invalid");
    }

    public async Task<string> RequestOptionsAsync() => await signIn.MakePasskeyRequestOptionsAsync(null);

    public async Task<Result> SignInAsync(string credentialJson)
    {
        SignInResult result;
        try
        {
            result = await signIn.PasskeySignInAsync(credentialJson);
        }
        catch (InvalidOperationException)
        {
            return Error.Unauthorized("auth.invalid_passkey", "Request passkey options first.");
        }

        if (result.IsLockedOut) return IdentityErrors.LockedOut;
        return result.Succeeded
            ? Result.Success()
            : Error.Unauthorized("auth.invalid_passkey", "The passkey could not be verified.");
    }

    public async Task<Result<IReadOnlyList<PasskeyDto>>> ListAsync(Guid userId)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return IdentityErrors.UserNotFound;

        return (await users.GetPasskeysAsync(user)).OrderBy(p => p.CreatedAt).Select(ToDto).ToList();
    }

    public async Task<Result<PasskeyDto>> RenameAsync(Guid userId, string id, string? name)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return IdentityErrors.UserNotFound;
        var clean = CleanName(name);
        if (clean is null)
            return Error.Validation("passkey.invalid_name", "Name is invalid.",
                new Dictionary<string, string[]> { ["name"] = [$"Must be 1-{MaxNameLength} characters."] });

        var passkey = TryDecode(id, out var credentialId) ? await users.GetPasskeyAsync(user, credentialId) : null;
        if (passkey is null) return Error.NotFound("passkey.not_found", "Passkey not found.");

        passkey.Name = clean;
        var stored = await users.AddOrUpdatePasskeyAsync(user, passkey);
        return stored.Succeeded ? ToDto(passkey) : stored.ToError("passkey.invalid");
    }

    public async Task<Result> DeleteAsync(Guid userId, string id)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return IdentityErrors.UserNotFound;
        if (!TryDecode(id, out var credentialId) || await users.GetPasskeyAsync(user, credentialId) is null)
            return Error.NotFound("passkey.not_found", "Passkey not found.");

        var removed = await users.RemovePasskeyAsync(user, credentialId);
        return removed.Succeeded ? Result.Success() : removed.ToError("passkey.invalid");
    }

    private static readonly Error NoChallenge =
        Error.Validation("passkey.no_challenge", "Request passkey creation options first.");

    private static PasskeyDto ToDto(UserPasskeyInfo p) =>
        new(WebEncoders.Base64UrlEncode(p.CredentialId), p.Name ?? "Passkey", p.CreatedAt, p.IsBackedUp, p.IsUserVerified);

    private static string? CleanName(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxNameLength ? null : trimmed;
    }

    private static bool TryDecode(string value, out byte[] bytes)
    {
        try
        {
            bytes = WebEncoders.Base64UrlDecode(value);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }
}
