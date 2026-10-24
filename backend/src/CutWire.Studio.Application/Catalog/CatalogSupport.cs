using System.Text;
using System.Text.Json;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Blobs;
using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Application.Catalog;

/// <summary>Applies an indexed full-text and fuzzy title filter; implemented where Postgres types are available.</summary>
public interface IItemTextSearch
{
    IQueryable<Item> Apply(IQueryable<Item> items, string text);
}

/// <summary>
/// Maintains <c>blobs.ref_count</c> for every row that references a blob. Each change is a single atomic UPDATE, so concurrent
/// writers never lose an increment; it runs immediately, not with the next <c>SaveChanges</c>.
/// </summary>
public sealed class BlobReferences(IAppDbContext db, IClock clock)
{
    public async Task AddAsync(Sha256Hash hash, CancellationToken ct)
    {
        var updated = await db.Blobs.Where(b => b.Id == hash).ExecuteUpdateAsync(s => s
            .SetProperty(b => b.RefCount, b => b.RefCount + 1)
            .SetProperty(b => b.ZeroRefSince, (DateTimeOffset?)null), ct);
        if (updated == 0) throw new InvalidOperationException($"Blob {hash} does not exist.");
    }

    public async Task ReleaseAsync(Sha256Hash hash, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await db.Blobs.Where(b => b.Id == hash && b.RefCount > 0).ExecuteUpdateAsync(s => s
            .SetProperty(b => b.ZeroRefSince, b => b.RefCount == 1 ? now : b.ZeroRefSince)
            .SetProperty(b => b.RefCount, b => b.RefCount - 1), ct);
    }
}

public static class Slugs
{
    public static string From(string title)
    {
        var sb = new StringBuilder();
        foreach (var c in title.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }

        var slug = sb.ToString().Trim('-');
        if (slug.Length > Item.MaxSlugLength - 5) slug = slug[..(Item.MaxSlugLength - 5)].Trim('-');
        return slug.Length == 0 ? "item" : slug;
    }
}

public static class BoundedRead
{
    public static async Task<byte[]> ReadAsync(Stream stream, long maxBytes, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while (buffer.Length <= maxBytes && (read = await stream.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, maxBytes + 1 - buffer.Length)), ct)) > 0)
            buffer.Write(chunk, 0, read);
        return buffer.ToArray();
    }
}

public sealed record PackageUpload(PackageInfo Info, Sha256Hash Hash, long Size, string Mime);

/// <summary>Validates an uploaded package with its kind handler and stores it as a blob.</summary>
public sealed class PackageIntake(ICommandHandler<StoreBlobCommand, StoreBlobResult> storeBlob)
{
    public async Task<Result<PackageUpload>> IntakeAsync(IAssetKind kind, Stream content, CancellationToken ct)
    {
        var bytes = await BoundedRead.ReadAsync(content, kind.MaxPackageBytes, ct);
        if (bytes.Length > kind.MaxPackageBytes)
            return Error.TooLarge("package_too_large", $"Package is larger than {kind.MaxPackageBytes} bytes.");

        var validated = await kind.ValidateAsync(new MemoryStream(bytes), ct);
        if (validated.IsFailure) return validated.Error;

        var mime = validated.Value.Format switch
        {
            PackageFormat.Json => "application/json",
            PackageFormat.Zip => "application/zip",
            PackageFormat.Cube => "text/plain",
            _ => "application/octet-stream",
        };
        var stored = await storeBlob.Handle(new StoreBlobCommand(new MemoryStream(bytes), mime, kind.MaxPackageBytes), ct);
        if (stored.IsFailure) return stored.Error;
        return new PackageUpload(validated.Value, stored.Value.Hash, stored.Value.Size, stored.Value.Mime);
    }
}

public static class ItemMapper
{
    public static string FileUrl(Sha256Hash hash) => $"/files/{hash}";

    public static OwnerDto Owner(Domain.Accounts.UserProfile p) =>
        new(p.Handle, p.DisplayName, p.AvatarSha256 is { } a ? FileUrl(a) : null);

    public static ItemDetailDto Detail(Item item, Domain.Accounts.UserProfile owner)
    {
        var current = item.Versions.FirstOrDefault(v => v.Id == item.CurrentVersionId);
        var versions = item.Versions
            .Select(v => (v, parsed: SemVersion.TryParse(v.Version, out var s) ? s : null))
            .OrderByDescending(x => x.parsed, Comparer<SemVersion?>.Default)
            .ThenByDescending(x => x.v.CreatedAt)
            .Select(x => new ItemVersionDto(x.v.Id, x.v.Version, x.v.Changelog, x.v.Size, x.v.MinAppVersion, x.v.PackageSha256.Value,
                $"/api/v1/items/{item.Id}/download?version={Uri.EscapeDataString(x.v.Version)}", x.v.CreatedAt, x.v.Id == item.CurrentVersionId))
            .ToList();
        var media = item.Media.OrderBy(m => m.Role)
            .Select(m => new ItemMediaDto(m.Role.ToKey(), FileUrl(m.BlobSha256), m.Width, m.Height, m.Bytes)).ToList();
        var thumb = item.Media.FirstOrDefault(m => m.Role == MediaRole.Thumbnail);
        return new ItemDetailDto(item.Id, item.Kind.Value, item.Slug, item.Title, item.Summary, item.Description, item.License,
            item.Category, item.Status.ToKey(), item.VisibilityReason, Owner(owner),
            item.Tags.Select(t => t.Tag.Slug).Order(StringComparer.Ordinal).ToList(),
            thumb is null ? null : FileUrl(thumb.BlobSha256), item.ScoreUp, item.ScoreDown, item.ScoreUp - item.ScoreDown,
            item.DownloadsTotal, item.PublishedAt, item.CreatedAt, item.UpdatedAt, current?.Version, versions, media, item.KindMeta, null);
    }

    public static JsonElement KindMeta(PackageInfo info)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(new
        {
            packageId = info.PackageId,
            driftInstallKind = info.DriftInstallKind,
            minAppVersion = info.MinAppVersion,
            fileCount = info.Files.Count,
            hasForgeJson = info.Files.Any(f => f.Path.Equals("forge.json", StringComparison.OrdinalIgnoreCase)),
        }));
        return doc.RootElement.Clone();
    }
}

public static class ItemLoader
{
    public static IQueryable<Item> WithAll(this IQueryable<Item> items) =>
        items.Include(i => i.Versions).Include(i => i.Media).Include(i => i.Tags).ThenInclude(t => t.Tag).Include(i => i.Owner);

    public static async Task<Result<Item>> LoadOwnedAsync(IAppDbContext db, Guid itemId, Guid userId, CancellationToken ct)
    {
        var item = await db.Items.WithAll().FirstOrDefaultAsync(i => i.Id == itemId, ct);
        return item is null || item.OwnerId != userId || item.Status == ItemStatus.Removed
            ? Error.NotFound("item.not_found", "Item not found.")
            : item;
    }

    public static Error Reject(string field, string code, string message, params (string, object)[] parameters) =>
        Error.Unprocessable("content_rejected", "The submission does not meet the quality rules.",
            [new FieldViolation(field, code, message, parameters.Length == 0 ? null : parameters.ToDictionary(p => p.Item1, p => p.Item2))]);
}
