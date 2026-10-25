namespace CutWire.Studio.Application.Stock;

public static class StockTypeIds
{
    public const string Video = "video";
    public const string Photo = "photo";
    public const string Audio = "audio";
    public const string Music = "music";
    public const string Greenscreen = "greenscreen";
}

public sealed record StockTypeDef(string Id, string Label, string MediaKind, string Delivery);

public static class StockTypes
{
    public static readonly IReadOnlyList<StockTypeDef> All =
    [
        new(StockTypeIds.Video, "Video", "video", "download"),
        new(StockTypeIds.Photo, "Photos", "image", "download"),
        new(StockTypeIds.Audio, "Audio", "audio", "download"),
        new(StockTypeIds.Music, "Music", "music", "download"),
        new(StockTypeIds.Greenscreen, "Green screen", "video", "download"),
    ];

    public static StockTypeDef? Find(string id) => All.FirstOrDefault(t => t.Id == id);
}

public static class FilterKinds
{
    public const string Select = "select";
    public const string Toggle = "toggle";
    public const string Range = "range";
}

public sealed record FilterOption(string Id, string Label);

/// <summary><paramref name="Kind"/> is one of <see cref="FilterKinds"/>. Toggles arrive as "1"/"true"; ranges as free text such as "10-60" (see <see cref="RangeFilter"/>).</summary>
public sealed record FilterDef(string Id, string Label, string Kind, IReadOnlyList<FilterOption>? Options = null);

/// <summary>Default cap seeded into provider_settings on first boot. Limit 0 means unlimited. Window is "day" or "hour"; Scope is "client" or "account".</summary>
public sealed record QuotaDefault(int Limit, string Window = "day", string Scope = "client");

public sealed record StockService(string Type, IReadOnlyList<FilterDef>? Filters = null, QuotaDefault? Quota = null)
{
    public IReadOnlyList<FilterDef> FilterList => Filters ?? [];
}

[Flags]
public enum ProviderCapabilities
{
    None = 0,
    Search = 1,
    Resolve = 2,
    Fulfill = 4,
}

public sealed record ProviderDescriptor(
    string Id,
    string Label,
    string Description,
    string Attribution,
    IReadOnlyList<StockService> Serves,
    ProviderCapabilities Capabilities)
{
    public StockService? Serving(string type) => Serves.FirstOrDefault(s => s.Type == type);
}

public sealed record ProviderQuery(string Type, string? Q, int Limit, string? Cursor, IReadOnlyDictionary<string, string> Filters)
{
    public string Filter(string id) => Filters.TryGetValue(id, out var v) ? v : "";
}

public sealed record ProviderCreator(string Name, string? Url = null);

public sealed record ProviderLicense(string Name, string? Url = null, string? Attribution = null);

public sealed record ProviderVariant(string Id, string Label, int Width = 0, int Height = 0, string Mime = "", long Size = 0);

/// <summary>
/// A provider-native listing. Ids are not opaque yet and URLs are raw origins: the assembler encodes the id and swaps every URL for a
/// sealed one before anything reaches a client. Zero ints mean "unknown".
/// </summary>
public sealed record ProviderItem
{
    public required string Native { get; init; }
    public string Type { get; init; } = "";
    public string Title { get; init; } = "";
    public ProviderCreator? Creator { get; init; }
    public int DurationMs { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string ThumbSrc { get; init; } = "";
    public string PreviewSrc { get; init; } = "";
    /// <summary>"image/..." previews go through imgproxy; video and audio previews stream through the relay.</summary>
    public string PreviewMime { get; init; } = "";
    public ProviderLicense? License { get; init; }
    public IReadOnlyList<ProviderVariant> Variants { get; init; } = [];
}

public sealed record ProviderPage(IReadOnlyList<ProviderItem> Items, string? NextCursor = null)
{
    public static readonly ProviderPage Empty = new([]);
}

public sealed record FulfillRequest(string Type, string Native, string? VariantId);

/// <summary>Scratch space a download job owns for the length of its run.</summary>
public sealed class StockWorkspace(string dir, HttpClient http, long maxBytes, Action<double>? progress = null)
{
    public string Dir { get; } = dir;
    public HttpClient Http { get; } = http;
    public long MaxBytes { get; } = maxBytes;
    /// <summary>Reports 0..1. Providers that cannot estimate may never call it.</summary>
    public void Report(double fraction) => progress?.Invoke(Math.Clamp(fraction, 0, 1));
}

/// <summary>A file on disk plus what the client needs to know about it.</summary>
public sealed record ProviderAsset(string Path, string Mime, string Filename, ProviderLicense? License = null, string? Attribution = null, TimeSpan? Duration = null);

/// <summary>
/// One source of stock media. A provider implements only what it can honour (see <see cref="ProviderDescriptor.Capabilities"/>);
/// the defaults below are never reached for a capability the descriptor does not claim.
/// </summary>
public interface IStockProvider
{
    ProviderDescriptor Descriptor { get; }

    /// <summary>Browse/search. An empty <see cref="ProviderQuery.Q"/> returns a curated "featured" list.</summary>
    Task<ProviderPage> SearchAsync(ProviderQuery query, CancellationToken ct) => throw new NotSupportedException();

    /// <summary>Fetch one listing by its provider-native id; null when the provider cannot reconstruct it.</summary>
    Task<ProviderItem?> GetItemAsync(string type, string native, CancellationToken ct) => Task.FromResult<ProviderItem?>(null);

    /// <summary>Whether this provider handles a pasted URL. Cheap, no network.</summary>
    bool OwnsUrl(string url) => false;

    Task<ProviderItem> ResolveAsync(string url, CancellationToken ct) => throw new NotSupportedException();

    /// <summary>The download source: writes the file into <paramref name="workspace"/> and describes it. Normalisation happens afterwards.</summary>
    Task<ProviderAsset> FetchAsync(FulfillRequest request, StockWorkspace workspace, CancellationToken ct) => throw new NotSupportedException();
}

/// <summary>A resolve-capable provider that is compiled in but switched off (no yt-dlp binary, ...). Lets /resolve explain why instead of "unsupported link".</summary>
public sealed record UnavailableProvider(string Id, string Reason, Func<string, bool> OwnsUrl);
