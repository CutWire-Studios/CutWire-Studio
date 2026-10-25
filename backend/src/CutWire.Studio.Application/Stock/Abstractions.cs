using CutWire.Studio.Domain.Common;

namespace CutWire.Studio.Application.Stock;

public sealed class StockJobOptions
{
    public const string Section = "Stock:Jobs";

    public int Workers { get; set; } = 4;
    public int QueueCapacity { get; set; } = 256;
    public int PerClientJobs { get; set; } = 2;
    public TimeSpan JobTimeout { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan FileTtl { get; set; } = TimeSpan.FromHours(1);
    public long MaxDownloadBytes { get; set; } = 512L << 20;
    public int ResolveWorkers { get; set; } = 4;
    public TimeSpan ResolveTimeout { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan ResolveTtl { get; set; } = TimeSpan.FromMinutes(15);
    public int MaxResolveJobs { get; set; } = 4096;
    public TimeSpan QuotaRetention { get; set; } = TimeSpan.FromDays(30);
}

/// <summary>Who is calling: an install id (or IP fallback) for anonymous quota, plus the signed-in account when there is one.</summary>
public sealed record StockCaller(string ClientId, Guid? UserId);

/// <summary>What a sealed media token carries. Exactly one of <see cref="Source"/> (preview relay) or <see cref="BlobSha256"/> (download result) is set.</summary>
public sealed record MediaClaims(string? Source, string? BlobSha256, string? Filename, string? Mime, long Expires);

public interface IMediaSealer
{
    string Seal(MediaClaims claims);

    /// <summary>Null when the token is malformed, tampered with, or expired.</summary>
    MediaClaims? Open(string token);

    long PreviewExpiry();
}

/// <summary>Turns origin URLs into client URLs. Origins never reach a client: images go through imgproxy (when configured) via a sealed relay URL, everything else through the relay.</summary>
public interface IMediaUrls
{
    string Thumb(string source);
    string Preview(string source, string mime);
    string FileUrl(string sha256, string filename, string mime, DateTimeOffset expires);
}

public sealed record NormalizedMedia(string Path, string Mime, string Filename, long Size, string Sha256);

public interface IMediaNormalizer
{
    Task<NormalizedMedia> NormalizeAsync(ProviderAsset asset, string dir, CancellationToken ct);
}

public interface IDownloadQueue
{
    bool TryEnqueue(Guid jobId);
}

public interface IStockHttpClients
{
    /// <summary>A client that refuses non-public addresses (SSRF-safe), for fetching provider-supplied URLs.</summary>
    HttpClient Public { get; }
}

public static class StockErrors
{
    public static Error From(StockException ex)
    {
        var info = StockReasons.Info(ex.Reason);
        return info.Status switch
        {
            404 => Error.NotFound(info.Code, info.Detail),
            422 => Error.Unprocessable(info.Code, info.Detail),
            _ => Error.Unavailable(info.Code, info.Detail),
        };
    }

    public static readonly Error ItemGone = Error.NotFound("stock.item_missing", "That item is no longer available.");
    public static readonly Error ProviderNotFound = Error.NotFound("stock.provider_not_found", "That source is not available.");
}
