using System.Globalization;
using System.Text.Json.Serialization;
using CutWire.Studio.Application.Stock;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Infrastructure.Stock.Providers;

public sealed class PexelsOptions
{
    public const string Section = "Stock:Providers:Pexels";

    public string ApiKey { get; set; } = "";
    public string PhotoApi { get; set; } = "https://api.pexels.com/v1";
    public string VideoApi { get; set; } = "https://api.pexels.com/videos";
}

public static class PexelsRegistration
{
    public static bool IsConfigured(IConfiguration config) => !string.IsNullOrWhiteSpace(config.GetSection(PexelsOptions.Section).Get<PexelsOptions>()?.ApiKey);

    public static void Register(IServiceCollection services, IConfiguration config)
    {
        var section = config.GetSection(PexelsOptions.Section);
        if (string.IsNullOrWhiteSpace(section.Get<PexelsOptions>()?.ApiKey)) return;

        services.AddOptions<PexelsOptions>().Bind(section);
        services.AddHttpClient(nameof(PexelsProvider), (sp, c) =>
            c.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", sp.GetRequiredService<IOptions<PexelsOptions>>().Value.ApiKey));
        services.AddSingleton(sp => new PexelsProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(PexelsProvider)),
            sp.GetRequiredService<IProviderCache>(),
            sp.GetRequiredService<IOptions<StockOptions>>(),
            sp.GetRequiredService<IOptions<PexelsOptions>>()));
        services.AddSingleton<IStockProvider>(sp => sp.GetRequiredService<PexelsProvider>());
    }
}

public sealed class PexelsProvider(HttpClient http, IProviderCache cache, IOptions<StockOptions> stock, IOptions<PexelsOptions> options) : IStockProvider
{
    public const string Id = "pexels";
    private const int MaxPerPage = 80;
    private const int PreviewLimit = 1280;
    private const string LicenseName = "Pexels License";
    private const string LicenseUrl = "https://www.pexels.com/license/";

    private static readonly ProviderLicense License = new(LicenseName, LicenseUrl);

    private static readonly FilterDef Orientation = new("orientation", "Orientation", FilterKinds.Select,
        [new("landscape", "Landscape"), new("portrait", "Portrait"), new("square", "Square")]);

    private static readonly FilterDef Size = new("size", "Minimum size", FilterKinds.Select,
        [new("large", "Large"), new("medium", "Medium"), new("small", "Small")]);

    private static readonly FilterDef Color = new("color", "Colour", FilterKinds.Select,
        [.. new[] { "red", "orange", "yellow", "green", "turquoise", "blue", "violet", "pink", "brown", "black", "gray", "white" }
            .Select(c => new FilterOption(c, char.ToUpperInvariant(c[0]) + c[1..]))]);

    private static readonly FilterDef Duration = new("duration", "Duration (seconds)", FilterKinds.Range);

    private readonly RestClient _rest = new(http, cache, stock.Value.SearchCacheTtl);
    private readonly PexelsOptions _o = options.Value;

    public ProviderDescriptor Descriptor { get; } = new(
        Id, "Pexels", "Free stock photos and video.", "Photos and video provided by Pexels",
        [
            new StockService(StockTypeIds.Photo, [Orientation, Size, Color]),
            new StockService(StockTypeIds.Video, [Orientation, Size, Duration]),
        ],
        ProviderCapabilities.Search | ProviderCapabilities.Fulfill);

    public async Task<ProviderPage> SearchAsync(ProviderQuery query, CancellationToken ct)
    {
        var photo = query.Type == StockTypeIds.Photo;
        var page = ProviderSupport.Page(query.Cursor);
        var limit = ProviderSupport.ClampLimit(query.Limit, MaxPerPage);
        var featured = string.IsNullOrEmpty(query.Q);

        var p = new List<KeyValuePair<string, string?>>
        {
            new("per_page", limit.ToString(CultureInfo.InvariantCulture)),
            new("page", page.ToString(CultureInfo.InvariantCulture)),
            new("orientation", query.Filter("orientation")),
            new("size", query.Filter("size")),
        };
        if (!featured) p.Add(new("query", query.Q));

        if (photo)
        {
            p.Add(new("color", query.Filter("color")));
            var url = RestClient.Url(_o.PhotoApi + (featured ? "/curated" : "/search"), p);
            var res = await _rest.GetJsonAsync<PexelsPhotoResponse>(url, ct);
            var items = res.Photos.Select(ToItem).ToList();
            return new ProviderPage(items, ProviderSupport.NextCursor(page, items.Count, limit));
        }
        else
        {
            var range = RangeFilter.Parse(query.Filter("duration"));
            if (range.Min is { } min) p.Add(new("min_duration", ((int)min).ToString(CultureInfo.InvariantCulture)));
            if (range.Max is { } max) p.Add(new("max_duration", ((int)max).ToString(CultureInfo.InvariantCulture)));
            var url = RestClient.Url(_o.VideoApi + (featured ? "/popular" : "/search"), p);
            var res = await _rest.GetJsonAsync<PexelsVideoResponse>(url, ct);
            var items = res.Videos.Select(ToItem).ToList();
            return new ProviderPage(items, ProviderSupport.NextCursor(page, items.Count, limit));
        }
    }

    public async Task<ProviderItem?> GetItemAsync(string type, string native, CancellationToken ct)
    {
        if (type == StockTypeIds.Photo) return ToItem(await GetPhotoAsync(native, ct));
        return ToItem(await GetVideoAsync(native, ct));
    }

    public async Task<ProviderAsset> FetchAsync(FulfillRequest request, StockWorkspace workspace, CancellationToken ct)
    {
        if (request.Type == StockTypeIds.Photo)
        {
            var photo = await GetPhotoAsync(request.Native, ct);
            var item = ToItem(photo);
            var name = ProviderSupport.FileName(item.Title, "jpg");
            var path = await ProviderSupport.DownloadAsync(workspace, photo.Src.Original, name, ct);
            return new ProviderAsset(path, "image/jpeg", name, License, Credit(item));
        }

        var video = await GetVideoAsync(request.Native, ct);
        var file = Pick(video, request.VariantId) ?? throw new StockException(StockReason.ItemMissing, "no matching video file");
        var title = ToItem(video).Title;
        var filename = ProviderSupport.FileName(title, "mp4");
        var saved = await ProviderSupport.DownloadAsync(workspace, file.Link, filename, ct);
        return new ProviderAsset(saved, "video/mp4", filename, License, Credit(ToItem(video)), TimeSpan.FromSeconds(video.Duration));
    }

    private Task<PexelsPhoto> GetPhotoAsync(string id, CancellationToken ct) =>
        _rest.GetJsonAsync<PexelsPhoto>($"{_o.PhotoApi}/photos/{Uri.EscapeDataString(id)}", ct);

    private Task<PexelsVideo> GetVideoAsync(string id, CancellationToken ct) =>
        _rest.GetJsonAsync<PexelsVideo>($"{_o.VideoApi}/videos/{Uri.EscapeDataString(id)}", ct);

    private static string Credit(ProviderItem item) =>
        string.IsNullOrEmpty(item.Creator?.Name) ? "Provided by Pexels" : item.Creator.Name + " on Pexels";

    private static ProviderItem ToItem(PexelsPhoto p) => new()
    {
        Native = p.Id.ToString(CultureInfo.InvariantCulture),
        Type = StockTypeIds.Photo,
        Title = string.IsNullOrEmpty(p.Alt) ? TitleFromSlug(p.Url, "Photo") : p.Alt,
        Width = p.Width,
        Height = p.Height,
        Creator = new ProviderCreator(p.Photographer, p.PhotographerUrl),
        ThumbSrc = p.Src.Large,
        PreviewSrc = p.Src.Large2x,
        PreviewMime = "image/jpeg",
        License = License,
    };

    private static ProviderItem ToItem(PexelsVideo v)
    {
        var files = Mp4s(v);
        var item = new ProviderItem
        {
            Native = v.Id.ToString(CultureInfo.InvariantCulture),
            Type = StockTypeIds.Video,
            Title = TitleFromSlug(v.Url, "Video"),
            Width = v.Width,
            Height = v.Height,
            DurationMs = v.Duration * 1000,
            Creator = new ProviderCreator(v.User.Name, v.User.Url),
            ThumbSrc = v.Image,
            License = License,
            Variants = [.. files.Select(f => new ProviderVariant(f.Id.ToString(CultureInfo.InvariantCulture), f.Width > 0 && f.Height > 0 ? $"{f.Width}x{f.Height}" : f.Quality, f.Width, f.Height, "video/mp4", f.Size))],
        };
        // The smallest rendition is the preview so a grid does not pull 4K files through the relay.
        if (files.Count > 0 && files[^1].Width <= PreviewLimit)
            item = item with { PreviewSrc = files[^1].Link, PreviewMime = "video/mp4" };
        return item;
    }

    private static List<PexelsVideoFile> Mp4s(PexelsVideo v) =>
        [.. v.VideoFiles.Where(f => f.FileType == "video/mp4" && !string.IsNullOrEmpty(f.Link)).OrderByDescending(f => f.Width)];

    private static PexelsVideoFile? Pick(PexelsVideo v, string? variantId)
    {
        var files = Mp4s(v);
        if (files.Count == 0) return null;
        if (!string.IsNullOrEmpty(variantId))
            return files.FirstOrDefault(f => f.Id.ToString(CultureInfo.InvariantCulture) == variantId);
        return files[0];
    }

    private static string TitleFromSlug(string? pageUrl, string fallback)
    {
        var trimmed = (pageUrl ?? "").Trim('/');
        var slug = trimmed[(trimmed.LastIndexOf('/') + 1)..];
        var words = slug.Split('-').ToList();
        if (words.Count > 1 && int.TryParse(words[^1], out _)) words.RemoveAt(words.Count - 1);
        var title = string.Join(' ', words).Trim();
        return title.Length == 0 ? fallback : char.ToUpperInvariant(title[0]) + title[1..];
    }
}

internal sealed class PexelsPhotoResponse
{
    public List<PexelsPhoto> Photos { get; set; } = [];
}

internal sealed class PexelsVideoResponse
{
    public List<PexelsVideo> Videos { get; set; } = [];
}

internal sealed class PexelsPhoto
{
    public long Id { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Url { get; set; } = "";
    public string Photographer { get; set; } = "";
    [JsonPropertyName("photographer_url")] public string PhotographerUrl { get; set; } = "";
    public string Alt { get; set; } = "";
    public PexelsPhotoSrc Src { get; set; } = new();
}

internal sealed class PexelsPhotoSrc
{
    public string Original { get; set; } = "";
    public string Large2x { get; set; } = "";
    public string Large { get; set; } = "";
}

internal sealed class PexelsVideo
{
    public long Id { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Duration { get; set; }
    public string Url { get; set; } = "";
    public string Image { get; set; } = "";
    public PexelsUser User { get; set; } = new();
    [JsonPropertyName("video_files")] public List<PexelsVideoFile> VideoFiles { get; set; } = [];
}

internal sealed class PexelsUser
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
}

internal sealed class PexelsVideoFile
{
    public long Id { get; set; }
    public string Quality { get; set; } = "";
    [JsonPropertyName("file_type")] public string FileType { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public string Link { get; set; } = "";
    public long Size { get; set; }
}
