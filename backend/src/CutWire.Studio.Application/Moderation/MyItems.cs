using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Catalog;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Application.Moderation;

public sealed record MyItemDto(
    Guid Id, string Kind, string Slug, string Title, string Summary, string Status, string? VisibilityReason, DateTimeOffset? PurgeAt,
    string? ThumbnailUrl, double ScoreUp, double ScoreDown, double Score, long Downloads, DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record MyItemPageDto(IReadOnlyList<MyItemDto> Items, string? NextCursor);

public sealed record ListMyItemsQuery(Guid UserId, string? Status, string? Cursor, int? Limit);

public sealed class ListMyItemsHandler(IAppDbContext db) : IQueryHandler<ListMyItemsQuery, MyItemPageDto>
{
    private sealed record Cursor(long T, Guid Id);

    public async Task<Result<MyItemPageDto>> Handle(ListMyItemsQuery q, CancellationToken ct)
    {
        List<ItemStatus> statuses = [ItemStatus.Draft, ItemStatus.Published, ItemStatus.Hidden];
        if (q.Status is { Length: > 0 } status && status != "all")
        {
            if (!ItemStatusExtensions.TryParse(status, out var parsed) || parsed == ItemStatus.Removed)
                return Error.Validation("bad_request", "Status must be draft, published, hidden or all.",
                    [new FieldViolation("status", "invalid_status", "Status must be draft, published, hidden or all.")]);
            statuses = [parsed];
        }

        var items = db.Items.AsNoTracking().Where(i => i.OwnerId == q.UserId && statuses.Contains(i.Status));
        if (!string.IsNullOrEmpty(q.Cursor))
        {
            var cursor = Cursors.Decode<Cursor>(q.Cursor);
            if (cursor is null) return Cursors.Invalid();
            var t = new DateTimeOffset(cursor.T, TimeSpan.Zero);
            var id = cursor.Id;
            items = items.Where(i => i.UpdatedAt < t || (i.UpdatedAt == t && i.Id.CompareTo(id) < 0));
        }

        var take = Math.Clamp(q.Limit ?? 24, 1, 50) + 1;
        var rows = await items.OrderByDescending(i => i.UpdatedAt).ThenByDescending(i => i.Id).Take(take).ToListAsync(ct);
        string? next = null;
        if (rows.Count == take)
        {
            rows.RemoveAt(take - 1);
            next = Cursors.Encode(new Cursor(rows[^1].UpdatedAt.UtcTicks, rows[^1].Id));
        }

        var ids = rows.Select(r => r.Id).ToList();
        var thumbs = (await db.ItemMedia.AsNoTracking().Where(m => ids.Contains(m.ItemId) && m.Role == MediaRole.Thumbnail)
            .Select(m => new { m.ItemId, m.BlobSha256 }).ToListAsync(ct)).ToDictionary(m => m.ItemId, m => m.BlobSha256);
        var purge = await db.CleanupSchedules.AsNoTracking().Where(c => ids.Contains(c.ItemId)).ToDictionaryAsync(c => c.ItemId, c => c.PurgeAt, ct);
        return new MyItemPageDto(rows.Select(i => new MyItemDto(i.Id, i.Kind.Value, i.Slug, i.Title, i.Summary, i.Status.ToKey(),
            i.VisibilityReason, i.Status == ItemStatus.Hidden && purge.TryGetValue(i.Id, out var p) ? p : null,
            thumbs.TryGetValue(i.Id, out var th) ? ItemMapper.FileUrl(th) : null, i.ScoreUp, i.ScoreDown, i.ScoreUp - i.ScoreDown,
            i.DownloadsTotal, i.PublishedAt, i.CreatedAt, i.UpdatedAt)).ToList(), next);
    }
}
