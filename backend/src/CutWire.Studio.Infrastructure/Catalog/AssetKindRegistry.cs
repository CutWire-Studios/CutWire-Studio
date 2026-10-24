using CutWire.Studio.Application.Catalog;
using CutWire.Studio.Domain.Catalog;

namespace CutWire.Studio.Infrastructure.Catalog;

public sealed class AssetKindRegistry(IEnumerable<IAssetKind> kinds) : IAssetKindRegistry
{
    private readonly Dictionary<AssetKindId, IAssetKind> _kinds = kinds.ToDictionary(k => k.Id);

    public IAssetKind? Get(AssetKindId id) => _kinds.GetValueOrDefault(id);

    public IReadOnlyList<IAssetKind> ListEnabled() => _kinds.Values.Where(k => k.Enabled).OrderBy(k => k.Id.Value, StringComparer.Ordinal).ToList();
}
