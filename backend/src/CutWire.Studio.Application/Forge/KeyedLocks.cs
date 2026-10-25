namespace CutWire.Studio.Application.Forge;

/// <summary>One async mutex per key, dropped again when nobody holds or waits for it.</summary>
public sealed class KeyedLocks<TKey> where TKey : notnull
{
    private sealed class Entry
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public int Users;
    }

    private readonly Dictionary<TKey, Entry> _entries = [];

    public async Task<IDisposable> AcquireAsync(TKey key, CancellationToken ct)
    {
        Entry entry;
        lock (_entries)
        {
            if (!_entries.TryGetValue(key, out entry!)) _entries[key] = entry = new Entry();
            entry.Users++;
        }

        try
        {
            await entry.Gate.WaitAsync(ct);
        }
        catch
        {
            Release(key, entry, held: false);
            throw;
        }

        return new Releaser(() => Release(key, entry, held: true));
    }

    private void Release(TKey key, Entry entry, bool held)
    {
        if (held) entry.Gate.Release();
        lock (_entries)
        {
            if (--entry.Users == 0) _entries.Remove(key);
        }
    }

    private sealed class Releaser(Action release) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0) release();
        }
    }
}
