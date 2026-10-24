using System.Text.Json;
using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Common;

namespace CutWire.Studio.Domain.Catalog;

public sealed class ItemVersion : Entity<Guid>
{
    private ItemVersion() { }

    public ItemVersion(Guid id, Guid itemId, string version, string changelog, Sha256Hash packageSha256, long size,
        PackageFormat format, string minAppVersion, JsonElement manifest, DateTimeOffset now)
    {
        Id = id;
        ItemId = itemId;
        Version = version;
        Changelog = changelog;
        PackageSha256 = packageSha256;
        Size = size;
        Format = format;
        MinAppVersion = minAppVersion;
        Manifest = manifest.Clone();
        CreatedAt = now;
    }

    public Guid ItemId { get; private set; }
    public string Version { get; private set; } = null!;
    public string Changelog { get; private set; } = "";
    public Sha256Hash PackageSha256 { get; private set; }
    public long Size { get; private set; }
    public PackageFormat Format { get; private set; }
    public string MinAppVersion { get; private set; } = null!;
    public JsonElement Manifest { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public string FileExtension => Format switch
    {
        PackageFormat.DriftFx => ".driftfx",
        PackageFormat.Json => ".json",
        PackageFormat.Zip => ".zip",
        PackageFormat.Cube => ".cube",
        _ => "",
    };
}
