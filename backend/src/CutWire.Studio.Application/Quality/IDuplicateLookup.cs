using CutWire.Studio.Domain.Blobs;

namespace CutWire.Studio.Application.Quality;

public interface IDuplicateLookup
{
    /// <summary>Only published and hidden items count; <paramref name="excludeItemId"/> skips the submission's own item.</summary>
    Task<bool> PackageExistsAsync(Sha256Hash packageSha256, Guid? excludeItemId, CancellationToken ct);

    /// <summary>True when any stored thumbnail hash is within <paramref name="maxDistance"/> bits of <paramref name="dHash"/>.</summary>
    Task<bool> SimilarThumbnailExistsAsync(ulong dHash, int maxDistance, Guid? excludeItemId, CancellationToken ct);

    Task<IReadOnlyList<string>> OwnerTitlesAsync(Guid ownerId, Guid? excludeItemId, CancellationToken ct);
}
