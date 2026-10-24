using CutWire.Studio.Domain.Common;

namespace CutWire.Studio.Domain.Accounts;

public sealed class PersonalAccessToken : Entity<Guid>
{
    private PersonalAccessToken() { }

    public PersonalAccessToken(Guid userId, string name, string tokenHash, string[] scopes, DateTimeOffset expiresAt,
        DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        Name = name;
        TokenHash = tokenHash;
        Scopes = scopes;
        ExpiresAt = expiresAt;
        CreatedAt = now;
    }

    public Guid UserId { get; private set; }
    public string Name { get; private set; } = null!;
    public string TokenHash { get; private set; } = null!;
    public string[] Scopes { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;

    public void MarkUsed(DateTimeOffset now) => LastUsedAt = now;
}
