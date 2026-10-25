using System.Globalization;
using System.Text.Json.Serialization;
using CutWire.Studio.Application.Stock;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Infrastructure.Stock.Providers;

public sealed class UnsplashOptions
{
    public const string Section = "Stock:Providers:Unsplash";

    public string AccessKey { get; set; } = "";
    public string Api { get; set; } = "https://api.unsplash.com";
}

public static class UnsplashRegistration
{
    public static void Register(IServiceCollection services, IConfiguration config)
    {
        var section = config.GetSection(UnsplashOptions.Section);
        if (string.IsNullOrWhiteSpace(section.Get<UnsplashOptions>()?.AccessKey)) return;

        services.AddOptions<UnsplashOptions>().Bind(section);
        services.AddHttpClient(nameof(UnsplashProvider), (sp, c) =>
        {
            c.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Client-ID " + sp.GetRequiredService<IOptions<UnsplashOptions>>().Value.AccessKey);
            c.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Version", "v1");
        });
        services.AddSingleton(sp => new UnsplashProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(UnsplashProvider)),
            sp.GetRequiredService<IProviderCache>(),
            sp.GetRequiredService<IOptions<StockOptions>>(),
            sp.GetRequiredService<IOptions<UnsplashOptions>>()));
        services.AddSingleton<IStockProvider>(sp => sp.GetRequiredService<UnsplashProvider>());
    }
}

public sealed class UnsplashProvider(HttpClient http, IProviderCache cache, IOptions<StockOptions> stock, IOptions<UnsplashOptions> options) : IStockProvider
{
    public const string Id = "unsplash";
    private const string LicenseName = "Unsplash License";
    private const string LicenseUrl = "https://unsplash.com/license";

    private static readonly FilterDef Orientation = new("orientation", "Orientation", FilterKinds.Select,
        [new("landscape", "Landscape"), new("portrait", "Portrait"), new("squarish", "Square")]);

    private static readonly FilterDef Color = new("color", "Colour", FilterKinds.Select,
    [
        new("black_and_white", "Black and white"), new("black", "Black"), new("white", "White"), new("yellow", "Yellow"),
        new("orange", "Orange"), new("red", "Red"), new("purple", "Purple"), new("magenta", "Magenta"),
        new("green", "Green"), new("teal", "Teal"), new("blue", "Blue"),
    ]);

    private readonly RestClient _rest = new(http, cache, stock.Value.SearchCacheTtl);
    private readonly UnsplashOptions _o = options.Value;

    public ProviderDescriptor Descriptor { get; } = new(
        Id, "Unsplash", "Photography from the Unsplash community.", "Photos from Unsplash",
        [new StockService(StockTypeIds.Photo, [Orientation, Color])],
        ProviderCapabilities.Search | ProviderCapabilities.Fulfill);

    public async Task<ProviderPage> SearchAsync(ProviderQuery query, CancellationToken ct)
    {
        var page = ProviderSupport.Page(query.Cursor);
        var limit = ProviderSupport.ClampLimit(query.Limit, 30);
        var p = new List<KeyValuePair<string, string?>>
        {
            new("per_page", limit.ToString(CultureInfo.InvariantCulture)),
            new("page", page.ToString(CultureInfo.InvariantCulture)),
            new("orientation", query.Filter("orientation")),
            new("color", query.Filter("color")),
        };

        if (string.IsNullOrEmpty(query.Q))
        {
            var photos = await _rest.GetJsonAsync<List<UnsplashPhoto>>(RestClient.Url(_o.Api + "/photos", p), ct);
            return Page(photos, page, limit);
        }

        p.Add(new("query", query.Q));
        var res = await _rest.GetJsonAsync<UnsplashSearchResponse>(RestClient.Url(_o.Api + "/search/photos", p), ct);
        var result = Page(res.Results, page, limit);
        return page >= res.TotalPages ? result with { NextCursor = null } : result;
    }

    public async Task<ProviderItem?> GetItemAsync(string type, string native, CancellationToken ct) => ToItem(await GetPhotoAsync(native, ct));

    public async Task<ProviderAsset> FetchAsync(FulfillRequest request, StockWorkspace workspace, CancellationToken ct)
    {
        var photo = await GetPhotoAsync(request.Native, ct);

        // Required by the Unsplash API terms: count the download before taking the file. Best effort.
        if (!string.IsNullOrEmpty(photo.Links.DownloadLocation))
        {
            try
            {
                await _rest.GetStringAsync(photo.Links.DownloadLocation, ct, useCache: false);
            }
            catch (StockException)
            {
            }
        }

        var filename = ProviderSupport.FileName(Title(photo), "jpg");
        var path = await ProviderSupport.DownloadAsync(workspace, Best(photo), filename, ct);
        return new ProviderAsset(path, "image/jpeg", filename, ToLicense(photo), Attribution(photo));
    }

    private Task<UnsplashPhoto> GetPhotoAsync(string id, CancellationToken ct) =>
        _rest.GetJsonAsync<UnsplashPhoto>($"{_o.Api}/photos/{Uri.EscapeDataString(id)}", ct);

    private static ProviderPage Page(List<UnsplashPhoto> photos, int page, int limit)
    {
        var items = photos.Select(ToItem).ToList();
        return new ProviderPage(items, ProviderSupport.NextCursor(page, items.Count, limit));
    }

    private static ProviderItem ToItem(UnsplashPhoto p) => new()
    {
        Native = p.Id,
        Type = StockTypeIds.Photo,
        Title = Title(p),
        Width = p.Width,
        Height = p.Height,
        Creator = Creator(p),
        ThumbSrc = p.Urls.Small,
        PreviewSrc = p.Urls.Regular,
        PreviewMime = "image/jpeg",
        License = ToLicense(p),
    };

    private static string Best(UnsplashPhoto p) =>
        new[] { p.Urls.Full, p.Urls.Raw, p.Urls.Regular }.FirstOrDefault(u => !string.IsNullOrEmpty(u)) ?? "";

    private static string Title(UnsplashPhoto p)
    {
        foreach (var c in new[] { p.Description, p.AltDescription })
            if (!string.IsNullOrWhiteSpace(c)) return c.Trim();
        return string.IsNullOrEmpty(p.User.Name) ? "Untitled" : "Photo by " + p.User.Name;
    }

    private static ProviderCreator? Creator(UnsplashPhoto p)
    {
        if (string.IsNullOrEmpty(p.User.Name) && string.IsNullOrEmpty(p.User.Username)) return null;
        return new ProviderCreator(string.IsNullOrEmpty(p.User.Name) ? p.User.Username : p.User.Name, p.User.Links.Html);
    }

    private static ProviderLicense ToLicense(UnsplashPhoto p) => new(LicenseName, LicenseUrl, Attribution(p));

    private static string Attribution(UnsplashPhoto p)
    {
        var name = string.IsNullOrEmpty(p.User.Name) ? p.User.Username : p.User.Name;
        return string.IsNullOrEmpty(name) ? "Photo on Unsplash" : $"Photo by {name} on Unsplash";
    }
}

internal sealed class UnsplashSearchResponse
{
    [JsonPropertyName("total_pages")] public int TotalPages { get; set; }
    public List<UnsplashPhoto> Results { get; set; } = [];
}

internal sealed class UnsplashPhoto
{
    public string Id { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public string? Description { get; set; }
    [JsonPropertyName("alt_description")] public string? AltDescription { get; set; }
    public UnsplashUrls Urls { get; set; } = new();
    public UnsplashLinks Links { get; set; } = new();
    public UnsplashUser User { get; set; } = new();
}

internal sealed class UnsplashUrls
{
    public string Raw { get; set; } = "";
    public string Full { get; set; } = "";
    public string Regular { get; set; } = "";
    public string Small { get; set; } = "";
}

internal sealed class UnsplashLinks
{
    [JsonPropertyName("download_location")] public string DownloadLocation { get; set; } = "";
}

internal sealed class UnsplashUser
{
    public string Name { get; set; } = "";
    public string Username { get; set; } = "";
    public UnsplashUserLinks Links { get; set; } = new();
}

internal sealed class UnsplashUserLinks
{
    public string Html { get; set; } = "";
}
