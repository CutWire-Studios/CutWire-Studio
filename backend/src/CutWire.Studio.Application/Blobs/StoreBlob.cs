using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Application.Blobs;

public sealed record StoreBlobCommand(Stream Content, string Mime, long MaxBytes);

public sealed record StoreBlobResult(Sha256Hash Hash, long Size, string Mime);

public sealed class StoreBlobHandler(IBlobStore store, IAppDbContext db, IClock clock)
    : ICommandHandler<StoreBlobCommand, StoreBlobResult>
{
    public async Task<Result<StoreBlobResult>> Handle(StoreBlobCommand command, CancellationToken ct)
    {
        var mime = command.Mime?.Trim();
        if (string.IsNullOrEmpty(mime) || !mime.Contains('/') || mime.Length > 127)
            return Error.Validation("blob.invalid_mime", "A valid MIME type is required.",
                new Dictionary<string, string[]> { ["mime"] = ["Must look like 'type/subtype'."] });

        var put = await store.PutAsync(command.Content, command.MaxBytes, ct);
        if (put.IsFailure) return put.Error;

        var stored = put.Value;
        var existing = await db.Blobs.FirstOrDefaultAsync(b => b.Id == stored.Hash, ct);
        if (existing is null)
        {
            db.Blobs.Add(new Blob(stored.Hash, stored.Size, mime, clock.UtcNow));
            await db.SaveChangesAsync(ct);
            return new StoreBlobResult(stored.Hash, stored.Size, mime);
        }

        return new StoreBlobResult(existing.Id, existing.Size, existing.Mime);
    }
}
