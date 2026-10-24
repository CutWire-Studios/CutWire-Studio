using CutWire.Studio.Application.Moderation;
using CutWire.Studio.Application.Quality;
using CutWire.Studio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Infrastructure.Catalog;

public sealed class SimilarItemFinder(StudioDbContext db, IOptions<ContentRulesOptions> rules) : ISimilarItemFinder
{
    public async Task<Guid?> FindAsync(Guid itemId, CancellationToken ct)
    {
        var maxDistance = rules.Value.SimilarThumbnailMaxDistance;
        var rows = await db.Database.SqlQuery<Guid>($"""
            select o.item_id as "Value"
            from item_media own
            join item_media o on o.role = 'thumbnail' and o.item_id <> own.item_id
            join items i on i.id = o.item_id and i.status in ('published', 'hidden')
            where own.item_id = {itemId} and own.role = 'thumbnail'
              and bit_count((o.dhash # own.dhash)::bit(64)) <= {maxDistance}
            order by bit_count((o.dhash # own.dhash)::bit(64)), o.item_id
            limit 1
            """).ToListAsync(ct);
        return rows.Count == 0 ? null : rows[0];
    }
}
