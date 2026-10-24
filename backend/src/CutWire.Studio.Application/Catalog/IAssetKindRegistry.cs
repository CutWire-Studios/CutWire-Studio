using CutWire.Studio.Domain.Catalog;

namespace CutWire.Studio.Application.Catalog;

public interface IAssetKindRegistry
{
    IAssetKind? Get(AssetKindId id);
    IReadOnlyList<IAssetKind> ListEnabled();
}
