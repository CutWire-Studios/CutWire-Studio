using CutWire.Studio.Domain.Common;

namespace CutWire.Studio.Domain.Blobs;

public sealed class Blob : Entity<Sha256Hash>
{
    private Blob() { }

    public Blob(Sha256Hash hash, long size, string mime, DateTimeOffset now)
    {
        Id = hash;
        Size = size;
        Mime = mime;
        CreatedAt = now;
        ZeroRefSince = now;
    }

    public long Size { get; private set; }
    public string Mime { get; private set; } = null!;
    public int RefCount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ZeroRefSince { get; private set; }

    public void AddRef()
    {
        RefCount++;
        ZeroRefSince = null;
    }

    public void Release(DateTimeOffset now)
    {
        if (RefCount == 0) return;
        if (--RefCount == 0) ZeroRefSince = now;
    }
}
