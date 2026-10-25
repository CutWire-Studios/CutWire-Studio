namespace CutWire.Studio.Application.Stock;

/// <summary>The only place an origin URL becomes a client URL, so "never hand out a raw provider URL" is enforceable by inspection.</summary>
public sealed class StockAssembler(IMediaUrls urls)
{
    public StockItemDto Item(IStockProvider provider, ProviderItem item, bool downloadable)
    {
        var d = provider.Descriptor;
        var type = string.IsNullOrEmpty(item.Type) ? d.Serves.FirstOrDefault()?.Type ?? "" : item.Type;
        var license = item.License is { } l
            ? new StockLicenseDto(l.Name, HttpUrl(l.Url), Nullable(l.Attribution))
            : new StockLicenseDto($"{d.Label} terms", null, Nullable(d.Attribution));
        var variants = item.Variants.Count == 0
            ? null
            : item.Variants.Select(v => new StockVariantDto(v.Id, v.Label, Positive(v.Width), Positive(v.Height),
                string.IsNullOrEmpty(v.Mime) ? DefaultMime(type) : v.Mime, v.Size > 0 ? v.Size : null)).ToList();
        return new StockItemDto(
            ItemIds.Encode(d.Id, type, item.Native),
            type,
            d.Id,
            item.Title,
            item.Creator is { } c ? new StockCreatorDto(c.Name, HttpUrl(c.Url)) : null,
            Positive(item.DurationMs),
            Positive(item.Width),
            Positive(item.Height),
            string.IsNullOrEmpty(item.ThumbSrc) ? "" : urls.Thumb(item.ThumbSrc),
            string.IsNullOrEmpty(item.PreviewSrc) ? null : urls.Preview(item.PreviewSrc, item.PreviewMime),
            license,
            downloadable,
            variants);
    }

    public static string DefaultMime(string type) => type switch
    {
        StockTypeIds.Photo => "image/jpeg",
        StockTypeIds.Audio or StockTypeIds.Music => "audio/mpeg",
        _ => "video/mp4",
    };

    private static int? Positive(int n) => n > 0 ? n : null;
    /// <summary>Provider links end up in href attributes; anything but http(s) (javascript:, data:) is dropped.</summary>
    public static string? HttpUrl(string? s) => ProviderSupport.TryParseHttpUrl(s, out _) ? s!.Trim() : null;

    private static string? Nullable(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
