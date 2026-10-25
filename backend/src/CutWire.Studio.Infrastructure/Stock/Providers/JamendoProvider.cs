using System.Globalization;
using System.Text.Json.Serialization;
using CutWire.Studio.Application.Stock;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Infrastructure.Stock.Providers;

public sealed class JamendoOptions
{
    public const string Section = "Stock:Providers:Jamendo";

    public string ClientId { get; set; } = "";
    public string Api { get; set; } = "https://api.jamendo.com/v3.0";
}

public static class JamendoRegistration
{
    public static void Register(IServiceCollection services, IConfiguration config)
    {
        var section = config.GetSection(JamendoOptions.Section);
        if (string.IsNullOrWhiteSpace(section.Get<JamendoOptions>()?.ClientId)) return;

        services.AddOptions<JamendoOptions>().Bind(section);
        services.AddHttpClient(nameof(JamendoProvider));
        services.AddSingleton(sp => new JamendoProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(JamendoProvider)),
            sp.GetRequiredService<IProviderCache>(),
            sp.GetRequiredService<IOptions<StockOptions>>(),
            sp.GetRequiredService<IOptions<JamendoOptions>>()));
        services.AddSingleton<IStockProvider>(sp => sp.GetRequiredService<JamendoProvider>());
    }
}

public sealed class JamendoProvider(HttpClient http, IProviderCache cache, IOptions<StockOptions> stock, IOptions<JamendoOptions> options) : IStockProvider
{
    public const string Id = "jamendo";
    private const string AudioFormat = "mp32";
    private const int MaxDurationSeconds = 3600;

    private static readonly FilterDef Order = new("order", "Sort by", FilterKinds.Select,
        [new("relevance", "Relevance"), new("popularity_total", "Most popular"), new("releasedate_desc", "Newest")]);

    private static readonly FilterDef Tags = new("tags", "Tags", FilterKinds.Select,
        [.. new[] { "pop", "rock", "electronic", "hiphop", "jazz", "indie", "classical", "ambient", "chillout", "folk", "metal", "reggae", "blues", "country", "house", "latin", "rnb", "punk", "filmscore" }
            .Select(t => new FilterOption(t, char.ToUpperInvariant(t[0]) + t[1..]))]);

    private static readonly FilterDef Duration = new("duration", "Duration (seconds)", FilterKinds.Range);

    private readonly RestClient _rest = new(http, cache, stock.Value.SearchCacheTtl);
    private readonly JamendoOptions _o = options.Value;

    public ProviderDescriptor Descriptor { get; } = new(
        Id, "Jamendo", "Creative Commons music from independent artists.", "Music from Jamendo",
        [new StockService(StockTypeIds.Music, [Order, Tags, Duration])],
        ProviderCapabilities.Search | ProviderCapabilities.Fulfill);

    public async Task<ProviderPage> SearchAsync(ProviderQuery query, CancellationToken ct)
    {
        var limit = ProviderSupport.ClampLimit(query.Limit, 50);
        var offset = int.TryParse(query.Cursor, out var n) && n > 0 ? n : 0;

        var order = query.Filter("order");
        if (order.Length == 0 && string.IsNullOrEmpty(query.Q)) order = "popularity_total";

        var p = Params();
        p.Add(new("limit", limit.ToString(CultureInfo.InvariantCulture)));
        p.Add(new("offset", offset.ToString(CultureInfo.InvariantCulture)));
        p.Add(new("include", "musicinfo licenses"));
        p.Add(new("audioformat", AudioFormat));
        p.Add(new("search", query.Q));
        p.Add(new("order", order));
        p.Add(new("tags", query.Filter("tags")));

        var range = RangeFilter.Parse(query.Filter("duration"));
        if (!range.IsEmpty)
        {
            var min = (int)(range.Min ?? 0);
            var max = (int)(range.Max ?? MaxDurationSeconds);
            p.Add(new("durationbetween", $"{min}_{max}"));
        }

        var res = await _rest.GetJsonAsync<JamendoResponse>(RestClient.Url(_o.Api + "/tracks/", p), ct);
        Check(res);

        // Tracks whose artist has not permitted downloads would only produce a failing job later.
        var items = res.Results.Where(t => t.AudioDownloadAllowed).Select(ToItem).ToList();
        return new ProviderPage(items, res.Results.Count == limit ? (offset + limit).ToString(CultureInfo.InvariantCulture) : null);
    }

    public async Task<ProviderItem?> GetItemAsync(string type, string native, CancellationToken ct) => ToItem(await TrackAsync(native, ct));

    public async Task<ProviderAsset> FetchAsync(FulfillRequest request, StockWorkspace workspace, CancellationToken ct)
    {
        var t = await TrackAsync(request.Native, ct);
        var source = string.IsNullOrEmpty(t.AudioDownload) || !t.AudioDownloadAllowed ? t.Audio : t.AudioDownload;
        if (string.IsNullOrEmpty(source)) throw new StockException(StockReason.ItemMissing, "track has no audio source");

        var filename = ProviderSupport.FileName(t.Name, "mp3");
        var path = await ProviderSupport.DownloadAsync(workspace, source, filename, ct);
        return new ProviderAsset(path, "audio/mpeg", filename, ToLicense(t), Attribution(t), TimeSpan.FromSeconds(t.Duration));
    }

    private List<KeyValuePair<string, string?>> Params() => [new("client_id", _o.ClientId), new("format", "json")];

    private async Task<JamendoTrack> TrackAsync(string id, CancellationToken ct)
    {
        var p = Params();
        p.Add(new("id", id));
        p.Add(new("include", "musicinfo licenses"));
        p.Add(new("audioformat", AudioFormat));
        var res = await _rest.GetJsonAsync<JamendoResponse>(RestClient.Url(_o.Api + "/tracks/", p), ct);
        Check(res);
        return res.Results.FirstOrDefault() ?? throw new StockException(StockReason.ItemMissing, "no such track");
    }

    // Jamendo answers 200 even for failures; code 5 (bad client id) and 11 (suspended app) are our configuration problem.
    private static void Check(JamendoResponse r)
    {
        if (r.Headers.Status is "success" or "") return;
        throw new StockException(StockReason.SourceError, "jamendo: " + r.Headers.ErrorMessage);
    }

    private static ProviderItem ToItem(JamendoTrack t) => new()
    {
        Native = t.Id,
        Type = StockTypeIds.Music,
        Title = t.Name,
        DurationMs = t.Duration * 1000,
        ThumbSrc = string.IsNullOrEmpty(t.Image) ? t.AlbumImage : t.Image,
        License = ToLicense(t),
        Creator = string.IsNullOrEmpty(t.ArtistName) ? null : new ProviderCreator(t.ArtistName, "https://www.jamendo.com/artist/" + t.ArtistId),
        PreviewSrc = t.Audio,
        PreviewMime = string.IsNullOrEmpty(t.Audio) ? "" : "audio/mpeg",
    };

    private static ProviderLicense ToLicense(JamendoTrack t) =>
        new(string.IsNullOrEmpty(t.LicenseCcUrl) ? "Jamendo" : "Creative Commons", t.LicenseCcUrl, Attribution(t));

    private static string Attribution(JamendoTrack t) =>
        string.IsNullOrEmpty(t.ArtistName) ? $"{t.Name} (Jamendo)" : $"{t.Name} by {t.ArtistName} (Jamendo)";
}

internal sealed class JamendoResponse
{
    public JamendoHeaders Headers { get; set; } = new();
    public List<JamendoTrack> Results { get; set; } = [];
}

internal sealed class JamendoHeaders
{
    public string Status { get; set; } = "";
    [JsonPropertyName("error_message")] public string ErrorMessage { get; set; } = "";
}

internal sealed class JamendoTrack
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Duration { get; set; }
    [JsonPropertyName("artist_name")] public string ArtistName { get; set; } = "";
    [JsonPropertyName("artist_id")] public string ArtistId { get; set; } = "";
    [JsonPropertyName("album_image")] public string AlbumImage { get; set; } = "";
    public string Image { get; set; } = "";
    [JsonPropertyName("license_ccurl")] public string LicenseCcUrl { get; set; } = "";
    public string Audio { get; set; } = "";
    [JsonPropertyName("audiodownload")] public string AudioDownload { get; set; } = "";
    [JsonPropertyName("audiodownload_allowed")] public bool AudioDownloadAllowed { get; set; }
}
