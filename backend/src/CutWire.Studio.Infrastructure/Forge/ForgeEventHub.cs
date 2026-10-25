using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using CutWire.Studio.Application.Forge;

namespace CutWire.Studio.Infrastructure.Forge;

/// <summary>In-process fan-out of document events to open SSE streams. A single backend process, so no broker is needed.</summary>
public sealed class ForgeEventHub : IForgeEventHub
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Channel<ForgeDocEvent>>> _subscribers = new();

    public void Publish(Guid documentId, ForgeDocEvent e)
    {
        if (!_subscribers.TryGetValue(documentId, out var channels)) return;
        foreach (var channel in channels.Values) channel.Writer.TryWrite(e);
    }

    public async IAsyncEnumerable<ForgeDocEvent> Subscribe(Guid documentId, [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateBounded<ForgeDocEvent>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        var key = Guid.NewGuid();
        var channels = _subscribers.GetOrAdd(documentId, _ => new ConcurrentDictionary<Guid, Channel<ForgeDocEvent>>());
        channels[key] = channel;
        try
        {
            await foreach (var e in channel.Reader.ReadAllAsync(ct)) yield return e;
        }
        finally
        {
            channels.TryRemove(key, out _);
            if (channels.IsEmpty) _subscribers.TryRemove(new KeyValuePair<Guid, ConcurrentDictionary<Guid, Channel<ForgeDocEvent>>>(documentId, channels));
        }
    }
}
