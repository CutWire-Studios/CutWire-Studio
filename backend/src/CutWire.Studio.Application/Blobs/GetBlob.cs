using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Application.Blobs;

public sealed record GetBlobQuery(string Sha256);

public sealed record BlobContent(Sha256Hash Hash, Stream Content, string Mime, long Size, DateTimeOffset CreatedAt);

public sealed class GetBlobHandler(IBlobStore store, IAppDbContext db) : IQueryHandler<GetBlobQuery, BlobContent>
{
    private static readonly Error NotFound = Error.NotFound("blob.not_found", "Blob not found.");

    public async Task<Result<BlobContent>> Handle(GetBlobQuery query, CancellationToken ct)
    {
        if (!Sha256Hash.TryParse(query.Sha256, out var hash)) return NotFound;

        var blob = await db.Blobs.AsNoTracking().FirstOrDefaultAsync(b => b.Id == hash, ct);
        if (blob is null) return NotFound;

        var stream = await store.OpenAsync(hash, ct: ct);
        if (stream is null) return NotFound;

        return new BlobContent(hash, stream, blob.Mime, blob.Size, blob.CreatedAt);
    }
}
