using System.Text.Json;
using System.Text.Json.Nodes;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Catalog;
using CutWire.Studio.Application.Stock;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Domain.Marketplace;
using CutWire.Studio.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CutWire.Studio.Application.Marketplace;

/// <summary>Holds the sources built for each distribution for a short time. Admin changes to categories, providers or flags call <see cref="Invalidate"/>.</summary>
public sealed class ManifestCache : IDisposable
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public async Task<IReadOnlyDictionary<string, MarketSourceDto>> SourcesAsync(
        string distribution, Func<Task<IReadOnlyDictionary<string, MarketSourceDto>>> build) =>
        (await _cache.GetOrCreateAsync(distribution, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Ttl;
            return build();
        }))!;

    public void Invalidate() => _cache.Clear();

    public void Dispose() => _cache.Dispose();
}

/// <summary>
/// Builds the manifest a caller receives: the best matching layout, the matching flags folded into <c>features</c>, and the sources that exist
/// for the caller's distribution. Sections whose source is missing are pruned, then pages left empty.
/// </summary>
public sealed class MarketplaceManifest(
    IAppDbContext db, DriftAssetsCatalog driftAssets, SfxCatalog sfx, StockBrowse stock, IAssetKindRegistry kinds, ManifestCache cache)
{
    public static readonly string[] CommunityKinds = ["effect", "transition", "audio-effect", "theme", "look", "effect-template"];

    private static readonly Lazy<string> SeedJson = new(() =>
    {
        using var stream = typeof(MarketplaceManifest).Assembly.GetManifestResourceStream("CutWire.Studio.Application.Marketplace.default-layout.json")
            ?? throw new InvalidOperationException("default-layout.json is not embedded.");
        return new StreamReader(stream).ReadToEnd();
    });

    public static string DefaultLayoutJson => SeedJson.Value;

    public async Task<MarketplaceManifestDto> BuildAsync(string callerDistribution, SemVersion? version, CancellationToken ct)
    {
        var distribution = Distributions.All.Contains(callerDistribution) ? callerDistribution : Distributions.Unknown;
        var features = await FeaturesAsync(distribution, version, ct);
        var sources = Filter(await cache.SourcesAsync(distribution, () => SourcesAsync(distribution, ct)), features);
        var pages = Prune(await PagesAsync(distribution, version, ct), sources);
        return new MarketplaceManifestDto(MarketplaceManifestDto.CurrentSchema, features, sources, JsonSerializer.SerializeToElement(pages));
    }

    /// <summary>Matching flags are applied from least to most specific (distribution list, then each version bound), then oldest to newest, so the last one wins.</summary>
    private async Task<Dictionary<string, JsonElement>> FeaturesAsync(string distribution, SemVersion? version, CancellationToken ct)
    {
        var flags = (await db.FeatureFlags.AsNoTracking().ToListAsync(ct)).Where(f => f.Matches(distribution, version))
            .OrderBy(f => Targeting.Specificity(f.Distributions, f.MinAppVersion, f.MaxAppVersion)).ThenBy(f => f.UpdatedAt);
        var folded = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var flag in flags)
            folded[flag.Key] = Value(flag);
        return folded;
    }

    private static JsonElement Value(FeatureFlag flag)
    {
        if (!string.IsNullOrWhiteSpace(flag.ValueJson))
        {
            try
            {
                return JsonDocument.Parse(flag.ValueJson).RootElement.Clone();
            }
            catch (JsonException)
            {
            }
        }

        return JsonSerializer.SerializeToElement(flag.Enabled);
    }

    private async Task<JsonArray> PagesAsync(string distribution, SemVersion? version, CancellationToken ct)
    {
        var layouts = (await db.MarketplaceLayouts.AsNoTracking().Where(l => l.Enabled).ToListAsync(ct))
            .Where(l => l.Matches(distribution, version))
            .OrderByDescending(l => l.Priority).ThenByDescending(l => l.UpdatedAt);
        foreach (var layout in layouts)
            if (LayoutValidator.Validate(layout.PagesJson) is { IsSuccess: true } valid)
                return valid.Value;
        return (JsonArray)JsonNode.Parse(DefaultLayoutJson)!;
    }

    private static Dictionary<string, MarketSourceDto> Filter(IReadOnlyDictionary<string, MarketSourceDto> sources, Dictionary<string, JsonElement> features)
    {
        bool Off(string key) => features.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.False;
        var communityOff = Off("community");
        return sources.Where(s => !(s.Value.Kind == "community" && (communityOff || Off(s.Key))) && !Off("source." + s.Key))
            .ToDictionary(s => s.Key, s => s.Value);
    }

    private async Task<IReadOnlyDictionary<string, MarketSourceDto>> SourcesAsync(string distribution, CancellationToken ct)
    {
        var sources = new Dictionary<string, MarketSourceDto>(StringComparer.Ordinal);

        var library = await driftAssets.GetAsync(ct);
        if (library.Categories.Count > 0)
            sources["drift-assets"] = new MarketSourceDto("drift-assets", "Drift Assets", null, true,
                library.Categories.Select(c => new MarketCategoryDto(c.Id, c.Label, c.Assets.FirstOrDefault()?.Kind, null)).ToList(), null, null, false);

        var sounds = await sfx.GetAsync(ct);
        if (sounds.Categories.Count > 0)
            sources["sfx"] = new MarketSourceDto("sfx", "Sound effects", "audio", true,
                sounds.Categories.Select(c => new MarketCategoryDto(c.Id, c.Label, "audio", null)).ToList(), null, null, false);

        var catalog = await stock.CatalogAsync(new StockCaller("manifest", null, distribution), ct);
        foreach (var type in catalog.Types)
        {
            var providers = type.Providers.Select(p => p with { Quota = p.Quota is null ? null : p.Quota with { Remaining = null } }).ToList();
            sources["stock." + type.Id] = new MarketSourceDto("stock", type.Label, type.MediaKind,
                providers.Any(p => p.Capabilities.Contains("search")), null, providers, null, providers.Any(p => p.Capabilities.Contains("resolve")));
        }

        var sorts = Enum.GetNames<ItemSort>().Select(n => n.ToLowerInvariant()).ToList();
        foreach (var kind in kinds.ListEnabled().Where(k => CommunityKinds.Contains(k.Id.Value)))
            sources["community." + kind.Id.Value] = new MarketSourceDto("community", kind.DisplayName, null, true,
                kind.Categories.Select(c => new MarketCategoryDto(c, c, null, null)).ToList(), null, sorts, false);

        return sources;
    }

    private static JsonArray Prune(JsonArray pages, IReadOnlyDictionary<string, MarketSourceDto> sources)
    {
        var kept = new JsonArray();
        foreach (var node in pages.ToList())
        {
            var page = node!.AsObject();
            var sections = page["sections"]!.AsArray();
            var survivors = sections.Where(s => Survives(s!.AsObject(), sections, sources)).Select(s => s!.DeepClone()).ToList();
            if (survivors.Count == 0) continue;
            var copy = page.DeepClone().AsObject();
            copy["sections"] = new JsonArray(survivors.ToArray());
            kept.Add(copy);
        }

        var ids = kept.Select(p => p!["id"]!.GetValue<string>()).ToHashSet();
        foreach (var section in kept.SelectMany(p => p!["sections"]!.AsArray()).Select(s => s!.AsObject()))
        {
            if (section["seeAll"]?["page"]?.GetValue<string>() is { } see && !ids.Contains(see)) section.Remove("seeAll");
            if (section["action"] is JsonObject { } action && action["type"]?.GetValue<string>() == "open-page" && !ids.Contains(action["page"]!.GetValue<string>()))
                section.Remove("action");
        }

        return kept;
    }

    private static bool Survives(JsonObject section, JsonArray siblings, IReadOnlyDictionary<string, MarketSourceDto> sources)
    {
        if (section["source"]?["id"]?.GetValue<string>() is not { } source) return true;
        if (!sources.ContainsKey(source)) return false;
        if (section["type"]?.GetValue<string>() != LayoutDocument.Chips) return true;
        var target = siblings.OfType<JsonObject>().First(s => s["id"]?.GetValue<string>() == section["target"]!.GetValue<string>());
        return target["source"]?["id"]?.GetValue<string>() is { } t && sources.ContainsKey(t);
    }
}
