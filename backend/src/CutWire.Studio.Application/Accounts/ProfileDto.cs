using CutWire.Studio.Domain.Accounts;

namespace CutWire.Studio.Application.Accounts;

public sealed record ProfileDto(
    Guid UserId,
    string Handle,
    string DisplayName,
    string Bio,
    string? AvatarUrl,
    TrustLevel TrustLevel,
    bool Banned)
{
    public static ProfileDto From(UserProfile profile, DateTimeOffset now) => new(
        profile.Id,
        profile.Handle,
        profile.DisplayName,
        profile.Bio,
        profile.AvatarSha256 is { } sha ? $"/files/{sha}" : null,
        profile.TrustLevel,
        profile.IsBanned(now));
}
