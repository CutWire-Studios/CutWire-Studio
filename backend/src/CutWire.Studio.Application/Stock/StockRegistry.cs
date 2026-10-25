namespace CutWire.Studio.Application.Stock;

/// <summary>Every compiled-in and configured provider, in registration order.</summary>
public sealed class StockRegistry
{
    private readonly List<IStockProvider> _all;
    private readonly List<UnavailableProvider> _off;

    public StockRegistry(IEnumerable<IStockProvider> providers, IEnumerable<UnavailableProvider> unavailable)
    {
        _all = providers.GroupBy(p => p.Descriptor.Id).Select(g => g.First()).ToList();
        _off = unavailable.ToList();
    }

    public IReadOnlyList<IStockProvider> All => _all;

    public IStockProvider? Get(string id) => _all.FirstOrDefault(p => p.Descriptor.Id == id);

    public IStockProvider? ResolverFor(string url) =>
        _all.FirstOrDefault(p => p.Descriptor.Capabilities.HasFlag(ProviderCapabilities.Resolve) && p.OwnsUrl(url));

    public UnavailableProvider? UnavailableFor(string url) => _off.FirstOrDefault(u => u.OwnsUrl(url));
}
