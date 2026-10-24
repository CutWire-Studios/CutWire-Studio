using CutWire.Studio.Domain.Accounts;

namespace CutWire.Studio.Infrastructure.Identity;

public static class SignInStatus
{
    public const string Ok = "ok";
    public const string Requires2Fa = "requires_2fa";
    public const string RequiresVerification = "requires_verification";
    public const string PendingProfile = "pending_profile";
}

public sealed record MeDto(
    Guid Id,
    string Handle,
    string DisplayName,
    string? AvatarUrl,
    string Email,
    bool EmailVerified,
    string[] Roles,
    TrustLevel TrustLevel,
    bool Banned);
