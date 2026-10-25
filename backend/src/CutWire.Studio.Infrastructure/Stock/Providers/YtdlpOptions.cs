namespace CutWire.Studio.Infrastructure.Stock.Providers;

/// <summary>Section <c>Stock:Providers:Ytdlp</c>.</summary>
public sealed class YtdlpOptions
{
    public const string Section = "Stock:Providers:Ytdlp";

    public bool Enabled { get; set; } = true;
    public string Path { get; set; } = "yt-dlp";
    public string? CookiesFile { get; set; }
    /// <summary>Raw <c>--extractor-args</c> value; empty lets yt-dlp decide, which is the intended state.</summary>
    public string? ExtractorArgs { get; set; }
    /// <summary>Path-separator list. Only directories that exist are passed: yt-dlp aborts on a missing plugin dir.</summary>
    public string? PluginDirs { get; set; }
    /// <summary>bgutil PO token provider base URL.</summary>
    public string? PotBaseUrl { get; set; }
    public bool ForceIPv6 { get; set; }
}
