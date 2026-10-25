using System.Text.RegularExpressions;
using System.Web;
using CutWire.Studio.Application.Stock;

namespace CutWire.Studio.Infrastructure.Stock.Providers;

internal static partial class YoutubeSupport
{
    public const string LicenseName = "YouTube Standard License";
    public const string LicenseUrl = "https://www.youtube.com/static?template=terms";

    // Prefer an already-MP4 pairing so the normalizer can remux rather than re-encode; cap at 1080p to keep job times sane.
    public const string VideoFormat = "bv*[ext=mp4][height<=?1080]+ba[ext=m4a]/b[ext=mp4][height<=?1080]/bv*+ba/b";
    public const string AudioFormat = "ba[ext=m4a]/ba/b";

    [GeneratedRegex("^[A-Za-z0-9_-]{11}$")]
    public static partial Regex IdShape();

    public static ProviderLicense License => new(LicenseName, LicenseUrl);

    public static string WatchUrl(string id) => "https://www.youtube.com/watch?v=" + id;

    public static string ThumbnailUrl(string id) => string.IsNullOrEmpty(id) ? "" : "https://i.ytimg.com/vi/" + id + "/hqdefault.jpg";

    public static int ClampLimit(int n) => n <= 0 ? 20 : Math.Min(n, 30);

    public static StockException Gone() => new(StockReason.ItemMissing, "not a valid youtube id");

    public static string Attribution(string title, string creator, string source) => (title, creator) switch
    {
        ("", "") => source,
        (_, "") => $"{title} ({source})",
        _ => $"{title} by {creator} ({source})",
    };

    public static string? BoundedQuery(string? q)
    {
        q = q?.Trim();
        if (string.IsNullOrEmpty(q)) return null;
        if (q.Length > YtdlpRunner.MaxQueryChars || q.IndexOfAny(['\r', '\n', '\0']) >= 0)
            throw new StockException(StockReason.LinkUnsupported, "search query rejected");
        return q;
    }

    public static ProviderCreator? CreatorOf(YtdlpInfo info)
    {
        var (name, url) = info.Creator();
        return name.Length == 0 ? null : new ProviderCreator(name, url.Length == 0 ? null : url);
    }
}

public static class YoutubeUrls
{
    /// <summary>The 11-character video id from any YouTube URL shape (watch, short, shorts, embed, live, music), or "".</summary>
    public static string VideoId(string? raw)
    {
        if (!Uri.TryCreate(raw?.Trim(), UriKind.Absolute, out var uri)) return "";

        var id = HttpUtility.ParseQueryString(uri.Query)["v"];
        if (id is not null && YoutubeSupport.IdShape().IsMatch(id)) return id;

        var segments = uri.AbsolutePath.Trim('/').Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            if (segments[i] is "shorts" or "embed" or "live" or "v" && i + 1 < segments.Length && YoutubeSupport.IdShape().IsMatch(segments[i + 1]))
                return segments[i + 1];
        }
        return segments.Length == 1 && YoutubeSupport.IdShape().IsMatch(segments[0]) ? segments[0] : "";
    }
}

public sealed class YoutubeVideoProvider(YtdlpRunner runner) : IStockProvider
{
    public static readonly ProviderDescriptor Info = new(
        YtdlpHosts.YoutubeId,
        "YouTube",
        "Search YouTube, or paste a link.",
        "Video from YouTube",
        [new StockService(StockTypeIds.Video, null, new QuotaDefault(1))],
        ProviderCapabilities.Search | ProviderCapabilities.Resolve | ProviderCapabilities.Fulfill);

    public ProviderDescriptor Descriptor => Info;

    public bool OwnsUrl(string url) => YtdlpHosts.Owns(YtdlpHosts.YoutubeId, url);

    public async Task<ProviderPage> SearchAsync(ProviderQuery query, CancellationToken ct)
    {
        var q = YoutubeSupport.BoundedQuery(query.Q);
        if (q is null) return ProviderPage.Empty;

        var limit = YoutubeSupport.ClampLimit(query.Limit);
        var entries = await runner.FlatAsync($"ytsearch{limit}:{q}", limit, ct);
        return new ProviderPage(entries.Where(e => YoutubeSupport.IdShape().IsMatch(e.Id ?? "") && !e.IsLive).Select(ToItem).ToArray());
    }

    public async Task<ProviderItem?> GetItemAsync(string type, string native, CancellationToken ct)
    {
        if (!YoutubeSupport.IdShape().IsMatch(native)) throw YoutubeSupport.Gone();
        var info = await runner.ExtractAsync(YoutubeSupport.WatchUrl(native), ct);
        if (info.IsLive) throw new StockException(StockReason.LiveStream, "live stream");
        return ToItem(info);
    }

    public async Task<ProviderItem> ResolveAsync(string url, CancellationToken ct)
    {
        var id = OwnsUrl(url) ? YoutubeUrls.VideoId(url) : "";
        if (id.Length == 0) throw new StockException(StockReason.LinkUnsupported, "no youtube video id in url");
        return (await GetItemAsync(StockTypeIds.Video, id, ct))!;
    }

    public async Task<ProviderAsset> FetchAsync(FulfillRequest request, StockWorkspace workspace, CancellationToken ct)
    {
        if (!YoutubeSupport.IdShape().IsMatch(request.Native)) throw YoutubeSupport.Gone();
        var info = await runner.ExtractAsync(YoutubeSupport.WatchUrl(request.Native), ct);
        if (info.IsLive) throw new StockException(StockReason.LiveStream, "live stream");

        workspace.Report(0.1);
        var path = await runner.DownloadAsync(YoutubeSupport.WatchUrl(request.Native), workspace, YoutubeSupport.VideoFormat, ct);
        var (creator, _) = info.Creator();
        return new ProviderAsset(
            path,
            "video/mp4",
            ProviderSupport.FileName(info.Title, "mp4"),
            YoutubeSupport.License,
            YoutubeSupport.Attribution(info.Title ?? "", creator, "YouTube"),
            TimeSpan.FromSeconds(info.Duration ?? 0));
    }

    private static ProviderItem ToItem(YtdlpInfo info)
    {
        var thumb = info.BestThumbnail();
        return new ProviderItem
        {
            Native = info.Id!,
            Type = StockTypeIds.Video,
            Title = info.Title ?? "",
            Creator = YoutubeSupport.CreatorOf(info),
            DurationMs = info.DurationMs,
            Width = info.WidthPx,
            Height = info.HeightPx,
            ThumbSrc = thumb.Length > 0 ? thumb : YoutubeSupport.ThumbnailUrl(info.Id!),
            License = YoutubeSupport.License,
        };
    }
}

public sealed class YoutubeMusicProvider(YtdlpRunner runner) : IStockProvider
{
    public static readonly ProviderDescriptor Info = new(
        YtdlpHosts.YoutubeMusicId,
        "YouTube Music",
        "Search YouTube Music, or paste a link.",
        "Audio from YouTube Music",
        [new StockService(StockTypeIds.Audio, null, new QuotaDefault(1))],
        ProviderCapabilities.Search | ProviderCapabilities.Resolve | ProviderCapabilities.Fulfill);

    public ProviderDescriptor Descriptor => Info;

    public bool OwnsUrl(string url) => YtdlpHosts.Owns(YtdlpHosts.YoutubeMusicId, url);

    public async Task<ProviderPage> SearchAsync(ProviderQuery query, CancellationToken ct)
    {
        var q = YoutubeSupport.BoundedQuery(query.Q);
        if (q is null) return ProviderPage.Empty;

        var limit = YoutubeSupport.ClampLimit(query.Limit);
        // The #songs section drops the channels, albums and playlists a bare search mixes in.
        var entries = await runner.FlatAsync("https://music.youtube.com/search?q=" + Uri.EscapeDataString(q) + "#songs", limit, ct);
        return new ProviderPage(entries
            .Where(e => YoutubeSupport.IdShape().IsMatch(e.Id ?? "") && !string.IsNullOrEmpty(e.Title))
            .Select(e => ToItem(e, false))
            .ToArray());
    }

    public async Task<ProviderItem?> GetItemAsync(string type, string native, CancellationToken ct)
    {
        if (!YoutubeSupport.IdShape().IsMatch(native)) throw YoutubeSupport.Gone();
        return ToItem(await runner.ExtractAsync(YoutubeSupport.WatchUrl(native), ct), true);
    }

    public async Task<ProviderItem> ResolveAsync(string url, CancellationToken ct)
    {
        var id = OwnsUrl(url) ? YoutubeUrls.VideoId(url) : "";
        if (id.Length == 0) throw new StockException(StockReason.LinkUnsupported, "no youtube video id in url");
        return (await GetItemAsync(StockTypeIds.Audio, id, ct))!;
    }

    public async Task<ProviderAsset> FetchAsync(FulfillRequest request, StockWorkspace workspace, CancellationToken ct)
    {
        if (!YoutubeSupport.IdShape().IsMatch(request.Native)) throw YoutubeSupport.Gone();
        var info = await runner.ExtractAsync(YoutubeSupport.WatchUrl(request.Native), ct);

        workspace.Report(0.1);
        var path = await runner.DownloadAsync(YoutubeSupport.WatchUrl(request.Native), workspace, YoutubeSupport.AudioFormat, ct);
        var (creator, _) = info.Creator();
        return new ProviderAsset(
            path,
            "audio/mp4",
            ProviderSupport.FileName(info.Title, "m4a"),
            YoutubeSupport.License,
            YoutubeSupport.Attribution(info.Title ?? "", creator, "YouTube Music"),
            TimeSpan.FromSeconds(info.Duration ?? 0));
    }

    private static ProviderItem ToItem(YtdlpInfo info, bool extracted)
    {
        var thumb = extracted ? info.BestThumbnail() : "";
        return new ProviderItem
        {
            Native = info.Id!,
            Type = StockTypeIds.Audio,
            Title = info.Title ?? "",
            Creator = YoutubeSupport.CreatorOf(info),
            DurationMs = info.DurationMs,
            ThumbSrc = thumb.Length > 0 ? thumb : YoutubeSupport.ThumbnailUrl(info.Id!),
            License = YoutubeSupport.License,
        };
    }
}
