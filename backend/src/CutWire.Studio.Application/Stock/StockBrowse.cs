using CutWire.Studio.Domain.Common;

namespace CutWire.Studio.Application.Stock;

public sealed class StockBrowse(StockSettings settings, StockQuota quota, StockAssembler assembler)
{
    public const int DefaultLimit = 30;
    public const int MaxLimit = 50;

    public async Task<StockCatalogDto> CatalogAsync(StockCaller caller, CancellationToken ct)
    {
        var types = new List<StockTypeDto>();
        foreach (var type in StockTypes.All)
        {
            var providers = new List<StockProviderDto>();
            foreach (var entry in await settings.EntriesForAsync(type.Id, caller.Distribution, ct))
                providers.Add(new StockProviderDto(
                    entry.Provider.Descriptor.Id, entry.Label, Capabilities(entry.Provider.Descriptor),
                    entry.Service.FilterList.Select(f => new StockFilterDto(f.Id, f.Label, f.Kind, f.Options?.Select(o => new StockFilterOptionDto(o.Id, o.Label)).ToList())).ToList(),
                    await quota.DtoAsync(entry, caller, ct)));
            if (providers.Count > 0) types.Add(new StockTypeDto(type.Id, type.Label, type.MediaKind, type.Delivery, providers));
        }

        return new StockCatalogDto(types);
    }

    public static IReadOnlyList<string> Capabilities(ProviderDescriptor d)
    {
        var caps = new List<string>();
        if (d.Capabilities.HasFlag(ProviderCapabilities.Search)) caps.AddRange(["search", "featured"]);
        if (d.Capabilities.HasFlag(ProviderCapabilities.Resolve)) caps.Add("resolve");
        return caps;
    }

    public async Task<Result<StockPageDto>> SearchAsync(StockCaller caller, string? type, string? providerId, string? q, string? cursor, int? limit,
        IReadOnlyDictionary<string, string> rawFilters, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(type) || StockTypes.Find(type) is null)
            return Error.Validation("stock.invalid_type", "A known media type is required.", new Dictionary<string, string[]> { ["type"] = ["Unknown type."] });

        var entries = await settings.EntriesForAsync(type, caller.Distribution, ct);
        var entry = string.IsNullOrEmpty(providerId)
            ? entries.FirstOrDefault(e => e.Provider.Descriptor.Capabilities.HasFlag(ProviderCapabilities.Search))
            : entries.FirstOrDefault(e => e.Provider.Descriptor.Id == providerId);
        if (entry is null) return string.IsNullOrEmpty(providerId) ? new StockPageDto([], null, null) : StockErrors.ProviderNotFound;
        if (!entry.Provider.Descriptor.Capabilities.HasFlag(ProviderCapabilities.Search)) return new StockPageDto([], null, await quota.DtoAsync(entry, caller, ct));

        var query = new ProviderQuery(type, q?.Trim(), Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit), cursor, CleanFilters(entry.Service, rawFilters));
        ProviderPage page;
        try
        {
            page = await entry.Provider.SearchAsync(query, ct);
        }
        catch (StockException ex)
        {
            return StockErrors.From(ex);
        }

        var status = await quota.DtoAsync(entry, caller, ct);
        var downloadable = status is null or { Remaining: > 0 } && entry.Provider.Descriptor.Capabilities.HasFlag(ProviderCapabilities.Fulfill);
        return new StockPageDto(page.Items.Select(i => assembler.Item(entry.Provider, i with { Type = string.IsNullOrEmpty(i.Type) ? type : i.Type }, downloadable)).ToList(),
            string.IsNullOrEmpty(page.NextCursor) ? null : page.NextCursor, status);
    }

    public async Task<Result<StockItemDto>> GetItemAsync(StockCaller caller, string id, CancellationToken ct)
    {
        if (!ItemIds.TryDecode(id, out var r)) return StockErrors.ItemGone;
        var entry = await settings.EntryAsync(r.Provider, r.Type, caller.Distribution, ct);
        if (entry is null) return StockErrors.ItemGone;
        ProviderItem? item;
        try
        {
            item = await entry.Provider.GetItemAsync(r.Type, r.Native, ct);
        }
        catch (StockException ex)
        {
            return StockErrors.From(ex);
        }

        if (item is null) return StockErrors.ItemGone;
        var status = await quota.DtoAsync(entry, caller, ct);
        var downloadable = status is null or { Remaining: > 0 } && entry.Provider.Descriptor.Capabilities.HasFlag(ProviderCapabilities.Fulfill);
        return assembler.Item(entry.Provider, item with { Type = string.IsNullOrEmpty(item.Type) ? r.Type : item.Type }, downloadable);
    }

    /// <summary>Only declared filters are forwarded; selects must match an option, toggles normalise to "1", ranges stay free text.</summary>
    public static IReadOnlyDictionary<string, string> CleanFilters(StockService service, IReadOnlyDictionary<string, string> raw)
    {
        var clean = new Dictionary<string, string>();
        foreach (var f in service.FilterList)
        {
            if (!raw.TryGetValue(f.Id, out var value) || string.IsNullOrWhiteSpace(value)) continue;
            value = value.Trim();
            switch (f.Kind)
            {
                case FilterKinds.Select:
                    if (f.Options is null || f.Options.Any(o => o.Id == value)) clean[f.Id] = value;
                    break;
                case FilterKinds.Toggle:
                    if (value is "1" or "true" or "on" or "yes") clean[f.Id] = "1";
                    break;
                default:
                    if (value.Length <= 40) clean[f.Id] = value;
                    break;
            }
        }

        return clean;
    }
}
