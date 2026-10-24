using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Common;

namespace CutWire.Studio.Domain.Accounts;

public sealed class UserProfile : Entity<Guid>
{
    public const int MaxDisplayNameLength = 50;
    public const int MaxBioLength = 500;
    public const string DeletedDisplayName = "Deleted user";

    private UserProfile() { }

    public UserProfile(Guid userId, string handle, string displayName, DateTimeOffset now)
    {
        Id = userId;
        Handle = handle;
        DisplayName = displayName;
        Bio = "";
        CreatedAt = now;
    }

    public string Handle { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string Bio { get; private set; } = null!;
    public Sha256Hash? AvatarSha256 { get; private set; }
    public TrustLevel TrustLevel { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? BannedUntil { get; private set; }
    public bool IsPermanentlyBanned { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsBanned(DateTimeOffset now) => IsPermanentlyBanned || BannedUntil > now;

    public void SetBan(DateTimeOffset? until, bool permanent)
    {
        BannedUntil = until;
        IsPermanentlyBanned = permanent;
    }

    public void Update(string handle, string displayName, string bio)
    {
        Handle = handle;
        DisplayName = displayName;
        Bio = bio;
    }

    public Sha256Hash? SetAvatar(Sha256Hash? avatar)
    {
        var previous = AvatarSha256;
        AvatarSha256 = avatar;
        return previous;
    }

    public Sha256Hash? Anonymize(string handle, DateTimeOffset now)
    {
        var previous = SetAvatar(null);
        Update(handle, DeletedDisplayName, "");
        DeletedAt = now;
        return previous;
    }
}
