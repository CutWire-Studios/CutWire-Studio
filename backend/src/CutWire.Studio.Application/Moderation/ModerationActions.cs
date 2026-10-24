using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Domain.Accounts;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Domain.Moderation;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Application.Moderation;

public sealed class ModerationActions(IAppDbContext db, IClock clock, AuditLog audit, ItemRemoval removal, IUserDirectory directory,
    Microsoft.Extensions.Options.IOptions<ModerationOptions> options)
{
    public const int MaxBulk = 100;
    public const int MaxPurgeDays = 365;

    private static Error ItemNotFound() => Error.NotFound("item.not_found", "Item not found.");

    private static Error Bad(string field, string code, string message) =>
        Error.Validation("bad_request", message, [new FieldViolation(field, code, message)]);

    public async Task<Result<ModItemResultDto>> RestoreAsync(Guid actor, Guid itemId, bool dismissReports, string? reason, CancellationToken ct)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null || item.Status == ItemStatus.Removed) return ItemNotFound();
        if (item.Status != ItemStatus.Hidden) return Error.Conflict("item.not_hidden", "Only hidden items can be restored.");

        var previous = item.VisibilityReason;
        item.Restore(clock.UtcNow);
        await db.CleanupSchedules.Where(c => c.ItemId == itemId).ExecuteDeleteAsync(ct);
        var dismissed = 0;
        if (dismissReports)
            foreach (var report in await db.Reports.Where(r => r.ItemId == itemId && r.Status == ReportStatus.Open).ToListAsync(ct))
            {
                report.Resolve(ReportStatus.Dismissed, actor, clock.UtcNow);
                dismissed++;
            }

        audit.Record(actor, "restore", "item", itemId, itemId, reason, new { previousReason = previous, dismissedReports = dismissed });
        await db.SaveChangesAsync(ct);
        return new ModItemResultDto(item.Id, item.Status.ToKey(), item.VisibilityReason);
    }

    public async Task<Result<ModItemResultDto>> RemoveAsync(Guid actor, Guid itemId, string? reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason)) return Bad("reason", "required", "A reason is required.");
        var item = await db.Items.Include(i => i.Versions).Include(i => i.Media).FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null || item.Status == ItemStatus.Removed) return ItemNotFound();

        var previous = item.Status.ToKey();
        await removal.RemoveAsync(item, "removed_by_moderator", ct);
        audit.Record(actor, "remove", "item", itemId, itemId, reason.Trim(), new { previousStatus = previous });
        await db.SaveChangesAsync(ct);
        return new ModItemResultDto(item.Id, item.Status.ToKey(), item.VisibilityReason);
    }

    public async Task<Result<ModItemResultDto>> SchedulePurgeAsync(Guid actor, Guid itemId, int? requestedDays, string? reason, CancellationToken ct)
    {
        var days = requestedDays ?? (int)options.Value.PurgeDelay.TotalDays;
        if (days is < 1 or > MaxPurgeDays) return Bad("days", "out_of_range", $"Days must be between 1 and {MaxPurgeDays}.");
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null || item.Status == ItemStatus.Removed) return ItemNotFound();
        if (item.Status != ItemStatus.Hidden) return Error.Conflict("item.not_hidden", "Only hidden items can be scheduled for purge.");

        var now = clock.UtcNow;
        var purgeAt = now.AddDays(days);
        var schedule = await db.CleanupSchedules.FirstOrDefaultAsync(c => c.ItemId == itemId, ct);
        if (schedule is null) db.CleanupSchedules.Add(new CleanupSchedule(itemId, purgeAt, now));
        else schedule.Reschedule(purgeAt);
        audit.Record(actor, "schedule_purge", "item", itemId, itemId, reason, new { days, purgeAt });
        await db.SaveChangesAsync(ct);
        return new ModItemResultDto(item.Id, item.Status.ToKey(), item.VisibilityReason);
    }

    public async Task<Result<BulkResultDto>> BulkAsync(Guid actor, IReadOnlyList<Guid>? ids, string? action, string? reason, int? days,
        bool dismissReports, CancellationToken ct)
    {
        if (ids is not { Count: > 0 } || ids.Count > MaxBulk) return Bad("ids", "invalid_count", $"Provide between 1 and {MaxBulk} ids.");
        action = action?.Replace('-', '_');
        if (action is not ("restore" or "remove" or "schedule_purge"))
            return Bad("action", "invalid_action", "Action must be restore, remove or schedule_purge.");
        if (action == "remove" && string.IsNullOrWhiteSpace(reason)) return Bad("reason", "required", "A reason is required.");

        var succeeded = new List<Guid>();
        var failed = new List<BulkFailureDto>();
        foreach (var id in ids.Distinct())
        {
            var result = action switch
            {
                "restore" => await RestoreAsync(actor, id, dismissReports, reason, ct),
                "remove" => await RemoveAsync(actor, id, reason, ct),
                _ => await SchedulePurgeAsync(actor, id, days, reason, ct),
            };
            if (result.IsSuccess) succeeded.Add(id);
            else failed.Add(new BulkFailureDto(id, result.Error.Code, result.Error.Message));
        }

        return new BulkResultDto(succeeded, failed);
    }

    public async Task<Result<ModItemResultDto>> ResolveReportAsync(Guid actor, Guid reportId, bool uphold, string? note, CancellationToken ct)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == reportId, ct);
        if (report is null) return Error.NotFound("report.not_found", "Report not found.");
        if (report.Status != ReportStatus.Open) return Error.Conflict("report.not_open", "The report is already resolved.");

        var now = clock.UtcNow;
        report.Resolve(uphold ? ReportStatus.Upheld : ReportStatus.Dismissed, actor, now);
        object details = new { reason = report.Reason.ToKey() };
        if (uphold)
        {
            var ownerId = await db.Items.Where(i => i.Id == report.ItemId).Select(i => i.OwnerId).FirstAsync(ct);
            var owner = await db.UserProfiles.FirstAsync(p => p.Id == ownerId, ct);
            var from = owner.TrustLevel;
            if (from is TrustLevel.T1 or TrustLevel.T2) owner.SetTrustLevel(from - 1);
            details = new { reason = report.Reason.ToKey(), ownerId, ownerTrustFrom = from.ToString(), ownerTrustTo = owner.TrustLevel.ToString() };
        }

        audit.Record(actor, uphold ? "report_upheld" : "report_dismissed", "report", reportId, report.ItemId, note, details);
        await db.SaveChangesAsync(ct);
        var item = await db.Items.AsNoTracking().FirstAsync(i => i.Id == report.ItemId, ct);
        return new ModItemResultDto(item.Id, item.Status.ToKey(), item.VisibilityReason);
    }

    public async Task<Result<ModItemResultDto>> ResolveFlagAsync(Guid actor, Guid flagId, string? note, CancellationToken ct)
    {
        var flag = await db.ItemFlags.FirstOrDefaultAsync(f => f.Id == flagId, ct);
        if (flag is null) return Error.NotFound("flag.not_found", "Flag not found.");
        if (flag.ResolvedAt is not null) return Error.Conflict("flag.resolved", "The flag is already resolved.");

        flag.Resolve(clock.UtcNow);
        audit.Record(actor, "flag_resolved", "flag", flagId, flag.ItemId, note, new { code = flag.Code });
        await db.SaveChangesAsync(ct);
        var item = await db.Items.AsNoTracking().FirstAsync(i => i.Id == flag.ItemId, ct);
        return new ModItemResultDto(item.Id, item.Status.ToKey(), item.VisibilityReason);
    }

    public async Task<Result<ModUserActionDto>> BanAsync(Guid actor, bool actorIsAdmin, Guid userId, DateTimeOffset? until, bool permanent,
        string? reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason)) return Bad("reason", "required", "A reason is required.");
        if (permanent == (until is not null)) return Bad("until", "permanent_xor_until", "Provide either until or permanent: true.");
        var now = clock.UtcNow;
        if (until is { } end && end <= now) return Bad("until", "in_the_past", "The ban end must be in the future.");
        if (userId == actor) return Error.Forbidden("user.self", "You cannot ban yourself.");

        var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.Id == userId, ct);
        if (profile is null) return Error.NotFound("user.not_found", "User not found.");
        var roles = (await directory.GetAsync([userId], ct)).GetValueOrDefault(userId)?.Roles ?? [];
        if (roles.Count > 0 && !actorIsAdmin) return Error.Forbidden("user.privileged", "Only an admin can ban a moderator or admin.");

        profile.SetBan(permanent ? null : until, permanent);
        var hidden = 0;
        foreach (var item in await db.Items.Where(i => i.OwnerId == userId && i.Status == ItemStatus.Published).ToListAsync(ct))
        {
            item.Hide(VisibilityReasons.OwnerBanned, now);
            hidden++;
        }

        audit.Record(actor, "ban", "user", userId, null, reason.Trim(), new { until = permanent ? null : until, permanent, hiddenItems = hidden });
        await db.SaveChangesAsync(ct);
        return new ModUserActionDto(userId, profile.TrustLevel.ToString(), true, hidden);
    }

    public async Task<Result<ModUserActionDto>> UnbanAsync(Guid actor, Guid userId, string? reason, CancellationToken ct)
    {
        var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.Id == userId, ct);
        if (profile is null) return Error.NotFound("user.not_found", "User not found.");

        var now = clock.UtcNow;
        profile.SetBan(null, false);
        var restored = 0;
        foreach (var item in await db.Items.Where(i => i.OwnerId == userId && i.Status == ItemStatus.Hidden
                     && i.VisibilityReason == VisibilityReasons.OwnerBanned).ToListAsync(ct))
        {
            item.Restore(now);
            restored++;
        }

        audit.Record(actor, "unban", "user", userId, null, reason, new { restoredItems = restored });
        await db.SaveChangesAsync(ct);
        return new ModUserActionDto(userId, profile.TrustLevel.ToString(), false, restored);
    }

    public async Task<Result<ModUserActionDto>> SetTrustAsync(Guid actor, Guid userId, string? level, string? reason, CancellationToken ct)
    {
        if (!Enum.TryParse<TrustLevel>(level, ignoreCase: true, out var trust) || !Enum.IsDefined(trust))
            return Bad("level", "invalid_level", "Level must be T0, T1, T2 or T3.");
        var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.Id == userId, ct);
        if (profile is null) return Error.NotFound("user.not_found", "User not found.");

        var from = profile.TrustLevel;
        profile.SetTrustLevel(trust);
        audit.Record(actor, "set_trust", "user", userId, null, reason, new { from = from.ToString(), to = trust.ToString() });
        await db.SaveChangesAsync(ct);
        return new ModUserActionDto(userId, trust.ToString(), profile.IsBanned(clock.UtcNow), 0);
    }

    public async Task<Result<ModUserActionDto>> SetModeratorAsync(Guid actor, Guid userId, bool moderator, CancellationToken ct)
    {
        var profile = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == userId, ct);
        if (profile is null) return Error.NotFound("user.not_found", "User not found.");

        var result = await directory.SetModeratorAsync(userId, moderator, ct);
        if (result.IsFailure) return result.Error;
        audit.Record(actor, moderator ? "grant_moderator" : "revoke_moderator", "user", userId);
        await db.SaveChangesAsync(ct);
        return new ModUserActionDto(userId, profile.TrustLevel.ToString(), profile.IsBanned(clock.UtcNow), 0);
    }
}
