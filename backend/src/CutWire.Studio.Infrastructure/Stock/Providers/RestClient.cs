using System.Net;
using System.Text.Json;
using CutWire.Studio.Application.Stock;

namespace CutWire.Studio.Infrastructure.Stock.Providers;

/// <summary>
/// Shared plumbing for keyed JSON REST providers: response caching through <see cref="IProviderCache"/> (keyed by the full URL, so every
/// page is its own entry), single-flight per URL, and translation of HTTP failures into <see cref="StockException"/>.
/// Auth headers belong on the typed HttpClient (set where it is registered) or are passed per call.
/// </summary>
public sealed class RestClient(HttpClient http, IProviderCache cache, TimeSpan ttl)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString };
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<string>>> _flights = new();

    public static string Url(string endpoint, IEnumerable<KeyValuePair<string, string?>>? query)
    {
        var parts = (query ?? []).Where(kv => !string.IsNullOrEmpty(kv.Value))
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}");
        var qs = string.Join("&", parts);
        return qs.Length == 0 ? endpoint : endpoint + (endpoint.Contains('?') ? "&" : "?") + qs;
    }

    public async Task<T> GetJsonAsync<T>(string url, CancellationToken ct, IReadOnlyDictionary<string, string>? headers = null, bool useCache = true)
    {
        var body = await GetStringAsync(url, ct, headers, useCache);
        try
        {
            return JsonSerializer.Deserialize<T>(body, Json) ?? throw new StockException(StockReason.SourceError, "empty body");
        }
        catch (JsonException ex)
        {
            throw new StockException(StockReason.SourceError, ex.Message, ex);
        }
    }

    public async Task<string> GetStringAsync(string url, CancellationToken ct, IReadOnlyDictionary<string, string>? headers = null, bool useCache = true)
    {
        if (useCache && await cache.GetAsync(url, ct) is { } hit) return hit;
        // Callers share one in-flight fetch per URL; the shared fetch is not tied to any single caller's cancellation.
        var lazy = _flights.GetOrAdd(url, u => new Lazy<Task<string>>(() => FetchAsync(u, headers, useCache)));
        try
        {
            return await lazy.Value.WaitAsync(ct);
        }
        finally
        {
            _flights.TryRemove(url, out _);
        }
    }

    private async Task<string> FetchAsync(string url, IReadOnlyDictionary<string, string>? headers, bool useCache)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("User-Agent", "CutWireStudio/1.0 (+https://cutwire.studio)");
        foreach (var (k, v) in headers ?? new Dictionary<string, string>()) request.Headers.TryAddWithoutValidation(k, v);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cts.Token);
        }
        catch (OperationCanceledException ex)
        {
            throw new StockException(StockReason.SourceTimeout, ex.Message, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new StockException(StockReason.SourceUnreachable, ex.Message, ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
            if (body.Length > 16 << 20) throw new StockException(StockReason.SourceError, "response too large");
            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound:
                    throw new StockException(StockReason.ItemMissing, "upstream returned 404");
                case HttpStatusCode.TooManyRequests:
                    // Our own budget with the source is exhausted; that is not the client's quota.
                    throw new StockException(StockReason.SourceBlocked, "upstream rate limited");
            }

            if (!response.IsSuccessStatusCode) throw new StockException(StockReason.SourceError, $"upstream status {(int)response.StatusCode}");
            if (useCache) await cache.SetAsync(url, body, ttl, CancellationToken.None);
            return body;
        }
    }
}

/// <summary>A cache that stores nothing; for tests and providers that must not cache.</summary>
public sealed class NullProviderCache : IProviderCache
{
    public Task<string?> GetAsync(string key, CancellationToken ct) => Task.FromResult<string?>(null);
    public Task SetAsync(string key, string body, TimeSpan ttl, CancellationToken ct) => Task.CompletedTask;
}
