namespace CutWire.Studio.Domain.Catalog;

public sealed class DownloadEvent
{
    private DownloadEvent() { }

    public DownloadEvent(Guid itemId, Guid? versionId, Guid? userId, string? installId, string ipHash, DateTimeOffset at)
    {
        ItemId = itemId;
        VersionId = versionId;
        UserId = userId;
        InstallId = installId;
        IpHash = ipHash;
        At = at;
    }

    public long Id { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid? VersionId { get; private set; }
    public Guid? UserId { get; private set; }
    public string? InstallId { get; private set; }
    public string IpHash { get; private set; } = null!;
    public DateTimeOffset At { get; private set; }
    public DateTimeOffset? RolledUpAt { get; private set; }
}

/// <summary>Distinct downloaders per item per UTC day, written by the rollup job.</summary>
public sealed class ItemDownloadDaily
{
    private ItemDownloadDaily() { }

    public Guid ItemId { get; private set; }
    public DateOnly Day { get; private set; }
    public int Downloads { get; private set; }
}
