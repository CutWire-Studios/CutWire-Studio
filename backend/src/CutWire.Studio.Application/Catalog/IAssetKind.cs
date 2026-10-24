using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Domain.Common;

namespace CutWire.Studio.Application.Catalog;

public interface IAssetKind
{
    AssetKindId Id { get; }
    string DisplayName { get; }
    IReadOnlyList<string> AcceptedExtensions { get; }
    long MaxPackageBytes { get; }
    long MaxUnpackedBytes { get; }
    IReadOnlyList<MediaSpec> MediaSpecs { get; }
    IReadOnlyList<string> Categories { get; }
    IReadOnlyList<string> TagVocabulary { get; }
    bool Enabled { get; }

    Task<Result<PackageInfo>> ValidateAsync(Stream package, CancellationToken ct);
}
