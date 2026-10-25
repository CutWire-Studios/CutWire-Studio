using CutWire.Studio.Application.Stock;

namespace CutWire.Studio.Infrastructure.Stock.Providers;

/// <summary>
/// Resolve-only pasted-link sources (Instagram, Facebook, TikTok, Vimeo, X, SoundCloud): one extractor, different configuration.
/// The native id is the page URL itself.
/// </summary>
public sealed class SocialProvider : IStockProvider
{
    private sealed record Media(string Kind, string Format, string Mime, string Ext, string Noun, string NounCapitalised);

    private static readonly Media AsVideo = new(StockTypeIds.Video, "bv*[ext=mp4]+ba[ext=m4a]/b[ext=mp4]/bv*+ba/b", "video/mp4", "mp4", "video", "Video");
    private static readonly Media AsAudio = new(StockTypeIds.Audio, "ba[ext=m4a]/ba/b", "audio/mp4", "m4a", "track", "Track");

    private readonly YtdlpRunner _runner;
    private readonly Media _media;
    private readonly string _label;
    private readonly ProviderLicense _license;

    private SocialProvider(YtdlpRunner runner, string id, string label, string description, Media media, ProviderLicense license)
    {
        _runner = runner;
        _media = media;
        _label = label;
        _license = license;
        Descriptor = new ProviderDescriptor(
            id, label, description,
            $"{media.NounCapitalised} from {label}",
            [new StockService(media.Kind, null, new QuotaDefault(3))],
            ProviderCapabilities.Resolve | ProviderCapabilities.Fulfill);
    }

    public ProviderDescriptor Descriptor { get; }

    public static SocialProvider Instagram(YtdlpRunner r) => new(r, YtdlpHosts.InstagramId, "Instagram", "Paste a post or reel link.", AsVideo,
        new("Instagram Terms of Use", "https://help.instagram.com/581066165581870"));

    public static SocialProvider Facebook(YtdlpRunner r) => new(r, YtdlpHosts.FacebookId, "Facebook", "Paste a video or reel link.", AsVideo,
        new("Facebook Terms of Service", "https://www.facebook.com/terms.php"));

    public static SocialProvider TikTok(YtdlpRunner r) => new(r, YtdlpHosts.TikTokId, "TikTok", "Paste a video link.", AsVideo,
        new("TikTok Terms of Service", "https://www.tiktok.com/legal/page/row/terms-of-service/en"));

    public static SocialProvider Vimeo(YtdlpRunner r) => new(r, YtdlpHosts.VimeoId, "Vimeo", "Paste a video link.", AsVideo,
        new("Vimeo Terms of Service", "https://vimeo.com/terms"));

    public static SocialProvider X(YtdlpRunner r) => new(r, YtdlpHosts.XId, "X", "Paste a post link.", AsVideo,
        new("X Terms of Service", "https://x.com/en/tos"));

    public static SocialProvider SoundCloud(YtdlpRunner r) => new(r, YtdlpHosts.SoundCloudId, "SoundCloud", "Paste a track link.", AsAudio,
        new("SoundCloud Terms of Use", "https://soundcloud.com/terms-of-use"));

    public bool OwnsUrl(string url) => YtdlpHosts.Owns(Descriptor.Id, url);

    public async Task<ProviderItem> ResolveAsync(string url, CancellationToken ct)
    {
        if (!OwnsUrl(url)) throw new StockException(StockReason.LinkUnsupported, "url not owned by " + Descriptor.Id);
        return ToItem(await _runner.ExtractAsync(url, ct), url);
    }

    public async Task<ProviderItem?> GetItemAsync(string type, string native, CancellationToken ct)
    {
        if (!OwnsUrl(native)) throw new StockException(StockReason.ItemMissing, "native id is not an owned url");
        return ToItem(await _runner.ExtractAsync(native, ct), native);
    }

    public async Task<ProviderAsset> FetchAsync(FulfillRequest request, StockWorkspace workspace, CancellationToken ct)
    {
        if (!OwnsUrl(request.Native)) throw new StockException(StockReason.ItemMissing, "native id is not an owned url");

        var info = await _runner.ExtractAsync(request.Native, ct);
        workspace.Report(0.1);
        var path = await _runner.DownloadAsync(request.Native, workspace, _media.Format, ct);
        var (creator, _) = info.Creator();
        return new ProviderAsset(
            path,
            _media.Mime,
            ProviderSupport.FileName(Title(info), _media.Ext),
            _license,
            creator.Length == 0 ? $"{_media.NounCapitalised} from {_label}" : $"{creator} on {_label}",
            TimeSpan.FromSeconds(info.Duration ?? 0));
    }

    public string Title(YtdlpInfo info)
    {
        var title = info.Title?.Trim();
        if (!string.IsNullOrEmpty(title)) return title;
        var (name, _) = info.Creator();
        return name.Length > 0 ? $"{_label} {_media.Noun} by {name}" : $"{_label} {_media.Noun}";
    }

    public ProviderItem ToItem(YtdlpInfo info, string pageUrl) => new()
    {
        Native = pageUrl,
        Type = _media.Kind,
        Title = Title(info),
        Creator = YoutubeSupport.CreatorOf(info),
        DurationMs = info.DurationMs,
        Width = info.WidthPx,
        Height = info.HeightPx,
        ThumbSrc = info.BestThumbnail(),
        License = _license,
    };
}
