namespace CutWire.Studio.Application.Stock;

/// <summary>Finer-grained cause of a stock failure. Each maps to a stable <c>stock.&lt;reason&gt;</c> problem code and user-facing sentence.</summary>
public enum StockReason
{
    SourceBlocked,
    SourceUnreachable,
    SourceTimeout,
    SourceDisabled,
    SourceError,
    ItemRemoved,
    ItemPrivate,
    ItemGeoblocked,
    ItemMissing,
    LinkNoMedia,
    LinkUnsupported,
    FileTooLarge,
    LiveStream,
    DownloadError,
}

public sealed record StockReasonInfo(string Code, int Status, string Detail);

public static class StockReasons
{
    public static StockReasonInfo Info(StockReason reason) => reason switch
    {
        StockReason.SourceBlocked => new("stock.source_blocked", 503, "This source is refusing automated requests right now. Try again later."),
        StockReason.SourceUnreachable => new("stock.source_unreachable", 503, "Could not reach this source. Try again in a few minutes."),
        StockReason.SourceTimeout => new("stock.source_timeout", 503, "This source took too long to respond. Try again."),
        StockReason.SourceDisabled => new("stock.source_disabled", 503, "That source is switched off right now."),
        StockReason.SourceError => new("stock.source_error", 503, "This source is temporarily unavailable."),
        StockReason.ItemRemoved => new("stock.item_removed", 404, "That item has been removed by whoever posted it."),
        StockReason.ItemPrivate => new("stock.item_private", 404, "That item is private or age-restricted, so it cannot be fetched."),
        StockReason.ItemGeoblocked => new("stock.item_geoblocked", 404, "That item is not available in this region."),
        StockReason.ItemMissing => new("stock.item_missing", 404, "That item is no longer available."),
        StockReason.LinkNoMedia => new("stock.link_no_media", 404, "That link does not contain a video or audio file."),
        StockReason.LinkUnsupported => new("stock.link_unsupported", 404, "Links from that site are not supported."),
        StockReason.FileTooLarge => new("stock.file_too_large", 422, "That file is too large to prepare."),
        StockReason.LiveStream => new("stock.live_stream", 422, "That is a live stream, which cannot be downloaded as a file."),
        StockReason.DownloadError => new("stock.download_error", 422, "Could not prepare that file."),
        _ => new("stock.source_error", 503, "This source is temporarily unavailable."),
    };
}

/// <summary>Thrown by providers and the download pipeline. The message is internal; clients only ever see <see cref="StockReasons"/> text.</summary>
public sealed class StockException(StockReason reason, string? internalMessage = null, Exception? inner = null)
    : Exception(internalMessage ?? reason.ToString(), inner)
{
    public StockReason Reason { get; } = reason;
}
