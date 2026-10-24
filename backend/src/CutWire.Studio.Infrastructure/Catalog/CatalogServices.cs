using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Catalog;
using CutWire.Studio.Application.Quality;
using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Infrastructure.Catalog;

public sealed class PostgresItemTextSearch : IItemTextSearch
{
    public IQueryable<Item> Apply(IQueryable<Item> items, string text) =>
        items.Where(i => EF.Property<NpgsqlTypes.NpgsqlTsVector>(i, "SearchVector").Matches(EF.Functions.WebSearchToTsQuery("english", text))
                         || EF.Functions.TrigramsAreSimilar(i.Title, text));
}

public sealed class DuplicateLookup(StudioDbContext db) : IDuplicateLookup
{
    public Task<bool> PackageExistsAsync(Sha256Hash packageSha256, Guid? excludeItemId, CancellationToken ct) =>
        db.Items.AsNoTracking()
            .Where(i => (i.Status == ItemStatus.Published || i.Status == ItemStatus.Hidden) && i.Id != (excludeItemId ?? Guid.Empty))
            .AnyAsync(i => i.Versions.Any(v => v.PackageSha256 == packageSha256), ct);

    public Task<bool> SimilarThumbnailExistsAsync(ulong dHash, int maxDistance, Guid? excludeItemId, CancellationToken ct)
    {
        var hash = unchecked((long)dHash);
        var exclude = excludeItemId ?? Guid.Empty;
        return db.Database.SqlQuery<bool>($"""
            select exists (
              select 1 from item_media m join items i on i.id = m.item_id
              where m.role = 'thumbnail' and i.status in ('published', 'hidden') and i.id <> {exclude}
                and bit_count((m.dhash # {hash})::bit(64)) <= {maxDistance}) as "Value"
            """).SingleAsync(ct);
    }

    public async Task<IReadOnlyList<string>> OwnerTitlesAsync(Guid ownerId, Guid? excludeItemId, CancellationToken ct) =>
        await db.Items.AsNoTracking()
            .Where(i => i.OwnerId == ownerId && (i.Status == ItemStatus.Published || i.Status == ItemStatus.Hidden) && i.Id != (excludeItemId ?? Guid.Empty))
            .Select(i => i.Title).ToListAsync(ct);
}

public sealed class UploadQuota(IAppDbContext db, IClock clock) : IUploadQuota
{
    public async Task<UploadUsage> GetUsageAsync(Guid ownerId, CancellationToken ct)
    {
        var trust = (int)await db.UserProfiles.AsNoTracking().Where(p => p.Id == ownerId).Select(p => p.TrustLevel).FirstOrDefaultAsync(ct);
        var since = clock.UtcNow.AddHours(-24);
        var published = await db.Items.AsNoTracking().CountAsync(i => i.OwnerId == ownerId && i.PublishedAt >= since, ct);
        return new UploadUsage(trust, published);
    }
}

/// <summary>Inserts tags from the kinds' vocabularies that are not in the database yet.</summary>
public sealed class CatalogSeeder(StudioDbContext db, IAssetKindRegistry kinds)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        foreach (var kind in kinds.ListEnabled())
            foreach (var slug in kind.TagVocabulary)
            {
                var id = kind.Id.Value;
                await db.Database.ExecuteSqlAsync($"insert into tags (kind, slug) values ({id}, {slug}) on conflict (kind, slug) do nothing", ct);
            }
    }
}
