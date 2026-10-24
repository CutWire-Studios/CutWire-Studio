using CutWire.Studio.Application.Catalog;
using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Domain.Common;

namespace CutWire.Studio.Infrastructure.Catalog;

public sealed class LanguagePackKind() : AssetKindBase(AssetKindId.LanguagePack)
{
    protected override Result<PackageInfo> Validate(byte[] bytes, Sha256Hash sha256) =>
        Error.Unavailable("kind_disabled", "Language packs are not supported yet.");
}
