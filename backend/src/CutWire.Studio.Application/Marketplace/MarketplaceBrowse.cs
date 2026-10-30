using System.Text;
using System.Text.Json;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Catalog;
using CutWire.Studio.Application.Stock;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Application.Marketplace;

/// <summary><see cref="Distribution"/> is already normalised; <see cref="BaseUrl"/> turns the relative byte routes into absolute URLs.</summary>
public sealed record MarketCaller(string Distribution, SemVersion? Version, StockCaller Stock, string BaseUrl, string? InstallId, string IpHash)
{
    public string Abs(string url) => url.StartsWith('/') ? BaseUrl + url : url;

    public string? AbsOrNull(string? url) => string.IsNullOrEmpty(url) ? null : Abs(url);
}

public sealed record MarketQuery(
    string? Category, string? Q, string? Cursor, int? Limit, string? Sort, string? Period, string? Provider, IReadOnlyDictionary<string, string> Filters);

public static class MarketErrors
{
    public static readonly Error UnknownSource = Error.NotFound("unknown_source", "That source is not available.");
    public static readonly Error ItemNotFound = Error.NotFound("item_not_found", "That item is not available.");

    public static Error InvalidCursor => Error.Validation("invalid_cursor", "The cursor is invalid.",
        new Dictionary<string, string[]> { ["cursor"] = ["The cursor is invalid."] });
}

public static class MarketMime
{
    public static string For(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".json" => "application/json",
        ".glb" => "model/gltf-binary",
        ".gltf" => "model/gltf+json",
        ".webp" => "image/webp",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".flac" => "audio/flac",
        ".opus" or ".ogg" => "audio/ogg",
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".zip" => "application/zip",
        ".cube" => "text/plain",
        _ => "application/octet-stream",
    };
}

static class MarketCursor
{
    public static string Encode(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string? Decode(string cursor)
    {
        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            return Encoding.UTF8.GetString(Convert.FromBase64String(padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=')));
        }
        catch (Exception e) when (e is FormatException or ArgumentException)
        {
            return null;
        }
    }

    public static int? DecodeOffset(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor)) return 0;
        return Decode(cursor) is { } text && int.TryParse(text, out var offset) && offset >= 0 ? offset : null;
    }

    public static bool Matches(string? query, params string?[] fields)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var haystack = string.Join(' ', fields.Where(f => !string.IsNullOrEmpty(f))).ToLowerInvariant();
        return query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).All(haystack.Contains);
    }
}

/// <summary>Lists and reads items per marketplace source, mapping every kind of source onto <see cref="MarketItemDto"/>.</summary>
public sealed class MarketplaceBrowse(
    MarketplaceManifest manifest,
    DriftAssetsCatalog driftAssets,
    SfxCatalog sfx,
    ISfxAudio sfxAudio,
    StockBrowse stock,
    StockSettings stockSettings,
    StockAssembler assembler,
    StockResolve resolve,
    IQueryHandler<ListItemsQuery, ItemPageDto> list,
    IQueryHandler<GetItemQuery, ItemDetailDto> detail,
    IAppDbContext db)
{
    public const int DefaultLimit = 24;
    public const int MaxLimit = 50;
    private const string StockPrefix = "stock.";
    private const string CommunityPrefix = "community.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly StockLicenseDto SfxLicense = new("Drift sound effects", null, null);

    private static JsonElement Extra(object value) => JsonSerializer.SerializeToElement(value, Json);

    public async Task<Result<MarketItemPageDto>> ListAsync(MarketCaller c, string source, MarketQuery q, CancellationToken ct)
    {
        var (_, sources) = await manifest.TargetAsync(c.Distribution, c.Version, ct);
        if (!sources.TryGetValue(source, out var def)) return MarketErrors.UnknownSource;
        var limit = Math.Clamp(q.Limit ?? DefaultLimit, 1, MaxLimit);
        return source switch
        {
            "drift-assets" => await DriftPageAsync(c, q, limit, ct),
            "sfx" => await SfxPageAsync(c, q, limit, ct),
            _ when def.Kind == "stock" => await StockPageAsync(c, source[StockPrefix.Length..], q, limit, ct),
            _ => await CommunityPageAsync(c, source[CommunityPrefix.Length..], q, limit, ct),
        };
    }

    public async Task<Result<MarketItemDto>> GetAsync(MarketCaller c, string source, string id, CancellationToken ct)
    {
        var (_, sources) = await manifest.TargetAsync(c.Distribution, c.Version, ct);
        if (!sources.TryGetValue(source, out var def)) return MarketErrors.UnknownSource;
        switch (source)
        {
            case "drift-assets":
                foreach (var category in await driftAssets.GetRawAsync(ct))
                    if (category.Assets.FirstOrDefault(a => a.Id == id) is { } asset) return DriftItem(c, asset, category.Label);
                return MarketErrors.ItemNotFound;
            case "sfx":
                foreach (var category in (await sfx.GetAsync(ct)).Categories)
                    if (category.Sounds.FirstOrDefault(s => s.Id == id) is { } sound) return SfxItem(c, sound, category);
                return MarketErrors.ItemNotFound;
        }

        if (def.Kind == "stock")
        {
            var found = await stock.GetItemAsync(c.Stock, id, ct);
            if (found.IsFailure) return found.Error;
            return found.Value.Type == source[StockPrefix.Length..] ? StockItem(c, found.Value) : MarketErrors.ItemNotFound;
        }

        var kind = source[CommunityPrefix.Length..];
        if (!Guid.TryParse(id, out var itemId)) return MarketErrors.ItemNotFound;
        var item = await detail.Handle(new GetItemQuery(Viewer.Anonymous, null, null, itemId), ct);
        if (item.IsFailure) return MarketErrors.ItemNotFound;
        return item.Value.Kind == kind ? CommunityItem(c, item.Value) : MarketErrors.ItemNotFound;
    }

    public async Task<bool> CanResolveAsync(MarketCaller c, CancellationToken ct)
    {
        var (features, sources) = await manifest.TargetAsync(c.Distribution, c.Version, ct);
        return !(features.TryGetValue("resolve", out var flag) && flag.ValueKind == JsonValueKind.False)
            && sources.Values.Any(s => s.Kind == "stock" && s.CanResolve);
    }

    public async Task<Result<MarketResolveJobDto>> StartResolveAsync(MarketCaller c, string? url, CancellationToken ct)
    {
        if (!await CanResolveAsync(c, ct)) return Error.NotFound("resolve_unavailable", "Pasting links is not available.");
        var job = await resolve.StartAsync(new CreateResolveCommand(c.Stock, url ?? ""), ct);
        return job.IsFailure ? job.Error : ResolveJob(c, job.Value);
    }

    public Result<MarketResolveJobDto> GetResolve(MarketCaller c, string id)
    {
        var job = resolve.Get(c.Stock, id);
        return job.IsFailure ? MarketErrors.ItemNotFound : ResolveJob(c, job.Value);
    }

    private static MarketResolveJobDto ResolveJob(MarketCaller c, ResolveJobDto job) =>
        new(job.Id, job.Status, job.Item is null ? null : StockItem(c, job.Item), job.Error);

    private static Result<MarketItemPageDto> Page<T>(IEnumerable<T> all, int offset, int limit, Func<T, MarketItemDto> map)
    {
        var items = all.Skip(offset).Take(limit + 1).ToList();
        var more = items.Count > limit;
        return new MarketItemPageDto(items.Take(limit).Select(map).ToList(), more ? MarketCursor.Encode((offset + limit).ToString()) : null, null);
    }

    private async Task<Result<MarketItemPageDto>> DriftPageAsync(MarketCaller c, MarketQuery q, int limit, CancellationToken ct)
    {
        if (MarketCursor.DecodeOffset(q.Cursor) is not { } offset) return MarketErrors.InvalidCursor;
        var matches = (await driftAssets.GetRawAsync(ct))
            .Where(cat => string.IsNullOrEmpty(q.Category) || cat.Id == q.Category)
            .SelectMany(cat => cat.Assets.Where(a => MarketCursor.Matches(q.Q, a.Name, a.Id, a.Kind, cat.Id, cat.Label)).Select(a => (Asset: a, cat.Label)));
        return Page(matches, offset, limit, m => DriftItem(c, m.Asset, m.Label));
    }

    private async Task<Result<MarketItemPageDto>> SfxPageAsync(MarketCaller c, MarketQuery q, int limit, CancellationToken ct)
    {
        if (MarketCursor.DecodeOffset(q.Cursor) is not { } offset) return MarketErrors.InvalidCursor;
        var (categoryId, subId) = SplitSfxCategory(q.Category);
        var matches = (await sfx.GetAsync(ct)).Categories
            .Where(cat => categoryId is null || cat.Id == categoryId)
            .SelectMany(cat => cat.Sounds
                .Where(s => subId is null || s.Data?.Subcategory == subId)
                .Where(s => MarketCursor.Matches(q.Q, s.Title, s.Id, string.Join(' ', s.Tags), cat.Id, cat.Label))
                .Select(s => (Sound: s, Category: cat)));
        return Page(matches, offset, limit, m => SfxItem(c, m.Sound, m.Category));
    }

    private static MarketItemDto DriftItem(MarketCaller c, DriftIndexAsset a, string categoryLabel)
    {
        var variants = a.Variants.Count > 0
            ? a.Variants.Select(v => new MarketVariantDto(v.Id, string.IsNullOrWhiteSpace(v.Name) ? v.Id : v.Name, null, null,
                MarketMime.For(v.Files["main"].Name), v.Files["main"].Size)).ToList()
            : [new MarketVariantDto("default", "Default", null, null, MarketMime.For(a.Files["main"].Name), a.Files["main"].Size)];
        return new MarketItemDto(a.Id, "drift-assets", a.Name, null, c.Abs(DriftUrl(a.Files["thumbnail"])), c.Abs(DriftUrl(a.Files["preview"])),
            DriftAssetsCatalog.KindFor(a.Kind), null, null, null, [], DriftLicenses.For(a.License), variants, null, a.Category, Extra(new { kind = a.Kind, categoryLabel }));
    }

    public static string DriftUrl(DriftIndexFile f) => $"/files/{f.Sha256}?name={Uri.EscapeDataString(f.Name)}";

    /// <summary>An sfx category filter is <c>category</c> or <c>category/subcategory</c>; the manifest lists the latter as the children of each category.</summary>
    public static (string? Category, string? Subcategory) SplitSfxCategory(string? value)
    {
        if (string.IsNullOrEmpty(value)) return (null, null);
        var slash = value.IndexOf('/');
        return slash < 0 ? (value, null) : (value[..slash], value[(slash + 1)..]);
    }

    private MarketItemDto SfxItem(MarketCaller c, SfxSoundDto s, SfxCategoryDto category)
    {
        var peaks = s.Data is null ? null : sfxAudio.Lookup(s.Data).Peaks;
        return new(s.Id, "sfx", s.Title, null, "", c.Abs(s.PreviewUrl), "audio", s.DurationMs, null, null, s.Tags, SfxLicense,
            [new MarketVariantDto("default", "Original", null, null, MarketMime.For(SfxFileName(s)), s.Size)], null, category.Id,
            peaks is null ? null : Extra(new { peaks }));
    }


    public static string SfxFileName(SfxSoundDto s) => Uri.UnescapeDataString(s.DownloadUrl.Split('/')[^1]);

    private async Task<Result<MarketItemPageDto>> StockPageAsync(MarketCaller c, string type, MarketQuery q, int limit, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(q.Provider) && q.Provider != "*")
        {
            var single = await stock.SearchAsync(c.Stock, type, q.Provider, q.Q, q.Cursor, limit, q.Filters, ct);
            return single.IsFailure ? single.Error : new MarketItemPageDto(single.Value.Items.Select(i => StockItem(c, i)).ToList(), single.Value.NextCursor, single.Value.Quota);
        }

        var eligible = (await stockSettings.EntriesForAsync(type, c.Distribution, ct))
            .Where(e => e.Setting.QuotaLimit <= 0 && e.Provider.Descriptor.Capabilities.HasFlag(ProviderCapabilities.Search)).ToList();
        if (eligible.Count == 0)
        {
            var first = await stock.SearchAsync(c.Stock, type, null, q.Q, q.Cursor, limit, q.Filters, ct);
            return first.IsFailure ? first.Error : new MarketItemPageDto(first.Value.Items.Select(i => StockItem(c, i)).ToList(), first.Value.NextCursor, first.Value.Quota);
        }

        Dictionary<string, string?>? state = null;
        if (!string.IsNullOrEmpty(q.Cursor))
        {
            try
            {
                state = MarketCursor.Decode(q.Cursor) is { } json ? JsonSerializer.Deserialize<Dictionary<string, string?>>(json) : null;
            }
            catch (JsonException)
            {
            }

            if (state is null) return MarketErrors.InvalidCursor;
        }

        var active = eligible.Where(e => state is null || state.GetValueOrDefault(e.Provider.Descriptor.Id) is not null).ToList();
        if (active.Count == 0) return new MarketItemPageDto([], null, null);

        var each = Math.Max(1, limit / active.Count);
        var searches = active.Select(e => SearchOneAsync(c, e, type, q, each, state?.GetValueOrDefault(e.Provider.Descriptor.Id), ct)).ToList();
        var outcomes = await Task.WhenAll(searches);

        if (outcomes.All(o => o.Failure is not null)) return outcomes[0].Failure!;

        var lists = outcomes.Where(o => o.Items is not null).Select(o => o.Items!).ToList();
        var items = new List<MarketItemDto>();
        for (var i = 0; lists.Any(l => i < l.Count); i++)
            items.AddRange(lists.Where(l => i < l.Count).Select(l => l[i]));

        var next = outcomes.Where(o => o.Next is not null).ToDictionary(o => o.Provider, o => o.Next);
        var warnings = outcomes.Where(o => o.Warning is not null).Select(o => o.Warning!).ToList();
        return new MarketItemPageDto(items, next.Count == 0 ? null : MarketCursor.Encode(JsonSerializer.Serialize(next)), null, warnings.Count == 0 ? null : warnings);
    }

    private sealed record Outcome(string Provider, List<MarketItemDto>? Items, string? Next, string? Warning, Error? Failure);

    private async Task<Outcome> SearchOneAsync(MarketCaller c, StockEntry entry, string type, MarketQuery q, int limit, string? cursor, CancellationToken ct)
    {
        var id = entry.Provider.Descriptor.Id;
        var query = new ProviderQuery(type, q.Q?.Trim(), limit, string.IsNullOrEmpty(cursor) ? null : cursor, StockBrowse.CleanFilters(entry.Service, q.Filters));
        try
        {
            var page = await entry.Provider.SearchAsync(query, ct);
            var downloadable = entry.Provider.Descriptor.Capabilities.HasFlag(ProviderCapabilities.Fulfill);
            var items = page.Items.Select(i => assembler.Item(entry.Provider, i with { Type = string.IsNullOrEmpty(i.Type) ? type : i.Type }, downloadable)).ToList();
            return new Outcome(id, items.Select(i => StockItem(c, i)).ToList(), string.IsNullOrEmpty(page.NextCursor) ? null : page.NextCursor, null, null);
        }
        catch (StockException ex)
        {
            var error = StockErrors.From(ex);
            return new Outcome(id, null, null, $"{id}:{error.Code}", error);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
        {
            var error = Error.Unavailable("stock.source_error", "This source is not responding.");
            return new Outcome(id, null, null, $"{id}:{error.Code}", error);
        }
    }

    public static MarketItemDto StockItem(MarketCaller c, StockItemDto i) =>
        new(i.Id, StockPrefix + i.Type, i.Title, i.Creator?.Name, c.Abs(i.ThumbUrl), c.AbsOrNull(i.PreviewUrl),
            StockTypes.Find(i.Type)?.MediaKind ?? i.Type, i.DurationMs, i.Width, i.Height, [], i.License,
            i.Variants?.Select(v => new MarketVariantDto(v.Id, v.Label, v.Width, v.Height, v.Mime, v.Size)).ToList() ?? [], null, null,
            Extra(new { provider = i.Provider, downloadable = i.Downloadable, creator = i.Creator }));

    private async Task<Result<MarketItemPageDto>> CommunityPageAsync(MarketCaller c, string kind, MarketQuery q, int limit, CancellationToken ct)
    {
        var page = await list.Handle(new ListItemsQuery(Viewer.Anonymous, kind, q.Q, [], q.Category, q.Sort, null, null, q.Cursor, limit, q.Period), ct);
        if (page.IsFailure) return page.Error;

        var ids = page.Value.Items.Select(i => i.Id).ToList();
        var rows = (await db.Items.AsNoTracking().Where(i => ids.Contains(i.Id)).Select(i => new
        {
            i.Id,
            i.Category,
            i.License,
            Min = i.Versions.Where(v => v.Id == i.CurrentVersionId).Select(v => v.MinAppVersion).FirstOrDefault(),
        }).ToListAsync(ct)).ToDictionary(r => r.Id);

        var items = new List<MarketItemDto>();
        foreach (var card in page.Value.Items)
        {
            if (!rows.TryGetValue(card.Id, out var row)) continue;
            if (c.Version is { } version && SemVersion.TryParse(row.Min, out var min) && version.CompareTo(min) < 0) continue;
            items.Add(new MarketItemDto(card.Id.ToString(), CommunityPrefix + card.Kind, card.Title, card.Summary, c.AbsOrNull(card.ThumbnailUrl) ?? "",
                c.AbsOrNull(card.PreviewStripUrl), card.Kind, null, null, null, card.Tags, new StockLicenseDto(row.License, null, null), [],
                $"{c.BaseUrl}/community/{card.Kind}/{card.Slug}", row.Category,
                Extra(new { score = card.Score, downloads = card.Downloads, owner = card.Owner.Handle, slug = card.Slug })));
        }

        return new MarketItemPageDto(items, page.Value.NextCursor, null);
    }

    private static MarketItemDto CommunityItem(MarketCaller c, ItemDetailDto d)
    {
        var current = d.Versions.FirstOrDefault(v => v.IsCurrent);
        return new MarketItemDto(d.Id.ToString(), CommunityPrefix + d.Kind, d.Title, d.Summary, c.AbsOrNull(d.ThumbnailUrl) ?? "",
            c.AbsOrNull(d.Media.FirstOrDefault(m => m.Role == MediaRole.PreviewStrip.ToKey())?.Url), d.Kind, null, null, null, d.Tags,
            new StockLicenseDto(d.License, null, null), [], $"{c.BaseUrl}/community/{d.Kind}/{d.Slug}", d.Category,
            Extra(new { score = d.Score, downloads = d.Downloads, owner = d.Owner.Handle, slug = d.Slug, version = current?.Version, minAppVersion = current?.MinAppVersion }));
    }
}
