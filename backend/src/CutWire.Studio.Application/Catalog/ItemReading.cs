using System.Text;
using System.Text.Json;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Application.Catalog;

public enum ItemSort
{
    Top,
    Trending,
    New,
    Downloads,
}

public sealed record ListItemsQuery(
    Viewer Viewer,
    string? Kind,
    string? Text,
    IReadOnlyList<string> Tags,
    string? Category,
    string? Sort,
    string? OwnerHandle,
    string? Status,
    string? Cursor,
    int? Limit,
    string? Period = null);

public sealed class ListItemsHandler(IAppDbContext db, IItemTextSearch search, IClock clock) : IQueryHandler<ListItemsQuery, ItemPageDto>
{
    public const int DefaultLimit = 24;
    public const int MaxLimit = 50;

    private sealed record Cursor(string S, double K, long T, Guid Id);

    private sealed record Row(Guid Id, string Kind, string Slug, string Title, string Summary, string Handle, string DisplayName,
        Domain.Blobs.Sha256Hash? Avatar, double Up, double Down, double Wilson, double Hot, long Downloads, DateTimeOffset? PublishedAt,
        DateTimeOffset SortTime, ItemStatus Status, Guid OwnerId, string? Reason);

    public async Task<Result<ItemPageDto>> Handle(ListItemsQuery query, CancellationToken ct)
    {
        var sort = ParseSort(query.Sort);
        if (sort is null) return Bad("sort", "Sort must be one of top, trending, new, downloads.");
        var periodStart = ParsePeriod(query.Period, clock.UtcNow, out var validPeriod);
        if (!validPeriod) return Bad("period", "Period must be one of day, week, month, year, all.");

        var statuses = new List<ItemStatus> { ItemStatus.Published };
        var owner = query.OwnerHandle is { Length: > 0 } handle
            ? await db.UserProfiles.AsNoTracking().Where(p => p.Handle == handle.ToLower()).Select(p => new { p.Id }).FirstOrDefaultAsync(ct)
            : null;
        if (query.Status is { Length: > 0 } status && status != "published")
        {
            var privileged = query.Viewer.IsModerator || (owner is not null && owner.Id == query.Viewer.UserId);
            if (!privileged) return Error.Forbidden("items.status_forbidden", "Only the owner can list unpublished items.");
            if (status == "all") statuses = [ItemStatus.Draft, ItemStatus.Published, ItemStatus.Hidden];
            else if (ItemStatusExtensions.TryParse(status, out var parsed) && parsed != ItemStatus.Removed) statuses = [parsed];
            else return Bad("status", "Status must be draft, published, hidden or all.");
        }

        if (query.OwnerHandle is { Length: > 0 } && owner is null) return new ItemPageDto([], null);

        IQueryable<Item> items = db.Items.AsNoTracking().Where(i => statuses.Contains(i.Status));
        if (owner is not null) items = items.Where(i => i.OwnerId == owner.Id);
        if (periodStart is { } since) items = items.Where(i => i.PublishedAt >= since);
        if (query.Kind is { Length: > 0 })
        {
            if (!AssetKindId.TryParse(query.Kind, out var kind)) return new ItemPageDto([], null);
            items = items.Where(i => i.Kind == kind);
        }

        if (query.Category is { Length: > 0 } category) items = items.Where(i => i.Category == category);
        foreach (var tag in query.Tags.Distinct())
        {
            var slug = tag;
            items = items.Where(i => i.Tags.Any(t => t.Tag.Slug == slug));
        }

        if (query.Text is { } text && !string.IsNullOrWhiteSpace(text)) items = search.Apply(items, text.Trim());

        if (query.Cursor is { Length: > 0 })
        {
            var cursor = DecodeCursor(query.Cursor);
            if (cursor is null || cursor.S != sort.ToString()!.ToLowerInvariant())
                return Bad("cursor", "The cursor is invalid for this query.");
            var time = new DateTimeOffset(cursor.T, TimeSpan.Zero);
            var k = cursor.K;
            var id = cursor.Id;
            items = sort switch
            {
                ItemSort.Top => items.Where(i => i.WilsonScore < k || (i.WilsonScore == k && ((i.PublishedAt ?? i.CreatedAt) < time || ((i.PublishedAt ?? i.CreatedAt) == time && i.Id.CompareTo(id) < 0)))),
                ItemSort.Trending => items.Where(i => i.HotScore < k || (i.HotScore == k && ((i.PublishedAt ?? i.CreatedAt) < time || ((i.PublishedAt ?? i.CreatedAt) == time && i.Id.CompareTo(id) < 0)))),
                ItemSort.Downloads => items.Where(i => (double)i.DownloadsTotal < k || ((double)i.DownloadsTotal == k && ((i.PublishedAt ?? i.CreatedAt) < time || ((i.PublishedAt ?? i.CreatedAt) == time && i.Id.CompareTo(id) < 0)))),
                _ => items.Where(i => (i.PublishedAt ?? i.CreatedAt) < time || ((i.PublishedAt ?? i.CreatedAt) == time && i.Id.CompareTo(id) < 0)),
            };
        }

        var ordered = sort switch
        {
            ItemSort.Top => items.OrderByDescending(i => i.WilsonScore),
            ItemSort.Trending => items.OrderByDescending(i => i.HotScore),
            ItemSort.Downloads => items.OrderByDescending(i => i.DownloadsTotal),
            _ => items.OrderByDescending(i => i.PublishedAt ?? i.CreatedAt),
        };
        ordered = sort == ItemSort.New
            ? ordered.ThenByDescending(i => i.Id)
            : ordered.ThenByDescending(i => i.PublishedAt ?? i.CreatedAt).ThenByDescending(i => i.Id);

        var limit = Math.Clamp(query.Limit ?? DefaultLimit, 1, MaxLimit);
        var rows = await ordered.Take(limit + 1).Select(i => new Row(i.Id, i.Kind.Value, i.Slug, i.Title, i.Summary, i.Owner.Handle,
            i.Owner.DisplayName, i.Owner.AvatarSha256, i.ScoreUp, i.ScoreDown, i.WilsonScore, i.HotScore, i.DownloadsTotal, i.PublishedAt,
            i.PublishedAt ?? i.CreatedAt, i.Status, i.OwnerId, i.VisibilityReason)).ToListAsync(ct);

        string? next = null;
        if (rows.Count > limit)
        {
            rows.RemoveAt(limit);
            var last = rows[^1];
            var key = sort switch { ItemSort.Top => last.Wilson, ItemSort.Trending => last.Hot, ItemSort.Downloads => last.Downloads, _ => 0 };
            next = EncodeCursor(new Cursor(sort.ToString()!.ToLowerInvariant(), key, last.SortTime.UtcTicks, last.Id));
        }

        var ids = rows.Select(r => r.Id).ToList();
        var thumbs = (await db.ItemMedia.AsNoTracking().Where(m => ids.Contains(m.ItemId) && (m.Role == MediaRole.Thumbnail || m.Role == MediaRole.PreviewStrip))
            .Select(m => new { m.ItemId, m.Role, m.BlobSha256 }).ToListAsync(ct)).ToList();
        var tags = (await db.ItemTags.AsNoTracking().Where(t => ids.Contains(t.ItemId)).Select(t => new { t.ItemId, t.Tag.Slug }).ToListAsync(ct))
            .GroupBy(t => t.ItemId).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(t => t.Slug).Order(StringComparer.Ordinal).ToList());

        var votes = query.Viewer.UserId is { } viewerId
            ? await db.Votes.AsNoTracking().Where(v => v.UserId == viewerId && ids.Contains(v.ItemId)).ToDictionaryAsync(v => v.ItemId, v => v.Value, ct)
            : [];

        return new ItemPageDto(rows.Select(r => new ItemCardDto(r.Id, r.Kind, r.Slug, r.Title, r.Summary,
            new OwnerDto(r.Handle, r.DisplayName, r.Avatar is { } a ? ItemMapper.FileUrl(a) : null),
            thumbs.FirstOrDefault(m => m.ItemId == r.Id && m.Role == MediaRole.Thumbnail) is { } t ? ItemMapper.FileUrl(t.BlobSha256) : null, r.Up - r.Down, r.Downloads,
            tags.GetValueOrDefault(r.Id) ?? [], r.PublishedAt, r.Status.ToKey(),
            query.Viewer.UserId is null ? null : votes.TryGetValue(r.Id, out var vote) ? vote : null,
            thumbs.FirstOrDefault(m => m.ItemId == r.Id && m.Role == MediaRole.PreviewStrip) is { } strip ? ItemMapper.FileUrl(strip.BlobSha256) : null,
            query.Viewer.IsModerator || r.OwnerId == query.Viewer.UserId ? r.Reason : null)).ToList(), next);
    }

    private static DateTimeOffset? ParsePeriod(string? value, DateTimeOffset now, out bool valid)
    {
        valid = true;
        switch (value?.ToLowerInvariant())
        {
            case null or "" or "all": return null;
            case "day": return now.AddDays(-1);
            case "week": return now.AddDays(-7);
            case "month": return now.AddMonths(-1);
            case "year": return now.AddYears(-1);
            default:
                valid = false;
                return null;
        }
    }

    private static ItemSort? ParseSort(string? value) => value?.ToLowerInvariant() switch
    {
        null or "" or "top" => ItemSort.Top,
        "trending" => ItemSort.Trending,
        "new" => ItemSort.New,
        "downloads" => ItemSort.Downloads,
        _ => null,
    };

    private static Error Bad(string field, string message) =>
        Error.Validation("bad_request", message, [new FieldViolation(field, $"invalid_{field}", message)]);

    private static string EncodeCursor(Cursor cursor) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static Cursor? DecodeCursor(string value)
    {
        try
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            return JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(padded));
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return null;
        }
    }
}

public sealed record GetItemQuery(Viewer Viewer, string? Kind, string? Slug, Guid? Id);

public sealed class GetItemHandler(IAppDbContext db) : IQueryHandler<GetItemQuery, ItemDetailDto>
{
    public async Task<Result<ItemDetailDto>> Handle(GetItemQuery q, CancellationToken ct)
    {
        var items = db.Items.AsNoTracking().WithAll();
        Item? item;
        if (q.Id is { } id) item = await items.FirstOrDefaultAsync(i => i.Id == id, ct);
        else if (AssetKindId.TryParse(q.Kind, out var kind) && q.Slug is { } slug)
            item = await items.FirstOrDefaultAsync(i => i.Kind == kind && i.Slug == slug, ct);
        else item = null;

        var visible = item is not null && (item.Status == ItemStatus.Published
            || (item.Status != ItemStatus.Removed && item.OwnerId == q.Viewer.UserId) || q.Viewer.IsModerator);
        if (!visible) return Error.NotFound("item.not_found", "Item not found.");

        var detail = ItemMapper.Detail(item!, item!.Owner);
        var privileged = q.Viewer.IsModerator || item.OwnerId == q.Viewer.UserId;
        int? viewerVote = q.Viewer.UserId is { } viewerId
            ? await db.Votes.AsNoTracking().Where(v => v.ItemId == item.Id && v.UserId == viewerId).Select(v => (int?)v.Value).FirstOrDefaultAsync(ct)
            : null;
        detail = detail with { ViewerVote = viewerVote };
        if (!privileged) return detail with { VisibilityReason = null };
        var purgeAt = item.Status == ItemStatus.Hidden
            ? await db.CleanupSchedules.AsNoTracking().Where(c => c.ItemId == item.Id).Select(c => (DateTimeOffset?)c.PurgeAt).FirstOrDefaultAsync(ct)
            : null;
        return detail with { PurgeAt = purgeAt };
    }
}

public sealed record GetUserProfileQuery(Viewer Viewer, string Handle, string? Sort, string? Cursor, int? Limit);

public sealed class GetUserProfileHandler(IAppDbContext db, IQueryHandler<ListItemsQuery, ItemPageDto> list)
    : IQueryHandler<GetUserProfileQuery, UserProfileDto>
{
    public async Task<Result<UserProfileDto>> Handle(GetUserProfileQuery q, CancellationToken ct)
    {
        var handle = q.Handle.ToLowerInvariant();
        var profile = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Handle == handle, ct);
        if (profile is null) return Error.NotFound("profile.not_found", "Profile not found.");

        var page = await list.Handle(new ListItemsQuery(q.Viewer, null, null, [], null, q.Sort ?? "new", handle, null, q.Cursor, q.Limit), ct);
        if (page.IsFailure) return page.Error;

        var stats = await db.Items.AsNoTracking().Where(i => i.OwnerId == profile.Id && i.Status == ItemStatus.Published)
            .GroupBy(_ => 1).Select(g => new { Count = g.Count(), Downloads = g.Sum(i => i.DownloadsTotal) }).FirstOrDefaultAsync(ct);
        return new UserProfileDto(profile.Handle, profile.DisplayName,
            profile.AvatarSha256 is { } a ? ItemMapper.FileUrl(a) : null, profile.Bio, profile.TrustLevel.ToString(), profile.CreatedAt,
            stats?.Count ?? 0, stats?.Downloads ?? 0, page.Value);
    }
}

public sealed record RecordDownloadCommand(Guid ItemId, string? Version, Guid? UserId, string? InstallId, string IpHash);

public sealed class RecordDownloadHandler(IAppDbContext db, IClock clock) : ICommandHandler<RecordDownloadCommand, DownloadTarget>
{
    public async Task<Result<DownloadTarget>> Handle(RecordDownloadCommand c, CancellationToken ct)
    {
        var item = await db.Items.AsNoTracking().Include(i => i.Versions).FirstOrDefaultAsync(i => i.Id == c.ItemId, ct);
        if (item is null || item.Status != ItemStatus.Published) return Error.NotFound("item.not_found", "Item not found.");

        var version = c.Version is { Length: > 0 }
            ? item.Versions.FirstOrDefault(v => v.Version == c.Version)
            : item.Versions.FirstOrDefault(v => v.Id == item.CurrentVersionId);
        if (version is null) return Error.NotFound("version.not_found", "Version not found.");

        db.DownloadEvents.Add(new DownloadEvent(item.Id, version.Id, c.UserId, c.InstallId, c.IpHash, clock.UtcNow));
        await db.SaveChangesAsync(ct);
        return new DownloadTarget(version.PackageSha256.Value, $"{item.Slug}-{version.Version}{version.FileExtension}");
    }
}
