using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Domain.Accounts;
using CutWire.Studio.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Application.Accounts;

public sealed record PatDto(
    Guid Id,
    string Name,
    string[] Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? LastUsedAt,
    bool Active);

public sealed record CreatedPatDto(PatDto Token, string Secret);

public sealed record CreatePatCommand(Guid UserId, string? Name, string[]? Scopes, DateTimeOffset? ExpiresAt);

public sealed record ListPatsQuery(Guid UserId);

public sealed record RevokePatCommand(Guid UserId, Guid TokenId);

internal static class PatMapping
{
    public static PatDto ToDto(this PersonalAccessToken token, DateTimeOffset now) => new(
        token.Id, token.Name, token.Scopes, token.CreatedAt, token.ExpiresAt, token.LastUsedAt, token.IsActive(now));
}

public sealed class CreatePatHandler(IAppDbContext db, IClock clock) : ICommandHandler<CreatePatCommand, CreatedPatDto>
{
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(365);
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(90);
    public const int MaxNameLength = 64;
    public const int MaxActiveTokens = 50;

    public async Task<Result<CreatedPatDto>> Handle(CreatePatCommand command, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var name = command.Name?.Trim() ?? "";
        var scopes = (command.Scopes ?? []).Distinct().Order().ToArray();
        var expiresAt = command.ExpiresAt ?? now + DefaultLifetime;

        var errors = new Dictionary<string, string[]>();
        if (name.Length is 0 or > MaxNameLength) errors["name"] = [$"Must be 1-{MaxNameLength} characters."];
        if (scopes.Length == 0 || scopes.Any(s => !AuthScopes.Personal.Contains(s)))
            errors["scopes"] = [$"Choose one or more of: {string.Join(", ", AuthScopes.Personal)}."];
        if (expiresAt <= now || expiresAt > now + MaxLifetime)
            errors["expiresAt"] = ["Must be in the future and at most one year from now."];
        if (errors.Count > 0) return Error.Validation("token.invalid", "Token is invalid.", errors);

        var active = await db.PersonalAccessTokens.CountAsync(
            t => t.UserId == command.UserId && t.RevokedAt == null && t.ExpiresAt > now, ct);
        if (active >= MaxActiveTokens)
            return Error.Conflict("token.limit_reached", $"At most {MaxActiveTokens} active tokens are allowed.");

        var secret = PatToken.Generate();
        var token = new PersonalAccessToken(command.UserId, name, PatToken.Hash(secret), scopes, expiresAt, now);
        db.PersonalAccessTokens.Add(token);
        await db.SaveChangesAsync(ct);
        return new CreatedPatDto(token.ToDto(now), secret);
    }
}

public sealed class ListPatsHandler(IAppDbContext db, IClock clock) : IQueryHandler<ListPatsQuery, IReadOnlyList<PatDto>>
{
    public async Task<Result<IReadOnlyList<PatDto>>> Handle(ListPatsQuery query, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var tokens = await db.PersonalAccessTokens.AsNoTracking()
            .Where(t => t.UserId == query.UserId && t.RevokedAt == null)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);
        return tokens.Select(t => t.ToDto(now)).ToList();
    }
}

public sealed class RevokePatHandler(IAppDbContext db, IClock clock) : ICommandHandler<RevokePatCommand, bool>
{
    public async Task<Result<bool>> Handle(RevokePatCommand command, CancellationToken ct)
    {
        var token = await db.PersonalAccessTokens.FirstOrDefaultAsync(
            t => t.Id == command.TokenId && t.UserId == command.UserId, ct);
        if (token is null) return Error.NotFound("token.not_found", "Token not found.");

        token.Revoke(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
