using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CutWire.Studio.Application.Stock;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Infrastructure.Stock.Providers;

public sealed class FreetouseOptions
{
    public const string Section = "Stock:Providers:Freetouse";

    public bool Enabled { get; set; } = true;
    public string Api { get; set; } = "https://api.freetouse.com/v3";
}

public static class FreetouseRegistration
{
    public static void Register(IServiceCollection services, IConfiguration config)
    {
        var section = config.GetSection(FreetouseOptions.Section);
        if (section.Get<FreetouseOptions>() is { Enabled: false }) return;

        services.AddOptions<FreetouseOptions>().Bind(section);
        services.AddHttpClient(nameof(FreetouseProvider));
        services.AddSingleton(sp => new FreetouseProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(FreetouseProvider)),
            sp.GetRequiredService<IProviderCache>(),
            sp.GetRequiredService<IOptions<StockOptions>>(),
            sp.GetRequiredService<IOptions<FreetouseOptions>>()));
        services.AddSingleton<IStockProvider>(sp => sp.GetRequiredService<FreetouseProvider>());
    }
}

public sealed class FreetouseProvider(HttpClient http, IProviderCache cache, IOptions<StockOptions> stock, IOptions<FreetouseOptions> options) : IStockProvider
{
    public const string Id = "freetouse";
    private const string LicenseName = "Free To Use";
    private const string LicenseUrl = "https://freetouse.com/license";

    private readonly RestClient _rest = new(http, cache, stock.Value.SearchCacheTtl);
    private readonly FreetouseOptions _o = options.Value;

    public ProviderDescriptor Descriptor { get; } = new(
        Id, "Free To Use", "Royalty-free background music for creators.", "Music from freetouse.com",
        [new StockService(StockTypeIds.Music)],
        ProviderCapabilities.Search | ProviderCapabilities.Fulfill);

    public async Task<ProviderPage> SearchAsync(ProviderQuery query, CancellationToken ct)
    {
        var limit = ProviderSupport.ClampLimit(query.Limit, 50);
        var offset = int.TryParse(query.Cursor, out var n) && n > 0 ? n : 0;
        var featured = string.IsNullOrEmpty(query.Q);

        var p = new List<KeyValuePair<string, string?>>
        {
            new("limit", limit.ToString(CultureInfo.InvariantCulture)),
            new("offset", offset.ToString(CultureInfo.InvariantCulture)),
        };
        if (!featured) p.Add(new("query", query.Q));

        var res = await _rest.GetJsonAsync<FreetouseList>(RestClient.Url(_o.Api + (featured ? "/music/tracks/all" : "/music/tracks/search"), p), ct);

        // The search endpoint ignores limit and returns every match, so the page size is enforced here.
        var items = res.Data.Where(t => !t.IsPremium).Take(limit).Select(ToItem).ToList();
        var next = offset + limit < res.Pagination.Count ? (offset + limit).ToString(CultureInfo.InvariantCulture) : null;
        return new ProviderPage(items, next);
    }

    public async Task<ProviderItem?> GetItemAsync(string type, string native, CancellationToken ct)
    {
        var track = await TrackAsync(native, ct);
        return string.IsNullOrEmpty(track.Id) ? throw new StockException(StockReason.ItemMissing, "empty track") : ToItem(track);
    }

    public async Task<ProviderAsset> FetchAsync(FulfillRequest request, StockWorkspace workspace, CancellationToken ct)
    {
        var track = await TrackAsync(request.Native, ct);
        if (string.IsNullOrEmpty(track.Files.Mp3) || track.IsPremium) throw new StockException(StockReason.ItemMissing, "no free mp3");

        var filename = ProviderSupport.FileName(track.Title, "mp3");
        var path = await ProviderSupport.DownloadAsync(workspace, track.Files.Mp3, filename, ct);
        return new ProviderAsset(path, "audio/mpeg", filename, ToLicense(track), Attribution(track), TimeSpan.FromSeconds(track.Duration));
    }

    private async Task<FreetouseTrack> TrackAsync(string id, CancellationToken ct) =>
        (await _rest.GetJsonAsync<FreetouseTrackResponse>($"{_o.Api}/music/tracks/{Uri.EscapeDataString(id)}", ct)).Data;

    private static ProviderItem ToItem(FreetouseTrack t) => new()
    {
        Native = t.Id,
        Type = StockTypeIds.Music,
        Title = t.Title,
        DurationMs = (int)(t.Duration * 1000),
        ThumbSrc = t.Thumbnails.Md,
        License = ToLicense(t),
        Creator = t.Artists.FirstOrDefault() is { Length: > 0 } artist ? new ProviderCreator(artist) : null,
        PreviewSrc = t.Files.Mp3,
        PreviewMime = string.IsNullOrEmpty(t.Files.Mp3) ? "" : "audio/mpeg",
    };

    private static ProviderLicense ToLicense(FreetouseTrack t) => new(LicenseName, LicenseUrl, Attribution(t));

    private static string Attribution(FreetouseTrack t) =>
        t.Artists.FirstOrDefault() is { Length: > 0 } artist ? $"{t.Title} by {artist} (freetouse.com)" : $"{t.Title} (freetouse.com)";
}

internal sealed class FreetouseList
{
    public List<FreetouseTrack> Data { get; set; } = [];
    public FreetousePagination Pagination { get; set; } = new();
}

internal sealed class FreetousePagination
{
    public int Count { get; set; }
}

internal sealed class FreetouseTrackResponse
{
    public FreetouseTrack Data { get; set; } = new();
}

internal sealed class FreetouseTrack
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public double Duration { get; set; }
    [JsonPropertyName("is_premium")] public bool IsPremium { get; set; }
    [JsonConverter(typeof(FreetouseNamesConverter))] public List<string> Artists { get; set; } = [];
    public FreetouseThumbnails Thumbnails { get; set; } = new();
    public FreetouseFiles Files { get; set; } = new();
}

internal sealed class FreetouseThumbnails
{
    public string Md { get; set; } = "";
}

internal sealed class FreetouseFiles
{
    public string Mp3 { get; set; } = "";
}

/// <summary>freetouse returns artists as [index, {id, name}] pairs; a plain array of objects is tolerated in case the shape is normalised.</summary>
internal sealed class FreetouseNamesConverter : JsonConverter<List<string>>
{
    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var names = new List<string>();
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return names;
        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            var obj = entry.ValueKind == JsonValueKind.Array ? (entry.GetArrayLength() > 1 ? entry[1] : default) : entry;
            if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                names.Add(name.GetString() ?? "");
        }

        return names;
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options) => throw new NotSupportedException();
}
