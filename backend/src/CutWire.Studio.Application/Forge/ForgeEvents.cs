namespace CutWire.Studio.Application.Forge;

public static class ForgeSources
{
    public const string Web = "web";
    public const string Mcp = "mcp";
    public const string Ai = "ai";
}

/// <param name="Type"><c>updated</c> or <c>deleted</c>.</param>
public sealed record ForgeDocEvent(string Type, int? Revision = null, DateTimeOffset? UpdatedAt = null, string? Source = null, string? ClientId = null);

public interface IForgeEventHub
{
    void Publish(Guid documentId, ForgeDocEvent e);

    /// <summary>Events for one document from now on; ends when <paramref name="ct"/> is cancelled.</summary>
    IAsyncEnumerable<ForgeDocEvent> Subscribe(Guid documentId, CancellationToken ct);
}
