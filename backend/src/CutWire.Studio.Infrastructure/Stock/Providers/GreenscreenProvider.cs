using CutWire.Studio.Application.Stock;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CutWire.Studio.Infrastructure.Stock.Providers;

public static class GreenscreenRegistration
{
    public const string Keywords = "green screen";

    public static void Register(IServiceCollection services, IConfiguration config)
    {
        if (PexelsRegistration.IsConfigured(config))
            services.AddSingleton<IStockProvider>(sp => new GreenscreenProvider(sp.GetRequiredService<PexelsProvider>(), Keywords));
        if (PixabayRegistration.IsConfigured(config))
            services.AddSingleton<IStockProvider>(sp => new GreenscreenProvider(sp.GetRequiredService<PixabayProvider>(), Keywords));
    }
}

/// <summary>Exposes a video source as the "greenscreen" catalog type by prepending keywords to the query.</summary>
public sealed class GreenscreenProvider(IStockProvider inner, string keywords) : IStockProvider
{
    public ProviderDescriptor Descriptor { get; } = Describe(inner);

    private static ProviderDescriptor Describe(IStockProvider inner)
    {
        var d = inner.Descriptor;
        return new ProviderDescriptor(
            d.Id + "_greenscreen", d.Label, "Chroma key footage from " + d.Label + ".", d.Attribution,
            [new StockService(StockTypeIds.Greenscreen, d.Serving(StockTypeIds.Video)?.Filters)],
            ProviderCapabilities.Search | ProviderCapabilities.Fulfill);
    }

    public async Task<ProviderPage> SearchAsync(ProviderQuery query, CancellationToken ct)
    {
        var page = await inner.SearchAsync(query with { Type = StockTypeIds.Video, Q = $"{keywords} {query.Q}".Trim() }, ct);
        return page with { Items = [.. page.Items.Select(i => i with { Type = StockTypeIds.Greenscreen })] };
    }

    public async Task<ProviderItem?> GetItemAsync(string type, string native, CancellationToken ct) =>
        await inner.GetItemAsync(StockTypeIds.Video, native, ct) is { } item ? item with { Type = StockTypeIds.Greenscreen } : null;

    public Task<ProviderAsset> FetchAsync(FulfillRequest request, StockWorkspace workspace, CancellationToken ct) =>
        inner.FetchAsync(request with { Type = StockTypeIds.Video }, workspace, ct);
}
