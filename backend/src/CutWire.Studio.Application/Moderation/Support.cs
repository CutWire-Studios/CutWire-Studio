using System.Text.Json;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Catalog;
using CutWire.Studio.Domain.Accounts;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Domain.Moderation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Application.Moderation;

public sealed record AccountInfo(Guid UserId, bool EmailVerified, IReadOnlyList<string> Roles);

public sealed record StaffAccount(Guid UserId, string? Email, IReadOnlyList<string> Roles);

/// <summary>Identity data (email verification, roles) that lives outside the profile tables.</summary>
public interface IUserDirectory
{
    Task<IReadOnlyDictionary<Guid, AccountInfo>> GetAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct);

    Task<Result> SetModeratorAsync(Guid userId, bool moderator, CancellationToken ct);

    Task<Result> SetRoleAsync(Guid userId, string role, bool granted, CancellationToken ct);

    /// <summary>Accounts holding any staff role, with their email.</summary>
    Task<IReadOnlyList<StaffAccount>> ListStaffAsync(CancellationToken ct);

    /// <summary>Ids of accounts whose email equals or starts with <paramref name="email"/>, at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<Guid>> FindByEmailAsync(string email, int limit, CancellationToken ct);

    Task<int> CountInRoleAsync(string role, CancellationToken ct);

    Task<bool> IsBootstrapAdminAsync(Guid userId, CancellationToken ct);
}

/// <summary>Finds the existing item whose thumbnail is visually closest to an item's own thumbnail.</summary>
public interface ISimilarItemFinder
{
    Task<Guid?> FindAsync(Guid itemId, CancellationToken ct);
}

internal static class ItemLock
{
    /// <summary>Takes the item's row lock for the surrounding transaction so tally and rule evaluation are serialised. False when the item is gone.</summary>
    public static async Task<bool> AcquireAsync(IAppDbContext db, Guid itemId, CancellationToken ct) =>
        await db.Items.Where(i => i.Id == itemId).ExecuteUpdateAsync(s => s.SetProperty(i => i.UpdatedAt, i => i.UpdatedAt), ct) > 0;
}

internal static class Cursors
{
    public static string Encode<T>(T value) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static T? Decode<T>(string? value) where T : class
    {
        if (string.IsNullOrEmpty(value)) return null;
        try
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            return JsonSerializer.Deserialize<T>(Convert.FromBase64String(padded));
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return null;
        }
    }

    public static Error Invalid() =>
        Error.Validation("bad_request", "The cursor is invalid.", [new FieldViolation("cursor", "invalid_cursor", "The cursor is invalid.")]);
}

/// <summary>Adds <c>moderation_actions</c> rows; the caller saves.</summary>
public sealed class AuditLog(IAppDbContext db, IClock clock)
{
    public void Record(Guid? actorId, string action, string targetType, Guid targetId, Guid? itemId = null, string? reason = null, object? details = null)
    {
        var json = JsonSerializer.SerializeToElement(details ?? new { }, JsonSerializerOptions.Web);
        db.ModerationActions.Add(new ModerationAction(Guid.CreateVersion7(), actorId, actorId is null, action, targetType, targetId,
            itemId, reason, json, clock.UtcNow));
    }
}

/// <summary>Hides a published item when the auto-hide rule fires. Call inside a transaction holding the item lock.</summary>
public sealed class AutoHideService(IAppDbContext db, IClock clock, IOptions<ModerationOptions> options, AuditLog audit)
{
    public async Task<bool> EvaluateAsync(Guid itemId, CancellationToken ct)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null || item.Status != ItemStatus.Published) return false;

        var trustedReporters = await db.Reports.Where(r => r.ItemId == itemId && r.Status == ReportStatus.Open)
            .Join(db.UserProfiles.Where(p => p.TrustLevel != TrustLevel.T0), r => r.ReporterId, p => p.Id, (r, _) => r.ReporterId)
            .Distinct().CountAsync(ct);
        var reason = AutoHideRule.Evaluate(item.ScoreUp, item.ScoreDown, trustedReporters, options.Value.Thresholds);
        if (reason is null) return false;

        var now = clock.UtcNow;
        item.Hide(reason, now);
        db.CleanupSchedules.Add(new CleanupSchedule(item.Id, now + options.Value.PurgeDelay, now));
        audit.Record(null, "auto_hide", "item", item.Id, item.Id, reason,
            new { scoreUp = item.ScoreUp, scoreDown = item.ScoreDown, trustedReporters, purgeAt = now + options.Value.PurgeDelay });
        await db.SaveChangesAsync(ct);
        return true;
    }
}

/// <summary>Removes an item and releases its blobs. The item must be loaded with versions and media; the caller saves.</summary>
public sealed class ItemRemoval(IAppDbContext db, IClock clock, BlobReferences refs)
{
    public async Task RemoveAsync(Item item, string? reason, CancellationToken ct)
    {
        foreach (var version in item.Versions) await refs.ReleaseAsync(version.PackageSha256, ct);
        foreach (var media in item.Media) await refs.ReleaseAsync(media.BlobSha256, ct);
        item.MarkRemoved(clock.UtcNow, reason);
        await db.CleanupSchedules.Where(c => c.ItemId == item.Id).ExecuteDeleteAsync(ct);
    }
}
