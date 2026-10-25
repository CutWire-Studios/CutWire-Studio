using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Common;

namespace CutWire.Studio.Domain.Forge;

public sealed class ForgeDocument : Entity<Guid>
{
    public const int MaxPerUser = 500;
    public const int MaxDocBytes = 2 * 1024 * 1024;
    public const int MaxInlineAssetBytes = 1024;
    public const int MaxNameLength = 120;
    public const int MaxLocalIdLength = 64;

    private ForgeDocument() { }

    public ForgeDocument(Guid ownerId, string localId, string kind, string name, string docJson, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        OwnerId = ownerId;
        LocalId = localId;
        Kind = kind;
        Name = name;
        DocJson = docJson;
        Revision = 1;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid OwnerId { get; private set; }
    public string LocalId { get; private set; } = null!;
    public string Kind { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public int Revision { get; private set; }
    public string DocJson { get; private set; } = null!;
    public Sha256Hash? ThumbSha256 { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
}

/// <summary>One blob a document references from its assets; the pair is unique, so a blob used twice in a document counts once.</summary>
public sealed class ForgeDocAsset
{
    private ForgeDocAsset() { }

    public ForgeDocAsset(Guid documentId, Sha256Hash blob)
    {
        DocumentId = documentId;
        BlobSha256 = blob;
    }

    public Guid DocumentId { get; private set; }
    public Sha256Hash BlobSha256 { get; private set; }
}

public sealed class AiUsage
{
    private AiUsage() { }

    public DateOnly Day { get; private set; }

    /// <summary>A user id, or <c>*</c> for the global budget.</summary>
    public string Sub { get; private set; } = null!;

    public double Neurons { get; private set; }
}

public sealed class ThemeDraft
{
    public const int MaxPerUser = 100;
    public const int MaxDocBytes = 512 * 1024;
    public const int MaxIdLength = 64;
    public const int MaxNameLength = 200;

    private ThemeDraft() { }

    public ThemeDraft(Guid ownerId, string id, string name, string docJson, long updatedAtMs)
    {
        OwnerId = ownerId;
        Id = id;
        Name = name;
        DocJson = docJson;
        UpdatedAtMs = updatedAtMs;
    }

    public Guid OwnerId { get; private set; }
    public string Id { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string DocJson { get; private set; } = null!;

    /// <summary>The client's own clock (JavaScript milliseconds); drafts merge by it on the client.</summary>
    public long UpdatedAtMs { get; private set; }

    public void Replace(string name, string docJson, long updatedAtMs)
    {
        Name = name;
        DocJson = docJson;
        UpdatedAtMs = updatedAtMs;
    }
}
