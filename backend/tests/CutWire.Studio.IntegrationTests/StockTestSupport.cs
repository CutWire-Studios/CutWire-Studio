using Microsoft.AspNetCore.TestHost;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using CutWire.Studio.Application.Stock;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;

namespace CutWire.Studio.IntegrationTests;

public static class StockData
{
    public static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    /// <summary>0.1 s of 48 kHz mono 16-bit silence.</summary>
    public static byte[] Wav()
    {
        const int samples = 4800;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8);
        w.Write(36 + samples * 2);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(48000);
        w.Write(96000);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(samples * 2);
        w.Write(new byte[samples * 2]);
        return ms.ToArray();
    }
}

/// <summary>A scriptable provider. Item ids: p1, p2, p3 (searchable), boom (fulfil fails with ItemRemoved).</summary>
public sealed class FakeProvider(string id, string type, QuotaDefault? quota = null, bool audio = false) : IStockProvider
{
    public List<ProviderQuery> Queries { get; } = [];

    public ProviderDescriptor Descriptor { get; } = new(id, "Fake " + id, "A fake source", "Fake attribution",
    [
        new StockService(type,
        [
            new FilterDef("orientation", "Orientation", FilterKinds.Select, [new FilterOption("landscape", "Landscape"), new FilterOption("portrait", "Portrait")]),
            new FilterDef("hd", "HD", FilterKinds.Toggle),
            new FilterDef("duration", "Duration", FilterKinds.Range),
        ], quota),
    ], ProviderCapabilities.Search | ProviderCapabilities.Resolve | ProviderCapabilities.Fulfill);

    public ProviderItem Make(string native, string? type1 = null) => new()
    {
        Native = native,
        Type = type1 ?? type,
        Title = "Item " + native,
        Creator = new ProviderCreator("Ada", "https://example.com/ada"),
        DurationMs = audio ? 100 : 0,
        Width = 1920,
        Height = 1080,
        ThumbSrc = "https://secret-origin.example/thumb/" + native + ".jpg",
        PreviewSrc = "https://secret-origin.example/preview/" + native + (audio ? ".mp3" : ".jpg"),
        PreviewMime = audio ? "audio/mpeg" : "image/jpeg",
        License = new ProviderLicense("Fake License", "https://example.com/license"),
        Variants = [new ProviderVariant("hd", "HD", 1920, 1080, audio ? "audio/wav" : "image/png", 123), new ProviderVariant("sd", "SD", 640, 360)],
    };

    public Task<ProviderPage> SearchAsync(ProviderQuery query, CancellationToken ct)
    {
        Queries.Add(query);
        var items = new[] { "p1", "p2", "p3" }.Where(n => string.IsNullOrEmpty(query.Q) || ("item " + n).Contains(query.Q.ToLowerInvariant()) || n == query.Q).Select(n => Make(n)).ToList();
        if (query.Q == "fail") throw new StockException(StockReason.SourceBlocked, "upstream rate limited");
        return Task.FromResult(new ProviderPage(items, query.Cursor is null ? "2" : null));
    }

    public Task<ProviderItem?> GetItemAsync(string t, string native, CancellationToken ct) =>
        Task.FromResult<ProviderItem?>(native == "gone" ? null : Make(native, t));

    public bool OwnsUrl(string url) => url.StartsWith("https://fake.example/", StringComparison.Ordinal);

    public Task<ProviderItem> ResolveAsync(string url, CancellationToken ct) =>
        url.EndsWith("/bad", StringComparison.Ordinal) ? throw new StockException(StockReason.ItemPrivate) : Task.FromResult(Make("p1"));

    public async Task<ProviderAsset> FetchAsync(FulfillRequest request, StockWorkspace workspace, CancellationToken ct)
    {
        if (request.Native == "boom") throw new StockException(StockReason.ItemRemoved, "gone upstream");
        var path = Path.Combine(workspace.Dir, audio ? "sound.wav" : "pic.png");
        await File.WriteAllBytesAsync(path, audio ? StockData.Wav() : StockData.Png, ct);
        workspace.Report(0.5);
        return new ProviderAsset(path, audio ? "audio/wav" : "image/png", ProviderSupport.FileName("Item " + request.Native, audio ? "wav" : "png"),
            new ProviderLicense("Fake License", "https://example.com/license", "Ada on Fake"), "Ada on Fake");
    }
}

public sealed class StubHttpClients(Func<HttpRequestMessage, HttpResponseMessage> respond) : IStockHttpClients
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public HttpClient Public { get; } = new(new Handler(respond));

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request));
    }
}

public abstract class StockTestBase : IDisposable
{
    protected CatalogFixture F { get; }
    protected FakeProvider Fake { get; } = new("fake", StockTypeIds.Photo, new QuotaDefault(2, "day", "client"));
    protected FakeProvider Audio { get; } = new("fakeaudio", StockTypeIds.Audio, audio: true);

    protected StockTestBase(PostgresFixture postgres, bool fakes = true, Action<IWebHostBuilder>? extra = null)
    {
        F = new CatalogFixture(postgres, builder =>
        {
            builder.UseSetting("Stock:Providers:Freetouse:Enabled", "false");
            builder.UseSetting("Stock:Providers:Ytdlp:Enabled", "false");
            builder.UseSetting("Stock:Media:SealKey", "integration-test-key");
            builder.UseSetting("Stock:Media:AllowPrivateNetworks", "true");
            builder.ConfigureTestServices(services =>
            {
                if (fakes)
                {
                    services.AddSingleton<IStockProvider>(Fake);
                    services.AddSingleton<IStockProvider>(Audio);
                }
            });
            extra?.Invoke(builder);
        });
    }

    protected CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => F.Dispose();

    protected HttpClient Client(string? install = null)
    {
        var client = F.Factory.Browser(cookies: false);
        client.DefaultRequestHeaders.Add("X-Studio-Install", install ?? Guid.NewGuid().ToString());
        return client;
    }

    protected async Task<JsonNode> Ok(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        ((int)response.StatusCode).ShouldBeInRange(200, 299, text);
        return JsonNode.Parse(text)!;
    }

    protected static string ItemId(string provider, string type, string native) => ItemIds.Encode(provider, type, native);

    protected Task<HttpResponseMessage> Download(HttpClient client, string itemId, string? variant = null) =>
        client.PostAsJsonAsync("/api/v1/stock/downloads", new { itemId, variantId = variant }, Ct);

    protected async Task<JsonNode> WaitFor(HttpClient client, string url, Func<JsonNode, bool> done)
    {
        for (var i = 0; i < 100; i++)
        {
            var body = await Ok(await client.GetAsync(url, Ct));
            if (done(body)) return body;
            await Task.Delay(100, Ct);
        }

        throw new TimeoutException("job did not finish: " + url);
    }
}
