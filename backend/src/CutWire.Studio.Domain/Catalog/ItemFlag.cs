using System.Text.Json;
using CutWire.Studio.Domain.Common;

namespace CutWire.Studio.Domain.Catalog;

/// <summary>A non-blocking gate finding stored for the moderation queue.</summary>
public sealed class ItemFlag : Entity<Guid>
{
    private ItemFlag() { }

    public ItemFlag(Guid id, Guid itemId, string field, string code, string message, JsonElement parameters, DateTimeOffset now)
    {
        Id = id;
        ItemId = itemId;
        Field = field;
        Code = code;
        Message = message;
        Params = parameters.Clone();
        CreatedAt = now;
    }

    public Guid ItemId { get; private set; }
    public string Field { get; private set; } = null!;
    public string Code { get; private set; } = null!;
    public string Message { get; private set; } = null!;
    public JsonElement Params { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
}
