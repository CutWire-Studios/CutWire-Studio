using System.Globalization;
using System.Text.Json.Serialization;
using CutWire.Studio.Application.Stock;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Infrastructure.Stock.Providers;

public sealed class PixabayOptions
{
    public const string Section = "Stock:Providers:Pixabay";

    public string ApiKey { get; set; } = "";
    public string ImageApi { get; set; } = "https://pixabay.com/api/";
    public string VideoApi { get; set; } = "https://pixabay.com/api/videos/";
}

public static class PixabayRegistration
{
    public static bool IsConfigured(IConfiguration config) => !string.IsNullOrWhiteSpace(config.GetSection(PixabayOptions.Section).Get<PixabayOptions>()?.ApiKey);

    public static void Register(IServiceCollection services, IConfiguration config)
    {
        var section = config.GetSection(PixabayOptions.Section);
        if (string.IsNullOrWhiteSpace(section.Get<PixabayOptions>()?.ApiKey)) return;

        services.AddOptions<PixabayOptions>().Bind(section);
        services.AddHttpClient(nameof(PixabayProvider));
        services.AddSingleton(sp => new PixabayProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(PixabayProvider)),
            sp.GetRequiredService<IProviderCache>(),
            sp.GetRequiredService<IOptions<StockOptions>>(),
            sp.GetRequiredService<IOptions<PixabayOptions>>()));
        services.AddSingleton<IStockProvider>(sp => sp.GetRequiredService<PixabayProvider>());
    }
}

public sealed class PixabayProvider(HttpClient http, IProviderCache cache, IOptions<StockOptions> stock, IOptions<PixabayOptions> options) : IStockProvider
{
    public const string Id = "pixabay";
    private const string LicenseName = "Pixabay Content License";
    private const string LicenseUrl = "https://pixabay.com/service/license-summary/";
    private static readonly TimeSpan MinCacheTtl = TimeSpan.FromHours(24);
    private static readonly ProviderLicense License = new(LicenseName, LicenseUrl);

    private static readonly string[] Categories =
    [
        "backgrounds", "fashion", "nature", "science", "education", "feelings", "health", "people", "religion", "places", "animals",
        "industry", "computer", "food", "sports", "transportation", "travel", "buildings", "business", "music",
    ];

    private static readonly FilterDef Category = new("category", "Category", FilterKinds.Select,
        [.. Categories.Select(c => new FilterOption(c, char.ToUpperInvariant(c[0]) + c[1..]))]);

    private static readonly FilterDef EditorsChoice = new("editors_choice", "Editor's choice", FilterKinds.Toggle);

    private readonly RestClient _rest = new(http, cache, stock.Value.SearchCacheTtl < MinCacheTtl ? MinCacheTtl : stock.Value.SearchCacheTtl);
    private readonly PixabayOptions _o = options.Value;

    public ProviderDescriptor Descriptor { get; } = new(
        Id, "Pixabay", "Free photos and video under the Pixabay licence.", "Content from Pixabay",
        [
            new StockService(StockTypeIds.Photo,
            [
                new FilterDef("orientation", "Orientation", FilterKinds.Select, [new("horizontal", "Landscape"), new("vertical", "Portrait")]),
                Category,
                EditorsChoice,
            ]),
            new StockService(StockTypeIds.Video,
            [
                new FilterDef("video_type", "Kind", FilterKinds.Select, [new("film", "Film"), new("animation", "Animation")]),
                Category,
                EditorsChoice,
            ]),
        ],
        ProviderCapabilities.Search | ProviderCapabilities.Fulfill);

    public async Task<ProviderPage> SearchAsync(ProviderQuery query, CancellationToken ct)
    {
        var page = ProviderSupport.Page(query.Cursor);
        var limit = Math.Clamp(ProviderSupport.ClampLimit(query.Limit, 200), 3, 200);
        var photo = query.Type == StockTypeIds.Photo;

        var p = Params();
        p.Add(new("per_page", limit.ToString(CultureInfo.InvariantCulture)));
        p.Add(new("page", page.ToString(CultureInfo.InvariantCulture)));
        p.Add(new("safesearch", "true"));
        p.Add(new("q", query.Q));
        p.Add(new("category", query.Filter("category")));
        p.Add(new("orientation", query.Filter("orientation")));
        p.Add(new("video_type", query.Filter("video_type")));
        if (query.Filter("editors_choice") is "true" or "1") p.Add(new("editors_choice", "true"));

        if (photo)
        {
            p.Add(new("image_type", "photo"));
            var res = await _rest.GetJsonAsync<PixabayImageResponse>(RestClient.Url(_o.ImageApi, p), ct);
            var items = res.Hits.Select(ToItem).ToList();
            return new ProviderPage(items, ProviderSupport.NextCursor(page, items.Count, limit));
        }
        else
        {
            var res = await _rest.GetJsonAsync<PixabayVideoResponse>(RestClient.Url(_o.VideoApi, p), ct);
            var items = res.Hits.Select(ToItem).ToList();
            return new ProviderPage(items, ProviderSupport.NextCursor(page, items.Count, limit));
        }
    }

    public async Task<ProviderItem?> GetItemAsync(string type, string native, CancellationToken ct) =>
        type == StockTypeIds.Photo ? ToItem(await ImageAsync(native, ct)) : ToItem(await VideoAsync(native, ct));

    public async Task<ProviderAsset> FetchAsync(FulfillRequest request, StockWorkspace workspace, CancellationToken ct)
    {
        if (request.Type == StockTypeIds.Photo)
        {
            var hit = await ImageAsync(request.Native, ct);
            var name = ProviderSupport.FileName(Title(hit.Tags), "jpg");
            var path = await ProviderSupport.DownloadAsync(workspace, Best(hit), name, ct);
            return new ProviderAsset(path, "image/jpeg", name, License, Attribution(hit.User));
        }

        var video = await VideoAsync(request.Native, ct);
        var rendition = Pick(video, request.VariantId) ?? throw new StockException(StockReason.ItemMissing, "no matching rendition");
        var filename = ProviderSupport.FileName(Title(video.Tags), "mp4");
        var saved = await ProviderSupport.DownloadAsync(workspace, rendition.Url, filename, ct);
        return new ProviderAsset(saved, "video/mp4", filename, License, Attribution(video.User), TimeSpan.FromSeconds(video.Duration));
    }

    private List<KeyValuePair<string, string?>> Params() => [new("key", _o.ApiKey)];

    private async Task<PixabayImageHit> ImageAsync(string id, CancellationToken ct)
    {
        var p = Params();
        p.Add(new("id", id));
        var res = await _rest.GetJsonAsync<PixabayImageResponse>(RestClient.Url(_o.ImageApi, p), ct);
        return res.Hits.FirstOrDefault() ?? throw new StockException(StockReason.ItemMissing, "no hits");
    }

    private async Task<PixabayVideoHit> VideoAsync(string id, CancellationToken ct)
    {
        var p = Params();
        p.Add(new("id", id));
        var res = await _rest.GetJsonAsync<PixabayVideoResponse>(RestClient.Url(_o.VideoApi, p), ct);
        return res.Hits.FirstOrDefault() ?? throw new StockException(StockReason.ItemMissing, "no hits");
    }

    private static ProviderItem ToItem(PixabayImageHit h) => new()
    {
        Native = h.Id.ToString(CultureInfo.InvariantCulture),
        Type = StockTypeIds.Photo,
        Title = Title(h.Tags),
        Width = h.ImageWidth,
        Height = h.ImageHeight,
        Creator = Creator(h.User, h.UserId),
        ThumbSrc = h.PreviewUrl,
        PreviewSrc = h.WebformatUrl,
        PreviewMime = "image/jpeg",
        License = License,
    };

    private static ProviderItem ToItem(PixabayVideoHit h)
    {
        var renditions = Renditions(h);
        var item = new ProviderItem
        {
            Native = h.Id.ToString(CultureInfo.InvariantCulture),
            Type = StockTypeIds.Video,
            Title = Title(h.Tags),
            DurationMs = h.Duration * 1000,
            Creator = Creator(h.User, h.UserId),
            License = License,
            Variants = [.. renditions.Select(r => new ProviderVariant(r.Name, r.Width > 0 && r.Height > 0 ? $"{r.Width}x{r.Height}" : r.Name, r.Width, r.Height, "video/mp4", r.Size))],
        };
        if (renditions.Count == 0) return item;

        var best = renditions[0];
        var smallest = renditions[^1];
        return item with
        {
            Width = best.Width,
            Height = best.Height,
            ThumbSrc = string.IsNullOrEmpty(best.Thumbnail) ? smallest.Thumbnail : best.Thumbnail,
            PreviewSrc = smallest.Url,
            PreviewMime = "video/mp4",
        };
    }

    private static string Best(PixabayImageHit h) =>
        new[] { h.FullHdUrl, h.LargeImageUrl, h.WebformatUrl, h.PreviewUrl }.FirstOrDefault(u => !string.IsNullOrEmpty(u)) ?? "";

    private static List<NamedRendition> Renditions(PixabayVideoHit h)
    {
        var v = h.Videos;
        return [.. new[] { new NamedRendition("large", v.Large), new NamedRendition("medium", v.Medium), new NamedRendition("small", v.Small), new NamedRendition("tiny", v.Tiny) }
            .Where(r => !string.IsNullOrEmpty(r.Url)).OrderByDescending(r => r.Width)];
    }

    private static NamedRendition? Pick(PixabayVideoHit h, string? variantId)
    {
        var all = Renditions(h);
        if (all.Count == 0) return null;
        return string.IsNullOrEmpty(variantId) ? all[0] : all.FirstOrDefault(r => r.Name == variantId);
    }

    private static string Title(string tags)
    {
        var kept = tags.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).Take(3).ToList();
        if (kept.Count == 0) return "Untitled";
        var joined = string.Join(", ", kept);
        return char.ToUpperInvariant(joined[0]) + joined[1..];
    }

    private static ProviderCreator? Creator(string user, long id) =>
        string.IsNullOrEmpty(user) ? null : new ProviderCreator(user, $"https://pixabay.com/users/{user}-{id}/");

    private static string Attribution(string user) => string.IsNullOrEmpty(user) ? "Content from Pixabay" : user + " on Pixabay";

    private sealed record NamedRendition(string Name, PixabayRendition Source)
    {
        public string Url => Source.Url;
        public int Width => Source.Width;
        public int Height => Source.Height;
        public long Size => Source.Size;
        public string Thumbnail => Source.Thumbnail;
    }
}

internal sealed class PixabayImageResponse
{
    public List<PixabayImageHit> Hits { get; set; } = [];
}

internal sealed class PixabayImageHit
{
    public long Id { get; set; }
    public string Tags { get; set; } = "";
    [JsonPropertyName("previewURL")] public string PreviewUrl { get; set; } = "";
    [JsonPropertyName("webformatURL")] public string WebformatUrl { get; set; } = "";
    [JsonPropertyName("largeImageURL")] public string LargeImageUrl { get; set; } = "";
    [JsonPropertyName("fullHDURL")] public string FullHdUrl { get; set; } = "";
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public string User { get; set; } = "";
    [JsonPropertyName("user_id")] public long UserId { get; set; }
}

internal sealed class PixabayVideoResponse
{
    public List<PixabayVideoHit> Hits { get; set; } = [];
}

internal sealed class PixabayVideoHit
{
    public long Id { get; set; }
    public string Tags { get; set; } = "";
    public int Duration { get; set; }
    public PixabayVideos Videos { get; set; } = new();
    public string User { get; set; } = "";
    [JsonPropertyName("user_id")] public long UserId { get; set; }
}

internal sealed class PixabayVideos
{
    public PixabayRendition Large { get; set; } = new();
    public PixabayRendition Medium { get; set; } = new();
    public PixabayRendition Small { get; set; } = new();
    public PixabayRendition Tiny { get; set; } = new();
}

internal sealed class PixabayRendition
{
    public string Url { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public long Size { get; set; }
    public string Thumbnail { get; set; } = "";
}
