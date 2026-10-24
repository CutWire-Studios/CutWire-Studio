using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Catalog;
using CutWire.Studio.Domain.Accounts;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Domain.Moderation;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Application.Moderation;

public sealed class ModerationQueries(IAppDbContext db, IUserDirectory directory, ISimilarItemFinder similar, IClock clock)
{
    public const int DefaultLimit = 25;
    public const int MaxLimit = 100;

    private sealed record TimeCursor(long T, Guid Id);

    private sealed record HandleCursor(string H);

    private sealed record IdCursor(Guid Id);

    private static int Clamp(int? limit) => Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

    public async Task<Result<ModQueuePageDto>> QueueAsync(string? type, string? cursor, int? limit, CancellationToken ct)
    {
        if (type is not ("reported" or "hidden" or "flagged"))
            return Error.Validation("bad_request", "Type must be reported, hidden or flagged.",
                [new FieldViolation("type", "invalid_type", "Type must be reported, hidden or flagged.")]);
        TimeCursor? after = null;
        if (!string.IsNullOrEmpty(cursor) && (after = Cursors.Decode<TimeCursor>(cursor)) is null) return Cursors.Invalid();

        var take = Clamp(limit) + 1;
        var t = new DateTimeOffset(after?.T ?? 0, TimeSpan.Zero);
        var id = after?.Id ?? Guid.Empty;
        var has = after is not null;
        List<(Guid ItemId, DateTimeOffset Key)> keys;
        switch (type)
        {
            case "reported":
                keys = (await db.Reports.AsNoTracking().Where(r => r.Status == ReportStatus.Open)
                    .GroupBy(r => r.ItemId).Select(g => new { ItemId = g.Key, Key = g.Max(r => r.CreatedAt) })
                    .Where(x => db.Items.Any(i => i.Id == x.ItemId && i.Status != ItemStatus.Removed))
                    .Where(x => !has || x.Key < t || (x.Key == t && x.ItemId.CompareTo(id) < 0))
                    .OrderByDescending(x => x.Key).ThenByDescending(x => x.ItemId).Take(take).ToListAsync(ct))
                    .Select(x => (x.ItemId, x.Key)).ToList();
                break;
            case "flagged":
                keys = (await db.ItemFlags.AsNoTracking().Where(f => f.ResolvedAt == null)
                    .GroupBy(f => f.ItemId).Select(g => new { ItemId = g.Key, Key = g.Max(f => f.CreatedAt) })
                    .Where(x => db.Items.Any(i => i.Id == x.ItemId && i.Status != ItemStatus.Removed))
                    .Where(x => !has || x.Key < t || (x.Key == t && x.ItemId.CompareTo(id) < 0))
                    .OrderByDescending(x => x.Key).ThenByDescending(x => x.ItemId).Take(take).ToListAsync(ct))
                    .Select(x => (x.ItemId, x.Key)).ToList();
                break;
            default:
                keys = (await db.Items.AsNoTracking().Where(i => i.Status == ItemStatus.Hidden)
                    .Select(i => new { ItemId = i.Id, Key = i.UpdatedAt })
                    .Where(x => !has || x.Key < t || (x.Key == t && x.ItemId.CompareTo(id) < 0))
                    .OrderByDescending(x => x.Key).ThenByDescending(x => x.ItemId).Take(take).ToListAsync(ct))
                    .Select(x => (x.ItemId, x.Key)).ToList();
                break;
        }

        string? next = null;
        if (keys.Count == take)
        {
            keys.RemoveAt(take - 1);
            next = Cursors.Encode(new TimeCursor(keys[^1].Key.UtcTicks, keys[^1].ItemId));
        }

        var ids = keys.Select(k => k.ItemId).ToList();
        var items = (await db.Items.AsNoTracking().Include(i => i.Owner).Where(i => ids.Contains(i.Id)).ToListAsync(ct)).ToDictionary(i => i.Id);
        var thumbs = (await db.ItemMedia.AsNoTracking().Where(m => ids.Contains(m.ItemId) && m.Role == MediaRole.Thumbnail)
            .Select(m => new { m.ItemId, m.BlobSha256 }).ToListAsync(ct)).ToDictionary(m => m.ItemId, m => m.BlobSha256);

        Dictionary<Guid, List<ModReportDto>> reports = [];
        Dictionary<Guid, DateTimeOffset> purge = [];
        Dictionary<Guid, List<ModFlagDto>> flags = [];
        if (type == "reported")
            reports = (await db.Reports.AsNoTracking().Where(r => ids.Contains(r.ItemId) && r.Status == ReportStatus.Open)
                    .Join(db.UserProfiles, r => r.ReporterId, p => p.Id, (r, p) => new { r, p.Handle, p.TrustLevel })
                    .OrderBy(x => x.r.CreatedAt).ToListAsync(ct))
                .GroupBy(x => x.r.ItemId).ToDictionary(g => g.Key, g => g.Select(x => new ModReportDto(x.r.Id, x.Handle,
                    x.TrustLevel.ToString(), x.r.Reason.ToKey(), x.r.Note, "open", x.r.CreatedAt)).ToList());
        else if (type == "hidden")
            purge = await db.CleanupSchedules.AsNoTracking().Where(c => ids.Contains(c.ItemId)).ToDictionaryAsync(c => c.ItemId, c => c.PurgeAt, ct);
        else
        {
            var rows = await db.ItemFlags.AsNoTracking().Where(f => ids.Contains(f.ItemId) && f.ResolvedAt == null).OrderBy(f => f.CreatedAt).ToListAsync(ct);
            var matches = new Dictionary<Guid, ModItemRefDto?>();
            foreach (var itemId in rows.Where(f => f.Code == "similar_thumbnail").Select(f => f.ItemId).Distinct())
            {
                var matchId = await similar.FindAsync(itemId, ct);
                matches[itemId] = matchId is { } m
                    ? await db.Items.AsNoTracking().Where(i => i.Id == m)
                        .Select(i => new ModItemRefDto(i.Id, i.Kind.Value, i.Slug, i.Title, i.Owner.Handle)).FirstOrDefaultAsync(ct)
                    : null;
            }

            flags = rows.GroupBy(f => f.ItemId).ToDictionary(g => g.Key, g => g.Select(f => new ModFlagDto(f.Id, f.Code, f.Message,
                f.Params, f.CreatedAt, matches.GetValueOrDefault(f.ItemId))).ToList());
        }

        var tagsByItem = (await db.ItemTags.AsNoTracking().Where(t => ids.Contains(t.ItemId)).Select(t => new { t.ItemId, t.Tag.Slug }).ToListAsync(ct))
            .GroupBy(t => t.ItemId).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(t => t.Slug).Order(StringComparer.Ordinal).ToList());
        var strips = (await db.ItemMedia.AsNoTracking().Where(m => ids.Contains(m.ItemId) && m.Role == MediaRole.PreviewStrip)
            .Select(m => new { m.ItemId, m.BlobSha256 }).ToListAsync(ct)).ToDictionary(m => m.ItemId, m => m.BlobSha256);

        var result = new List<ModQueueEntryDto>();
        foreach (var itemId in ids)
        {
            if (!items.TryGetValue(itemId, out var item)) continue;
            var card = new ItemCardDto(item.Id, item.Kind.Value, item.Slug, item.Title, item.Summary, ItemMapper.Owner(item.Owner),
                thumbs.TryGetValue(itemId, out var th) ? ItemMapper.FileUrl(th) : null, item.ScoreUp - item.ScoreDown, item.DownloadsTotal,
                tagsByItem.GetValueOrDefault(itemId) ?? [], item.PublishedAt, item.Status.ToKey(), null,
                strips.TryGetValue(itemId, out var strip) ? ItemMapper.FileUrl(strip) : null, item.VisibilityReason);
            var itemReports = reports.GetValueOrDefault(itemId) ?? [];
            result.Add(new ModQueueEntryDto(card, itemReports, flags.GetValueOrDefault(itemId) ?? [], item.VisibilityReason,
                purge.TryGetValue(itemId, out var p) ? p : null, itemReports.GroupBy(r => r.Reason).ToDictionary(g => g.Key, g => g.Count())));
        }

        return new ModQueuePageDto(result, next);
    }

    public async Task<Result<ModUserPageDto>> UsersAsync(string? q, string? cursor, int? limit, CancellationToken ct)
    {
        HandleCursor? after = null;
        if (!string.IsNullOrEmpty(cursor) && (after = Cursors.Decode<HandleCursor>(cursor)) is null) return Cursors.Invalid();

        IQueryable<UserProfile> profiles = db.UserProfiles.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim().ToLowerInvariant();
            profiles = profiles.Where(p => p.Handle.Contains(needle) || p.DisplayName.ToLower().Contains(needle));
        }

        if (after is not null)
        {
            var h = after.H;
            profiles = profiles.Where(p => p.Handle.CompareTo(h) > 0);
        }

        var take = Clamp(limit) + 1;
        var rows = await profiles.OrderBy(p => p.Handle).Take(take).ToListAsync(ct);
        string? next = null;
        if (rows.Count == take)
        {
            rows.RemoveAt(take - 1);
            next = Cursors.Encode(new HandleCursor(rows[^1].Handle));
        }

        var ids = rows.Select(r => r.Id).ToList();
        var accounts = await directory.GetAsync(ids, ct);
        var counts = await db.Items.AsNoTracking().Where(i => ids.Contains(i.OwnerId) && i.Status == ItemStatus.Published)
            .GroupBy(i => i.OwnerId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var now = clock.UtcNow;
        return new ModUserPageDto(rows.Select(p => new ModUserDto(p.Id, p.Handle, p.DisplayName, p.AvatarSha256 is { } av ? ItemMapper.FileUrl(av) : null, p.TrustLevel.ToString(),
            accounts.GetValueOrDefault(p.Id)?.Roles ?? [], accounts.GetValueOrDefault(p.Id)?.EmailVerified ?? false,
            p.IsBanned(now), p.BannedUntil, p.IsPermanentlyBanned, p.CreatedAt, counts.GetValueOrDefault(p.Id))).ToList(), next);
    }

    /// <param name="actor">User id or handle.</param>
    /// <param name="target">Item, user, report or flag id.</param>
    public async Task<Result<AuditPageDto>> AuditAsync(string? actor, Guid? target, string? cursor, int? limit, CancellationToken ct)
    {
        IdCursor? after = null;
        if (!string.IsNullOrEmpty(cursor) && (after = Cursors.Decode<IdCursor>(cursor)) is null) return Cursors.Invalid();

        IQueryable<ModerationAction> actions = db.ModerationActions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(actor))
        {
            Guid? actorId = Guid.TryParse(actor, out var parsed)
                ? parsed
                : await db.UserProfiles.AsNoTracking().Where(p => p.Handle == actor.Trim().ToLower()).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
            if (actorId is null) return new AuditPageDto([], null);
            actions = actions.Where(a => a.ActorId == actorId);
        }

        if (target is { } t) actions = actions.Where(a => a.TargetId == t || a.ItemId == t);
        if (after is not null)
        {
            var id = after.Id;
            actions = actions.Where(a => a.Id.CompareTo(id) < 0);
        }

        var take = Clamp(limit) + 1;
        var rows = await actions.OrderByDescending(a => a.Id).Take(take).ToListAsync(ct);
        string? next = null;
        if (rows.Count == take)
        {
            rows.RemoveAt(take - 1);
            next = Cursors.Encode(new IdCursor(rows[^1].Id));
        }

        var actorIds = rows.Where(r => r.ActorId is not null).Select(r => r.ActorId!.Value).Distinct().ToList();
        var handles = await db.UserProfiles.AsNoTracking().Where(p => actorIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Handle, ct);
        return new AuditPageDto(rows.Select(a => new AuditEntryDto(a.Id, a.CreatedAt, a.ActorId is { } aid ? handles.GetValueOrDefault(aid) : null,
            a.Action, a.TargetType, a.TargetId, a.ItemId, a.Reason, a.Auto, a.Details)).ToList(), next);
    }
}
