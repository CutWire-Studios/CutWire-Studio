using System.Text.Json.Serialization;

namespace CutWire.Studio.Application.Stock;

public sealed record StockFilterOptionDto(string Id, string Label);

public sealed record StockFilterDto(string Id, string Label, string Kind, IReadOnlyList<StockFilterOptionDto>? Options);

public sealed record StockQuotaDto(int Limit, string Window, int? Remaining);

public sealed record StockProviderDto(string Id, string Label, IReadOnlyList<string> Capabilities, IReadOnlyList<StockFilterDto> Filters, StockQuotaDto? Quota);

public sealed record StockTypeDto(string Id, string Label, string MediaKind, string Delivery, IReadOnlyList<StockProviderDto> Providers);

public sealed record StockCatalogDto(IReadOnlyList<StockTypeDto> Types);

public sealed record StockCreatorDto(string Name, string? Url);

public sealed record StockLicenseDto(string Name, string? Url, string? Attribution);

public sealed record StockVariantDto(string Id, string Label, int? Width, int? Height, string Mime, long? Size);

public sealed record StockItemDto(
    string Id,
    string Type,
    string Provider,
    string Title,
    StockCreatorDto? Creator,
    int? DurationMs,
    int? Width,
    int? Height,
    string ThumbUrl,
    string? PreviewUrl,
    StockLicenseDto License,
    bool Downloadable,
    IReadOnlyList<StockVariantDto>? Variants);

public sealed record StockPageDto(IReadOnlyList<StockItemDto> Items, string? NextCursor, StockQuotaDto? Quota);

public sealed record StockJobErrorDto(string Code, string Detail);

public sealed record ResolveJobDto(string Id, string Status, StockItemDto? Item, StockJobErrorDto? Error);

public sealed record DownloadFileDto(string Filename, string Mime, long Size, string Url, DateTimeOffset ExpiresAt, string Sha256);

public sealed record DownloadJobDto(string Id, string Status, double Progress, DownloadFileDto? File, StockJobErrorDto? Error, StockLicenseDto License);

public sealed record CreateDownloadCommand(StockCaller Caller, string ItemId, string? VariantId);
public sealed record CreateResolveCommand(StockCaller Caller, string Url);

public sealed record SfxSoundDto(string Id, string Title, int DurationMs, IReadOnlyList<string> Tags, string PreviewUrl, string DownloadUrl, long Size,
    [property: JsonIgnore] SfxSoundData? Data = null);
public sealed record SfxCategoryDto(string Id, string Label, IReadOnlyList<SfxSoundDto> Sounds, [property: JsonIgnore] IReadOnlyList<SfxSubcategoryData>? Subcategories = null);
public sealed record SfxLibraryDto(string Version, IReadOnlyList<SfxCategoryDto> Categories);

public sealed record DriftAssetVariantDto(string Id, string Label, string DownloadUrl, long Size);
public sealed record DriftAssetDto(string Id, string Title, string Kind, string ThumbnailUrl, string? PreviewUrl, IReadOnlyList<DriftAssetVariantDto> Variants);
public sealed record DriftAssetCategoryDto(string Id, string Label, IReadOnlyList<DriftAssetDto> Assets);
public sealed record DriftAssetLibraryDto(string Version, IReadOnlyList<DriftAssetCategoryDto> Categories);
