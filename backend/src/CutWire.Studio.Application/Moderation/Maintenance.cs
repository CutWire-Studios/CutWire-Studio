using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Domain.Accounts;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Domain.Moderation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Application.Moderation;

/// <summary>Job bodies: they live here so they can be tested without a timer.</summary>
public sealed class ModerationMaintenance(IAppDbContext db, IClock clock, ItemRemoval removal, AuditLog audit, IOptions<ModerationOptions> options)
{
    /// <summary>Removes hidden items whose purge time has passed, and restores owner_banned items whose temporary ban has ended.</summary>
    public async Task<(int Purged, int Restored)> CleanupAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var purged = 0;
        while (!ct.IsCancellationRequested)
        {
            var batch = await db.CleanupSchedules.Where(c => c.PurgeAt <= now)
                .Join(db.Items.Where(i => i.Status == ItemStatus.Hidden), c => c.ItemId, i => i.Id, (c, i) => i.Id)
                .OrderBy(id => id).Take(100).ToListAsync(ct);
            if (batch.Count == 0) break;

            foreach (var item in await db.Items.Include(i => i.Versions).Include(i => i.Media).Where(i => batch.Contains(i.Id)).ToListAsync(ct))
            {
                var reason = item.VisibilityReason;
                await removal.RemoveAsync(item, "purged", ct);
                audit.Record(null, "purge", "item", item.Id, item.Id, reason, new { previousReason = reason });
                purged++;
            }

            await db.SaveChangesAsync(ct);
        }

        // Schedules of items that are no longer hidden (restored or deleted by the owner) are stale.
        await db.CleanupSchedules.Where(c => c.PurgeAt <= now && !db.Items.Any(i => i.Id == c.ItemId && i.Status == ItemStatus.Hidden))
            .ExecuteDeleteAsync(ct);

        var restored = 0;
        var expired = await db.Items.Where(i => i.Status == ItemStatus.Hidden && i.VisibilityReason == VisibilityReasons.OwnerBanned)
            .Join(db.UserProfiles.Where(p => !p.IsPermanentlyBanned && (p.BannedUntil == null || p.BannedUntil <= now)),
                i => i.OwnerId, p => p.Id, (i, _) => i).ToListAsync(ct);
        foreach (var item in expired)
        {
            item.Restore(now);
            audit.Record(null, "restore", "item", item.Id, item.Id, "ban_expired", new { previousReason = VisibilityReasons.OwnerBanned });
            restored++;
        }

        if (restored > 0) await db.SaveChangesAsync(ct);
        return (purged, restored);
    }

    /// <summary>T0 to T1 after the account age with no upheld report lately; T1 to T2 with enough positively scored published items. Never demotes.</summary>
    public async Task<(int ToT1, int ToT2)> RecomputeTrustAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var o = options.Value;
        var ageCutoff = now - o.TrustT1AccountAge;
        var cleanSince = now - o.TrustT1CleanPeriod;

        var toT1 = 0;
        var candidates = await db.UserProfiles
            .Where(p => p.TrustLevel == TrustLevel.T0 && p.CreatedAt <= ageCutoff && p.DeletedAt == null && !p.IsPermanentlyBanned
                        && (p.BannedUntil == null || p.BannedUntil <= now))
            .Where(p => !db.Reports.Any(r => r.Status == ReportStatus.Upheld && r.ResolvedAt >= cleanSince
                                             && db.Items.Any(i => i.Id == r.ItemId && i.OwnerId == p.Id)))
            .ToListAsync(ct);
        foreach (var profile in candidates)
        {
            profile.SetTrustLevel(TrustLevel.T1);
            audit.Record(null, "trust_promoted", "user", profile.Id, null, "account_age", new { from = "T0", to = "T1" });
            toT1++;
        }

        var toT2 = 0;
        var promotable = await db.UserProfiles
            .Where(p => p.TrustLevel == TrustLevel.T1 && p.DeletedAt == null
                        && db.Items.Count(i => i.OwnerId == p.Id && i.Status == ItemStatus.Published && i.WilsonScore > 0) >= o.TrustT2MinItems)
            .ToListAsync(ct);
        foreach (var profile in promotable)
        {
            profile.SetTrustLevel(TrustLevel.T2);
            audit.Record(null, "trust_promoted", "user", profile.Id, null, "published_items", new { from = "T1", to = "T2" });
            toT2++;
        }

        if (toT1 + toT2 > 0) await db.SaveChangesAsync(ct);
        return (toT1, toT2);
    }
}
