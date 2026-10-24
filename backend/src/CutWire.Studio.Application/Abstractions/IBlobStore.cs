using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Common;

namespace CutWire.Studio.Application.Abstractions;

public sealed record StoredBlob(Sha256Hash Hash, long Size, bool AlreadyExisted);

public interface IBlobStore
{
    Task<Result<StoredBlob>> PutAsync(Stream content, long maxBytes, CancellationToken ct);

    /// <summary>Returns a seekable stream positioned at <paramref name="offset"/>, or null when missing.</summary>
    Task<Stream?> OpenAsync(Sha256Hash hash, long offset = 0, CancellationToken ct = default);

    Task<bool> ExistsAsync(Sha256Hash hash, CancellationToken ct = default);
    Task<bool> DeleteAsync(Sha256Hash hash, CancellationToken ct = default);
}
