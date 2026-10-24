using CutWire.Studio.Domain.Blobs;

namespace CutWire.Studio.Application.Quality;

public interface IDuplicateLookup
{
    Task<bool> PackageExistsAsync(Sha256Hash packageSha256, CancellationToken ct);

    /// <summary>True when any stored thumbnail hash is within <paramref name="maxDistance"/> bits of <paramref name="dHash"/>.</summary>
    Task<bool> SimilarThumbnailExistsAsync(ulong dHash, int maxDistance, CancellationToken ct);

    Task<IReadOnlyList<string>> OwnerTitlesAsync(Guid ownerId, CancellationToken ct);
}
