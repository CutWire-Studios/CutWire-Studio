namespace CutWire.Studio.Application.Quality;

public sealed record UploadUsage(int TrustLevel, int UploadsInLastDay);

public interface IUploadQuota
{
    Task<UploadUsage> GetUsageAsync(Guid ownerId, CancellationToken ct);
}
