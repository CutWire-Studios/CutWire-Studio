namespace CutWire.Studio.Infrastructure.Stock;

/// <summary>Top-level <c>Stock</c> section. Per-provider keys live under <c>Stock:Providers:&lt;Id&gt;</c>.</summary>
public sealed class StockOptions
{
    public const string Section = "Stock";
    public const string ProvidersSection = "Stock:Providers";

    /// <summary>How long a REST origin body (Pexels, Unsplash, Pixabay, Jamendo, freetouse) is reused. Pixabay's terms require at least 24h.</summary>
    public TimeSpan SearchCacheTtl { get; set; } = TimeSpan.FromDays(7);
}
