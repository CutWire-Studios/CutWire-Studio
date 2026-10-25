using System.Text.Json;
using System.Text.Json.Serialization;

namespace CutWire.Studio.Infrastructure.Stock.Providers;

public sealed record YtdlpThumbnail(string? Url, double? Width, double? Height);

/// <summary>The subset of yt-dlp's metadata the stock module uses.</summary>
public sealed record YtdlpInfo
{
    public string? Id { get; init; }
    public string? Title { get; init; }
    public double? Duration { get; init; }
    public string? Thumbnail { get; init; }
    public IReadOnlyList<YtdlpThumbnail>? Thumbnails { get; init; }
    public string? Uploader { get; init; }
    public string? UploaderUrl { get; init; }
    public string? Channel { get; init; }
    public string? ChannelUrl { get; init; }
    public string? WebpageUrl { get; init; }
    public double? Width { get; init; }
    public double? Height { get; init; }
    public string? Extractor { get; init; }
    public string? LiveStatus { get; init; }
    public IReadOnlyList<YtdlpInfo?>? Entries { get; init; }
    public string? Url { get; init; }

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public bool IsLive => LiveStatus is "is_live" or "is_upcoming";

    public int DurationMs => Duration is > 0 ? (int)Math.Min(Duration.Value * 1000, int.MaxValue) : 0;

    public int WidthPx => Width is > 0 ? (int)Width.Value : 0;

    public int HeightPx => Height is > 0 ? (int)Height.Value : 0;

    /// <summary>The widest thumbnail that is not absurdly large.</summary>
    public string BestThumbnail()
    {
        var best = Thumbnail ?? "";
        double bestWidth = 0;
        foreach (var t in Thumbnails ?? [])
        {
            if (!string.IsNullOrEmpty(t.Url) && t.Width is { } w && w > bestWidth && w <= 1280)
            {
                best = t.Url;
                bestWidth = w;
            }
        }
        return best;
    }

    public (string Name, string Url) Creator()
    {
        var name = !string.IsNullOrEmpty(Channel) ? Channel : Uploader ?? "";
        var url = !string.IsNullOrEmpty(ChannelUrl) ? ChannelUrl : UploaderUrl ?? "";
        return (name, url);
    }
}
