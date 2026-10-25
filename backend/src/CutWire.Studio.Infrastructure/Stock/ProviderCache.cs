using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Stock;
using CutWire.Studio.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace CutWire.Studio.Infrastructure.Stock;

/// <summary>IMemoryCache in front of the provider_cache table, so provider responses survive restarts and are shared between instances.</summary>
public sealed class ProviderCache(IMemoryCache memory, IServiceScopeFactory scopes, IClock clock) : IProviderCache
{
    private const int MaxKey = 2200;
    private static readonly TimeSpan MemoryTtl = TimeSpan.FromMinutes(10);

    /// <summary>Callers key by full URL, and some providers (Pixabay) carry their API key in it; the key must not be stored.</summary>
    public static string Digest(string key) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)));

    public async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        if (key.Length > MaxKey) return null;
        key = Digest(key);
        if (memory.TryGetValue(key, out string? hit)) return hit;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var now = clock.UtcNow;
        var row = await db.ProviderCache.AsNoTracking().FirstOrDefaultAsync(e => e.Key == key && e.ExpiresAt > now, ct);
        if (row is null) return null;
        memory.Set(key, row.Body, new[] { MemoryTtl, row.ExpiresAt - now }.Min());
        return row.Body;
    }

    public async Task SetAsync(string key, string body, TimeSpan ttl, CancellationToken ct)
    {
        if (key.Length > MaxKey) return;
        key = Digest(key);
        memory.Set(key, body, new[] { MemoryTtl, ttl }.Min());
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var expires = clock.UtcNow + ttl;
        var updated = await db.ProviderCache.Where(e => e.Key == key)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Body, body).SetProperty(e => e.ExpiresAt, expires), ct);
        if (updated > 0) return;
        db.ProviderCache.Add(new ProviderCacheEntry { Key = key, Body = body, ExpiresAt = expires });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent writer stored the same key first.
        }
    }
}
