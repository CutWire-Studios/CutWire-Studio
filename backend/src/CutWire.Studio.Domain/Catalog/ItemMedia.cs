using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Common;

namespace CutWire.Studio.Domain.Catalog;

public sealed class ItemMedia : Entity<Guid>
{
    private ItemMedia() { }

    public ItemMedia(Guid id, Guid itemId, MediaRole role, Sha256Hash blob, int width, int height, int bytes, ulong dHash)
    {
        Id = id;
        ItemId = itemId;
        Role = role;
        BlobSha256 = blob;
        Width = width;
        Height = height;
        Bytes = bytes;
        DHash = unchecked((long)dHash);
    }

    public Guid ItemId { get; private set; }
    public MediaRole Role { get; private set; }
    public Sha256Hash BlobSha256 { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Bytes { get; private set; }

    /// <summary>64-bit difference hash, stored as a signed bigint so Postgres can XOR and popcount it.</summary>
    public long DHash { get; private set; }
}
