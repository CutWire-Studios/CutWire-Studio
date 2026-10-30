using System.Text.Json;
using CutWire.Studio.Application.Stock;

namespace CutWire.Studio.Application.Marketplace;

public sealed record MarketCategoryDto(string Id, string Label, string? MediaKind, IReadOnlyList<MarketCategoryDto>? Children);

/// <summary><paramref name="Kind"/> is the source id's prefix: drift-assets, sfx, stock or community.</summary>
public sealed record MarketSourceDto(
    string Kind,
    string Label,
    string? MediaKind,
    bool Searchable,
    IReadOnlyList<MarketCategoryDto>? Categories,
    IReadOnlyList<StockProviderDto>? Providers,
    IReadOnlyList<string>? Sorts,
    bool CanResolve);

public sealed record MarketplaceManifestDto(
    int Schema,
    IReadOnlyDictionary<string, JsonElement> Features,
    IReadOnlyDictionary<string, MarketSourceDto> Sources,
    JsonElement Pages)
{
    public const int CurrentSchema = 1;
}

public sealed record MarketVariantDto(string Id, string Label, int? Width, int? Height, string? Mime, long? Size);

public sealed record MarketItemDto(
    string Id,
    string Source,
    string Title,
    string? Subtitle,
    string ThumbUrl,
    string? PreviewUrl,
    string MediaKind,
    int? DurationMs,
    int? Width,
    int? Height,
    IReadOnlyList<string> Tags,
    StockLicenseDto License,
    IReadOnlyList<MarketVariantDto> Variants,
    string? WebUrl,
    string? Category,
    JsonElement? Extra);

/// <summary><paramref name="Warnings"/> names providers a fan-out query skipped because they failed, as <c>providerId:code</c>.</summary>
public sealed record MarketItemPageDto(IReadOnlyList<MarketItemDto> Items, string? NextCursor, StockQuotaDto? Quota, IReadOnlyList<string>? Warnings = null);

public sealed record MarketResolveJobDto(string Id, string Status, MarketItemDto? Item, StockJobErrorDto? Error);

public sealed record InstallFileDto(string Role, string Name, string Url, long Size, string? Sha256, string? Mime);

public sealed record InstallJobDto(
    string Id,
    string Source,
    string Status,
    double Progress,
    IReadOnlyList<InstallFileDto>? Files,
    StockLicenseDto? License,
    StockJobErrorDto? Error,
    string? MinAppVersion);
