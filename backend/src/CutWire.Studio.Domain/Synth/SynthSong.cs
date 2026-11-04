using CutWire.Studio.Domain.Common;

namespace CutWire.Studio.Domain.Synth;

public sealed class SynthSong : Entity<Guid>
{
    public const int MaxPerUser = 200;
    public const int MaxDocBytes = 512 * 1024;
    public const int MaxNameLength = 60;
    public const int MaxLocalIdLength = 64;

    private SynthSong() { }

    public SynthSong(Guid ownerId, string localId, string name, string docJson, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        OwnerId = ownerId;
        LocalId = localId;
        Name = name;
        DocJson = docJson;
        Revision = 1;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid OwnerId { get; private set; }
    public string LocalId { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public int Revision { get; private set; }
    public string DocJson { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
}
