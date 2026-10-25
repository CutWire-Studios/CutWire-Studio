using System.Text.Json;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Blobs;
using CutWire.Studio.Application.Catalog;
using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Domain.Forge;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Application.Forge;

public sealed record ForgeDocSummaryDto(Guid Id, string LocalId, string Kind, string Name, int Revision, DateTimeOffset UpdatedAt, string? ThumbUrl);

public sealed record ForgeDocDto(Guid Id, string LocalId, string Kind, string Name, int Revision, DateTimeOffset UpdatedAt, JsonElement Doc);

public sealed record ForgeSavedDto(int Revision, DateTimeOffset UpdatedAt);

/// <summary>A document as loaded for an edit: its JSON text and the revision to save against.</summary>
public sealed record ForgeLoaded(Guid Id, string Kind, string Name, int Revision, DateTimeOffset UpdatedAt, string DocJson);

/// <summary>The cloud library. Every write is a revision-checked compare-and-swap, so concurrent editors never overwrite each other silently.</summary>
public sealed class ForgeDocuments(IAppDbContext db, IClock clock, BlobReferences refs, IForgeEventHub events,
    ICommandHandler<StoreBlobCommand, StoreBlobResult> storeBlob, IImageInspector images)
{
    public const long MaxThumbBytes = 64 * 1024;

    public async Task<IReadOnlyList<ForgeDocSummaryDto>> ListAsync(Guid userId, CancellationToken ct)
    {
        var rows = await db.ForgeDocuments.AsNoTracking().Where(d => d.OwnerId == userId)
            .OrderByDescending(d => d.UpdatedAt)
            .Select(d => new { d.Id, d.LocalId, d.Kind, d.Name, d.Revision, d.UpdatedAt, d.ThumbSha256 })
            .ToListAsync(ct);
        return rows.Select(d => new ForgeDocSummaryDto(d.Id, d.LocalId, d.Kind, d.Name, d.Revision, d.UpdatedAt,
            d.ThumbSha256 is { } t ? ItemMapper.FileUrl(t) : null)).ToList();
    }

    public Task<bool> ExistsAsync(Guid userId, Guid id, CancellationToken ct) =>
        db.ForgeDocuments.AnyAsync(d => d.Id == id && d.OwnerId == userId, ct);

    public async Task<Result<ForgeLoaded>> LoadAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var row = await db.ForgeDocuments.AsNoTracking().Where(d => d.Id == id && d.OwnerId == userId)
            .Select(d => new ForgeLoaded(d.Id, d.Kind, d.Name, d.Revision, d.UpdatedAt, d.DocJson))
            .FirstOrDefaultAsync(ct);
        return row is null ? NotFound() : row;
    }

    public async Task<Result<ForgeDocDto>> GetAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var row = await db.ForgeDocuments.AsNoTracking().Where(d => d.Id == id && d.OwnerId == userId).FirstOrDefaultAsync(ct);
        return row is null ? NotFound() : ToDto(row);
    }

    public async Task<Result<ForgeDocDto>> CreateAsync(Guid userId, string? localId, string docJson, CancellationToken ct)
    {
        localId = localId?.Trim();
        if (string.IsNullOrEmpty(localId) || localId.Length > ForgeDocument.MaxLocalIdLength)
            return Error.Validation("local_id_invalid", $"localId must be 1-{ForgeDocument.MaxLocalIdLength} characters.");
        var info = ForgeDocJson.Inspect(docJson);
        if (info.IsFailure) return info.Error;

        var existing = await db.ForgeDocuments.AsNoTracking().Where(d => d.OwnerId == userId && d.LocalId == localId)
            .Select(d => (Guid?)d.Id).FirstOrDefaultAsync(ct);
        if (existing is { } existingId) return Exists(existingId);
        if (await db.ForgeDocuments.CountAsync(d => d.OwnerId == userId, ct) >= ForgeDocument.MaxPerUser)
            return Error.Conflict("document_limit", $"You can keep at most {ForgeDocument.MaxPerUser} documents in the cloud.");

        var missing = await MissingBlobAsync(info.Value.Blobs, ct);
        if (missing is not null) return missing;

        var document = new ForgeDocument(userId, localId, info.Value.Kind, info.Value.Name, docJson, clock.UtcNow);
        await using var tx = await db.BeginTransactionAsync(ct);
        db.ForgeDocuments.Add(document);
        foreach (var blob in info.Value.Blobs) db.ForgeDocAssets.Add(new ForgeDocAsset(document.Id, blob));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            var raced = await db.ForgeDocuments.AsNoTracking().Where(d => d.OwnerId == userId && d.LocalId == localId)
                .Select(d => (Guid?)d.Id).FirstOrDefaultAsync(ct);
            if (raced is { } id) return Exists(id);
            throw;
        }

        foreach (var blob in info.Value.Blobs) await refs.AddAsync(blob, ct);
        await tx.CommitAsync(ct);
        return ToDto(document);
    }

    public async Task<Result<ForgeSavedDto>> UpdateAsync(Guid userId, Guid id, string docJson, int baseRevision, string source,
        string? clientId, CancellationToken ct)
    {
        var info = ForgeDocJson.Inspect(docJson);
        if (info.IsFailure) return info.Error;
        var missing = await MissingBlobAsync(info.Value.Blobs, ct);
        if (missing is not null) return missing;

        await using var tx = await db.BeginTransactionAsync(ct);
        var now = clock.UtcNow;
        var changed = await db.ForgeDocuments
            .Where(d => d.Id == id && d.OwnerId == userId && d.Revision == baseRevision && d.Kind == info.Value.Kind)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.DocJson, docJson)
                .SetProperty(d => d.Name, info.Value.Name)
                .SetProperty(d => d.Revision, baseRevision + 1)
                .SetProperty(d => d.UpdatedAt, now), ct);
        if (changed == 0)
        {
            var current = await db.ForgeDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == userId, ct);
            if (current is null) return NotFound();
            if (current.Kind != info.Value.Kind) return Error.Validation("kind_changed", "A document's kind cannot change.");
            var dto = ToDto(current);
            return Error.Conflict("revision_conflict", "The document changed since you last loaded it.")
                .With("current", new { revision = dto.Revision, updatedAt = dto.UpdatedAt, doc = dto.Doc });
        }

        var before = await db.ForgeDocAssets.Where(a => a.DocumentId == id).Select(a => a.BlobSha256).ToListAsync(ct);
        foreach (var added in info.Value.Blobs.Except(before))
        {
            db.ForgeDocAssets.Add(new ForgeDocAsset(id, added));
            await refs.AddAsync(added, ct);
        }

        foreach (var removed in before.Except(info.Value.Blobs))
        {
            await db.ForgeDocAssets.Where(a => a.DocumentId == id && a.BlobSha256 == removed).ExecuteDeleteAsync(ct);
            await refs.ReleaseAsync(removed, ct);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        events.Publish(id, new ForgeDocEvent("updated", baseRevision + 1, now, source, clientId));
        return new ForgeSavedDto(baseRevision + 1, now);
    }

    public async Task<Result<bool>> DeleteAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var row = await db.ForgeDocuments.AsNoTracking().Where(d => d.Id == id && d.OwnerId == userId)
            .Select(d => new { d.ThumbSha256 }).FirstOrDefaultAsync(ct);
        if (row is null) return NotFound();

        await using var tx = await db.BeginTransactionAsync(ct);
        var blobs = await db.ForgeDocAssets.Where(a => a.DocumentId == id).Select(a => a.BlobSha256).ToListAsync(ct);
        if (await db.ForgeDocuments.Where(d => d.Id == id && d.OwnerId == userId).ExecuteDeleteAsync(ct) == 0) return NotFound();
        foreach (var blob in blobs) await refs.ReleaseAsync(blob, ct);
        if (row.ThumbSha256 is { } thumb) await refs.ReleaseAsync(thumb, ct);
        await tx.CommitAsync(ct);
        events.Publish(id, new ForgeDocEvent("deleted"));
        return true;
    }

    public async Task<Result<bool>> SetThumbAsync(Guid userId, Guid id, Stream content, string? mime, CancellationToken ct)
    {
        var current = await db.ForgeDocuments.AsNoTracking().Where(d => d.Id == id && d.OwnerId == userId)
            .Select(d => new { d.ThumbSha256 }).FirstOrDefaultAsync(ct);
        if (current is null) return NotFound();

        mime = mime?.Split(';')[0].Trim().ToLowerInvariant();
        if (mime is not ("image/webp" or "image/png"))
            return Error.Validation("thumb_invalid", "The thumbnail must be image/webp or image/png.");
        var data = await BoundedRead.ReadAsync(content, MaxThumbBytes, ct);
        if (data.Length > MaxThumbBytes) return Error.TooLarge("thumb_too_large", "The thumbnail can be at most 64 KB.");
        if (images.Inspect(new MemoryStream(data)) is not { } info || info.Mime != mime)
            return Error.Validation("thumb_invalid", "The thumbnail isn't a valid image of that type.");

        var stored = await storeBlob.Handle(new StoreBlobCommand(new MemoryStream(data), mime, MaxThumbBytes), ct);
        if (stored.IsFailure) return stored.Error;
        var hash = stored.Value.Hash;
        if (current.ThumbSha256 == hash) return true;

        await using var tx = await db.BeginTransactionAsync(ct);
        var old = current.ThumbSha256;
        var changed = await db.ForgeDocuments
            .Where(d => d.Id == id && d.OwnerId == userId && d.ThumbSha256 == old)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.ThumbSha256, (Sha256Hash?)hash), ct);
        if (changed == 0) return Error.Conflict("thumb_conflict", "The thumbnail changed meanwhile; try again.");
        await refs.AddAsync(hash, ct);
        if (old is { } previous) await refs.ReleaseAsync(previous, ct);
        await tx.CommitAsync(ct);
        return true;
    }

    private async Task<Error?> MissingBlobAsync(IReadOnlySet<Sha256Hash> blobs, CancellationToken ct)
    {
        if (blobs.Count == 0) return null;
        var present = await db.Blobs.Where(b => blobs.Contains(b.Id)).Select(b => b.Id).ToListAsync(ct);
        var missing = blobs.Except(present).FirstOrDefault();
        return blobs.Count == present.Count
            ? null
            : Error.Validation("asset_blob_missing", $"Blob {missing} hasn't been uploaded; POST it to /api/v1/forge/assets first.");
    }

    private static ForgeDocDto ToDto(ForgeDocument d)
    {
        using var parsed = JsonDocument.Parse(d.DocJson);
        return new ForgeDocDto(d.Id, d.LocalId, d.Kind, d.Name, d.Revision, d.UpdatedAt, parsed.RootElement.Clone());
    }

    private static Error NotFound() => Error.NotFound("document_not_found", "Document not found.");

    private static Error Exists(Guid id) => Error.Conflict("document_exists", "A document with this localId already exists.").With("id", id);
}
