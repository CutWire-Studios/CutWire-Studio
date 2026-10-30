using Microsoft.AspNetCore.TestHost;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Marketplace;
using CutWire.Studio.Application.Stock;
using CutWire.Studio.Infrastructure.Jobs;
using CutWire.Studio.Infrastructure.Persistence;
using CutWire.Studio.Infrastructure.Stock;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;

namespace CutWire.Studio.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class StockCatalogTests(PostgresFixture postgres) : StockTestBase(postgres, fakes: false)
{
    [Fact]
    public async Task Manifest_HasNoStockSourcesWhenNothingIsConfigured()
    {
        var manifest = await Ok(await Web().GetAsync(Market, Ct));

        manifest["sources"]!.AsObject().Select(kv => kv.Key).Where(k => k.StartsWith("stock.")).ShouldBeEmpty();
    }
}

[Collection(PostgresCollection.Name)]
public sealed class StockConfiguredCatalogTests(PostgresFixture postgres) : StockTestBase(postgres, fakes: false, b =>
{
    b.UseSetting("Stock:Providers:Pexels:ApiKey", "pexels-key");
    b.UseSetting("Stock:Providers:Jamendo:ClientId", "jamendo-id");
})
{
    private async Task<JsonObject> Sources() => (await Ok(await Web().GetAsync(Market, Ct)))["sources"]!.AsObject();

    [Fact]
    public async Task Manifest_ListsOnlyConfiguredProviders()
    {
        var sources = await Sources();

        var types = sources.Where(kv => kv.Key.StartsWith("stock.")).ToDictionary(kv => kv.Key["stock.".Length..], kv => kv.Value!);
        types.Keys.ShouldBe(["video", "photo", "music", "greenscreen"], ignoreOrder: true);
        types["video"]["mediaKind"]!.GetValue<string>().ShouldBe("video");
        types["photo"]["mediaKind"]!.GetValue<string>().ShouldBe("image");
        types["music"]["mediaKind"]!.GetValue<string>().ShouldBe("music");

        types["photo"]["providers"]!.AsArray().Select(p => p!["id"]!.GetValue<string>()).ShouldBe(["pexels"]);
        types["music"]["providers"]!.AsArray().Select(p => p!["id"]!.GetValue<string>()).ShouldBe(["jamendo"]);
        types["greenscreen"]["providers"]!.AsArray().Select(p => p!["id"]!.GetValue<string>()).ShouldBe(["pexels_greenscreen"]);

        var pexels = types["photo"]["providers"]![0]!;
        pexels["capabilities"]!.AsArray().Select(c => c!.GetValue<string>()).ShouldBe(["search", "featured"]);
        var orientation = pexels["filters"]!.AsArray().Single(f => f!["id"]!.GetValue<string>() == "orientation")!;
        orientation["kind"]!.GetValue<string>().ShouldBe("select");
        orientation["options"]!.AsArray().Count.ShouldBeGreaterThan(1);
        types["video"]["providers"]![0]!["filters"]!.AsArray().Any(f => f!["kind"]!.GetValue<string>() == "range").ShouldBeTrue();
    }

    [Fact]
    public async Task DisablingAProviderSettingRemovesItsSource()
    {
        await Sources();
        await F.Sql("update provider_settings set enabled = false where provider = 'jamendo'");
        F.Factory.Services.GetRequiredService<ManifestCache>().Invalidate();

        (await Sources()).ContainsKey("stock.music").ShouldBeFalse();
    }
}

[Collection(PostgresCollection.Name)]
public sealed class StockBrowseTests(PostgresFixture postgres) : StockTestBase(postgres)
{
    private const string Photos = Market + "/sources/stock.photo/items";

    [Fact]
    public async Task Manifest_ShowsFilterKindsAndQuotaLimitsButNotRemaining()
    {
        var sources = (await Ok(await Web().GetAsync(Market, Ct)))["sources"]!;

        var provider = sources["stock.photo"]!["providers"]!.AsArray().Single()!;
        provider["id"]!.GetValue<string>().ShouldBe("fake");
        provider["filters"]!.AsArray().Select(f => f!["kind"]!.GetValue<string>()).ShouldBe(["select", "toggle", "range"]);
        provider["quota"]!["limit"]!.GetValue<int>().ShouldBe(2);
        provider["quota"]!["window"]!.GetValue<string>().ShouldBe("day");
        provider["quota"]!["remaining"].ShouldBeNull();

        sources["stock.audio"]!["providers"]![0]!["quota"].ShouldBeNull();
    }

    [Fact]
    public async Task Search_MapsItemsHidesOriginsAndForwardsOnlyValidFilters()
    {
        var page = await Ok(await Web().GetAsync(Photos + "?provider=fake&q=Item&limit=10&orientation=landscape&hd=true&duration=10-60&bogus=1&orientation2=x", Ct));

        page["nextCursor"].ShouldNotBeNull();
        page["quota"]!["remaining"]!.GetValue<int>().ShouldBe(2);
        var items = page["items"]!.AsArray();
        items.Count.ShouldBe(3);
        var first = items[0]!;
        first["id"]!.GetValue<string>().ShouldBe(ItemId("fake", "photo", "p1"));
        first["source"]!.GetValue<string>().ShouldBe("stock.photo");
        first["subtitle"]!.GetValue<string>().ShouldBe("Ada");
        first["durationMs"].ShouldBeNull();
        first["license"]!["name"]!.GetValue<string>().ShouldBe("Fake License");
        first["thumbUrl"]!.GetValue<string>().ShouldContain("/api/v1/stock/media/");
        first["previewUrl"]!.GetValue<string>().ShouldContain("/api/v1/stock/media/");
        first.ToJsonString().ShouldNotContain("secret-origin");
        first["variants"]!.AsArray().Count.ShouldBe(2);

        var query = Fake.Queries.Single();
        query.Limit.ShouldBe(10);
        query.Filters.ShouldBe(new Dictionary<string, string> { ["orientation"] = "landscape", ["hd"] = "1", ["duration"] = "10-60" });
    }

    [Fact]
    public async Task Search_ReportsProblemsWithStableCodes()
    {
        var client = Web();

        var unknownProvider = await client.GetAsync(Photos + "?provider=ghost", Ct);
        unknownProvider.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await unknownProvider.Json(Ct))["code"]!.GetValue<string>().ShouldBe("stock.provider_not_found");

        var blocked = await client.GetAsync(Photos + "?provider=fake&q=fail", Ct);
        blocked.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await blocked.Json(Ct))["code"]!.GetValue<string>().ShouldBe("stock.source_blocked");
    }

    [Fact]
    public async Task Item_IsLookedUpFromItsIdWithoutAPriorSearch()
    {
        var item = await Ok(await Web().GetAsync($"{Photos}/{Uri.EscapeDataString(ItemId("fake", "photo", "zzz"))}", Ct));

        item["title"]!.GetValue<string>().ShouldBe("Item zzz");
        item["variants"]!.AsArray().Select(v => v!["id"]!.GetValue<string>()).ShouldBe(["hd", "sd"]);
        item["variants"]![0]!["mime"]!.GetValue<string>().ShouldBe("image/png");
        item["variants"]![1]!["mime"]!.GetValue<string>().ShouldBe("image/jpeg");
    }

    [Fact]
    public async Task Item_UnknownIdsAre404()
    {
        var response = await Web().GetAsync($"{Photos}/not-an-id", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Json(Ct))["code"]!.GetValue<string>().ShouldBe("stock.item_missing");

        (await Web().GetAsync($"{Photos}/{ItemId("fake", "photo", "gone")}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Web().GetAsync($"{Photos}/{ItemId("ghost", "photo", "1")}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}

[Collection(PostgresCollection.Name)]
public sealed class StockDownloadTests(PostgresFixture postgres) : StockTestBase(postgres)
{
    private static string JobUrl(JsonNode job) => $"{Market}/installs/{job["id"]!.GetValue<string>()}";

    private async Task<int> Remaining(HttpClient client) =>
        (await Ok(await client.GetAsync(Market + "/sources/stock.photo/items?provider=fake", Ct)))["quota"]!["remaining"]!.GetValue<int>();

    [Fact]
    public async Task Image_DownloadCompletesInlineAndTheSealedUrlServesTheFile()
    {
        var client = Web();

        var response = await Download(client, ItemId("fake", "photo", "p1"), "hd");
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var job = await response.Json(Ct);

        job["status"]!.GetValue<string>().ShouldBe("ready");
        job["progress"]!.GetValue<double>().ShouldBe(1);
        job["license"]!["name"]!.GetValue<string>().ShouldBe("Fake License");
        job["license"]!["attribution"]!.GetValue<string>().ShouldBe("Ada on Fake");
        var file = job["files"]!.AsArray().Single()!;
        file["role"]!.GetValue<string>().ShouldBe("main");
        file["mime"]!.GetValue<string>().ShouldBe("image/png");
        file["name"]!.GetValue<string>().ShouldEndWith(".png");
        file["size"]!.GetValue<long>().ShouldBe(StockData.Png.Length);
        file["sha256"]!.GetValue<string>().ShouldBe(Convert.ToHexStringLower(SHA256.HashData(StockData.Png)));

        var url = PathOf(file["url"]!);
        (await client.GetByteArrayAsync(url, Ct)).ShouldBe(StockData.Png);

        var range = new HttpRequestMessage(HttpMethod.Get, url) { Headers = { Range = new RangeHeaderValue(0, 7) } };
        var partial = await client.SendAsync(range, Ct);
        partial.StatusCode.ShouldBe(HttpStatusCode.PartialContent);
        (await partial.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(StockData.Png[..8]);

        (await client.GetAsync(url.Replace("/files/", "/files/00", StringComparison.Ordinal), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var polled = await Ok(await client.GetAsync(JobUrl(job), Ct));
        polled["status"]!.GetValue<string>().ShouldBe("ready");
        (await Web().GetAsync(JobUrl(job), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Audio_DownloadRunsInTheBackgroundAndIsNormalisedByFfmpeg()
    {
        if (ExecutableLocator.Find("ffmpeg") is null) Assert.Skip("ffmpeg is not installed; the audio normalisation path needs it.");
        var client = Web();

        var created = await Download(client, ItemId("fakeaudio", "audio", "p1"), null, "stock.audio");
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var job = await created.Json(Ct);
        job["status"]!.GetValue<string>().ShouldBeOneOf("queued", "processing", "ready");

        var done = await WaitFor(client, JobUrl(job), j => j["status"]!.GetValue<string>() is "ready" or "failed");

        done["status"]!.GetValue<string>().ShouldBe("ready", done.ToJsonString());
        var file = done["files"]![0]!;
        file["mime"]!.GetValue<string>().ShouldBe("audio/wav");
        var bytes = await client.GetByteArrayAsync(PathOf(file["url"]!), Ct);
        Encoding.ASCII.GetString(bytes, 0, 4).ShouldBe("RIFF");
        file["size"]!.GetValue<long>().ShouldBe(bytes.Length);
    }

    [Fact]
    public async Task Quota_IsExhaustedAfterTheLimitWith429RetryAfterAndPerInstall()
    {
        var client = Web("install-a");
        var id = ItemId("fake", "photo", "p1");

        (await Download(client, id)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await Download(client, id)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var third = await Download(client, id);
        third.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        third.Headers.RetryAfter!.Delta!.Value.TotalSeconds.ShouldBeGreaterThan(0);
        var problem = await third.Json(Ct);
        problem["code"]!.GetValue<string>().ShouldBe("stock.quota_exceeded");
        problem["retryAfter"]!.GetValue<int>().ShouldBe((int)third.Headers.RetryAfter.Delta.Value.TotalSeconds);

        (await Remaining(client)).ShouldBe(0);

        (await Download(Web("install-b"), id)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task HiddenFor_ProviderHiddenForADistributionIsNotInstallableFromIt()
    {
        var manifest = await Ok(await Web().GetAsync(Market, Ct));
        manifest["sources"]!["stock.photo"]!["providers"]!.AsArray().Select(p => p!["id"]!.GetValue<string>()).ShouldContain("fake");
        await F.Sql("update provider_settings set hidden_for = array['web'] where provider = 'fake'");
        F.Factory.Services.GetRequiredService<ManifestCache>().Invalidate();

        var hidden = await Ok(await Web().GetAsync(Market, Ct));
        hidden["sources"]!.AsObject().ContainsKey("stock.photo").ShouldBeFalse();
        (await Download(Web(), ItemId("fake", "photo", "p1"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task FailedJobRefundsItsQuotaSlot()
    {
        var client = Web();

        var failed = await Ok(await Download(client, ItemId("fake", "photo", "boom")));

        failed["status"]!.GetValue<string>().ShouldBe("failed");
        failed["error"]!["code"]!.GetValue<string>().ShouldBe("stock.item_removed");
        failed["error"]!["detail"]!.GetValue<string>().ShouldNotContain("upstream");
        failed["files"].ShouldBeNull();

        (await Remaining(client)).ShouldBe(2);
        (await F.Db(db => db.QuotaEvents.CountAsync(Ct))).ShouldBe(2);
    }

    [Fact]
    public async Task Download_UnknownItemsAre404()
    {
        var client = Web();

        (await Download(client, "nope")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Download(client, ItemId("ghost", "photo", "1"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync($"{Market}/installs/stock_{Guid.NewGuid()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AccountScopedQuotaFollowsTheUserAcrossInstalls()
    {
        await F.Sql("update provider_settings set quota_scope = 'account' where provider = 'fake'");
        var user = await F.Factory.SignedIn(Ct);
        user.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
        await Ok(await user.GetAsync(Market, Ct));
        await F.Sql("update provider_settings set quota_scope = 'account', quota_limit = 1 where provider = 'fake'");
        F.Factory.Services.GetRequiredService<ManifestCache>().Invalidate();

        string[] installs = ["one", "two"];
        var statuses = new List<HttpStatusCode>();
        foreach (var install in installs)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, Market + "/installs")
            {
                Content = JsonContent.Create(new { source = "stock.photo", itemId = ItemId("fake", "photo", "p1") }),
            };
            request.Headers.Add("X-Studio-Install", install);
            statuses.Add((await user.SendAsync(request, Ct)).StatusCode);
        }

        statuses.ShouldBe([HttpStatusCode.Created, HttpStatusCode.TooManyRequests]);
    }

    [Fact]
    public async Task ExpirySweepDeletesFinishedJobsAndReleasesTheirBlobs()
    {
        var client = Web();
        var job = await Ok(await Download(client, ItemId("fake", "photo", "p1")));
        job["files"]![0]!["sha256"]!.GetValue<string>().Length.ShouldBe(64);
        (await F.Db(db => db.Blobs.Where(b => b.RefCount > 0).CountAsync(Ct))).ShouldBe(1);

        await F.Sql("update download_jobs set expires_at = now() - interval '1 minute'");
        await using (var scope = F.Factory.Services.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<StockExpiry>().SweepAsync(Ct);
            result.Jobs.ShouldBe(1);
        }

        (await F.Db(db => db.DownloadJobs.CountAsync(Ct))).ShouldBe(0);
        (await F.Db(db => db.Blobs.Where(b => b.RefCount > 0).CountAsync(Ct))).ShouldBe(0);
        (await client.GetAsync(JobUrl(job), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var recurring = F.Factory.Services.GetRequiredService<IEnumerable<IRecurringJob>>().OfType<StockExpiryJob>().Single();
        await recurring.RunAsync(Ct);
    }
}

[Collection(PostgresCollection.Name)]
public sealed class StockResolveTests(PostgresFixture postgres) : StockTestBase(postgres, extra: b => b.ConfigureTestServices(s =>
    s.AddSingleton(new UnavailableProvider("youtube", "yt-dlp not found", url => url.StartsWith("https://youtu.be/", StringComparison.Ordinal)))))
{
    private const string ResolveUrl = Market + "/resolve";

    [Fact]
    public async Task Resolve_RunsAsAJobAndYieldsAnItem()
    {
        var client = Web();

        var created = await client.PostAsJsonAsync(ResolveUrl, new { url = "https://fake.example/watch/1" }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var job = await created.Json(Ct);
        job["status"]!.GetValue<string>().ShouldBeOneOf("queued", "processing", "ready");

        var done = await WaitFor(client, $"{ResolveUrl}/{job["id"]!.GetValue<string>()}", j => j["status"]!.GetValue<string>() is "ready" or "failed");

        done["status"]!.GetValue<string>().ShouldBe("ready");
        done["error"].ShouldBeNull();
        done["item"]!["title"]!.GetValue<string>().ShouldBe("Item p1");
        done["item"]!["extra"]!["provider"]!.GetValue<string>().ShouldBe("fake");
        done["item"]!["thumbUrl"]!.GetValue<string>().ShouldContain("/api/v1/stock/media/");
        (await Web().GetAsync($"{ResolveUrl}/{job["id"]!.GetValue<string>()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Resolve_ProviderFailureSurfacesOnTheJobWithAStableCode()
    {
        var client = Web();
        var job = await Ok(await client.PostAsJsonAsync(ResolveUrl, new { url = "https://fake.example/bad" }, Ct));

        var done = await WaitFor(client, $"{ResolveUrl}/{job["id"]!.GetValue<string>()}", j => j["status"]!.GetValue<string>() is "ready" or "failed");

        done["status"]!.GetValue<string>().ShouldBe("failed");
        done["item"].ShouldBeNull();
        done["error"]!["code"]!.GetValue<string>().ShouldBe("stock.item_private");
    }

    [Fact]
    public async Task Resolve_DistinguishesUnsupportedFromUnavailableSources()
    {
        var client = Web();

        var unsupported = await client.PostAsJsonAsync(ResolveUrl, new { url = "https://nowhere.example/x" }, Ct);
        unsupported.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await unsupported.Json(Ct))["code"]!.GetValue<string>().ShouldBe("stock.link_unsupported");

        var off = await client.PostAsJsonAsync(ResolveUrl, new { url = "https://youtu.be/abc" }, Ct);
        off.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await off.Json(Ct))["code"]!.GetValue<string>().ShouldBe("stock.source_disabled");

        var bad = await client.PostAsJsonAsync(ResolveUrl, new { url = "javascript:alert(1)" }, Ct);
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}

[Collection(PostgresCollection.Name)]
public sealed class StockMediaRelayTests(PostgresFixture postgres) : StockTestBase(postgres, extra: b => b.ConfigureTestServices(s =>
{
    s.RemoveAll<IStockHttpClients>();
    s.AddSingleton<IStockHttpClients>(Stub);
}))
{
    private static readonly StubHttpClients Stub = new(request =>
    {
        var body = Encoding.ASCII.GetBytes("0123456789abcdef");
        if (request.Headers.Range?.Ranges.FirstOrDefault() is { } r)
        {
            var from = (int)r.From!.Value;
            var to = (int)(r.To ?? body.Length - 1);
            var slice = body[from..(to + 1)];
            var partial = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(slice) };
            partial.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, body.Length);
            partial.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            return partial;
        }

        var ok = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        ok.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return ok;
    });

    [Fact]
    public async Task Relay_StreamsThePreviewWithRangeSupportAndNeverExposesTheOrigin()
    {
        var client = Web();
        var page = await Ok(await client.GetAsync(Market + "/sources/stock.photo/items?provider=fake", Ct));
        var preview = PathOf(page["items"]![0]!["previewUrl"]!);

        var full = await client.GetAsync(preview, Ct);
        full.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await full.Content.ReadAsStringAsync(Ct)).ShouldBe("0123456789abcdef");
        full.Headers.CacheControl!.ToString().ShouldContain("max-age");
        full.Content.Headers.ContentType!.MediaType.ShouldBe("image/jpeg");

        var request = new HttpRequestMessage(HttpMethod.Get, preview) { Headers = { Range = new RangeHeaderValue(2, 5) } };
        var partial = await client.SendAsync(request, Ct);
        partial.StatusCode.ShouldBe(HttpStatusCode.PartialContent);
        (await partial.Content.ReadAsStringAsync(Ct)).ShouldBe("2345");
        partial.Content.Headers.ContentRange!.ToString().ShouldBe("bytes 2-5/16");

        (await client.GetAsync("/api/v1/stock/media/garbage", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}

[Collection(PostgresCollection.Name)]
public sealed class StockSfxTests : StockTestBase
{
    private static readonly string Dir = Directory.CreateTempSubdirectory("stock-sfx-").FullName;

    public StockSfxTests(PostgresFixture postgres) : base(postgres, extra: b => b.UseSetting("Stock:Sfx:Path", Dir))
    {
        Directory.CreateDirectory(Path.Combine(Dir, "library", "ui"));
        Directory.CreateDirectory(Path.Combine(Dir, "previews"));
        File.WriteAllText(Path.Combine(Dir, "library", "ui", "click one.flac"), "FLACDATA-0123456789");
        File.WriteAllText(Path.Combine(Dir, "previews", "ab12.opus"), "OPUSDATA");
        File.WriteAllText(Path.Combine(Dir, "sfx.json"), """
            {"schema":1,
             "categories":[{"id":"ui","label":"UI","subcategories":[{"id":"click","label":"Clicks"}]},{"id":"nature","label":"Nature","subcategories":[]}],
             "sounds":[
               {"id":"s1","name":"Click one","category":"ui","subcategory":"click","duration":0.25,"size":19,"path":"ui/click one.flac","preview":"ab12.opus"},
               {"id":"s2","name":"Rain","category":"nature","subcategory":"","duration":3,"size":5,"path":"nature/rain.flac","preview":"rain.opus"}
             ]}
            """);
    }

    private const string SfxItems = Market + "/sources/sfx/items";

    [Fact]
    public async Task Sfx_ListsSoundsAndServesFilesWithRange()
    {
        var client = Web();

        var sources = (await Ok(await client.GetAsync(Market, Ct)))["sources"]!;
        sources["sfx"]!["categories"]!.AsArray().Select(c => c!["id"]!.GetValue<string>()).ShouldBe(["ui", "nature"]);
        var page = await Ok(await client.GetAsync(SfxItems, Ct));
        var sound = page["items"]![0]!;
        sound["id"]!.GetValue<string>().ShouldBe("s1");
        sound["title"]!.GetValue<string>().ShouldBe("Click one");
        sound["durationMs"]!.GetValue<int>().ShouldBe(250);
        sound["tags"]!.AsArray().Select(t => t!.GetValue<string>()).ShouldBe(["Clicks"]);

        (await client.GetStringAsync(PathOf(sound["previewUrl"]!), Ct)).ShouldBe("OPUSDATA");
        var install = await Ok(await client.PostAsJsonAsync(Market + "/installs", new { source = "sfx", itemId = "s1" }, Ct));
        var file = install["files"]![0]!;
        file["size"]!.GetValue<long>().ShouldBe(19);
        var request = new HttpRequestMessage(HttpMethod.Get, PathOf(file["url"]!)) { Headers = { Range = new RangeHeaderValue(5, 9) } };
        var partial = await client.SendAsync(request, Ct);
        partial.StatusCode.ShouldBe(HttpStatusCode.PartialContent);
        (await partial.Content.ReadAsStringAsync(Ct)).ShouldBe("ATA-0");
        partial.Content.Headers.ContentType!.MediaType.ShouldBe("audio/flac");
    }

    [Fact]
    public async Task Sfx_RefusesTraversalAndUnknownAreas()
    {
        var client = Web();

        (await client.GetAsync("/api/v1/stock/sfx/files/library/..%2Fsfx.json", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync("/api/v1/stock/sfx/files/library/%2e%2e/sfx.json", Ct)).StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
        (await client.GetAsync("/api/v1/stock/sfx/files/secrets/x", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync("/api/v1/stock/sfx/files/library/missing.flac", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sfx_HonoursCategorySettings()
    {
        var client = Web();
        await Ok(await client.GetAsync(SfxItems, Ct));
        await F.Sql("update sfx_categories set enabled = false where category = 'nature'; update sfx_categories set label = 'Interface' where category = 'ui'");
        F.Factory.Services.GetRequiredService<ManifestCache>().Invalidate();

        var categories = (await Ok(await client.GetAsync(Market, Ct)))["sources"]!["sfx"]!["categories"]!.AsArray();

        categories.Select(c => c!["label"]!.GetValue<string>()).ShouldBe(["Interface"]);
        (await Ok(await client.GetAsync(SfxItems, Ct)))["items"]!.AsArray().Select(i => i!["id"]!.GetValue<string>()).ShouldBe(["s1"]);
    }
}

[Collection(PostgresCollection.Name)]
public sealed class StockEmptySfxTests(PostgresFixture postgres) : StockTestBase(postgres)
{
    [Fact]
    public async Task SfxAndDriftAssetsAreAbsentWhenNoLibraryIsConfigured()
    {
        var sources = (await Ok(await Web().GetAsync(Market, Ct)))["sources"]!.AsObject();

        sources.ContainsKey("sfx").ShouldBeFalse();
        sources.ContainsKey("drift-assets").ShouldBeFalse();
        (await Web().GetAsync(Market + "/sources/sfx/items", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}

[Collection(PostgresCollection.Name)]
public sealed class StockDriftAssetsTests : StockTestBase
{
    private static readonly byte[] Lottie = Encoding.UTF8.GetBytes("""{"v":"5.7","layers":[]}""");
    private static readonly byte[] Thumb = Encoding.UTF8.GetBytes("thumb-bytes");
    private static readonly byte[] Preview = Encoding.UTF8.GetBytes("preview-bytes");
    private static readonly byte[] Model = Encoding.UTF8.GetBytes("glb-bytes-0123456789");
    private static readonly byte[] ModelThumb = Encoding.UTF8.GetBytes("model-thumb");
    private static readonly byte[] ModelPreview = Encoding.UTF8.GetBytes("model-preview");

    private static string Sha(byte[] b) => Convert.ToHexStringLower(SHA256.HashData(b));

    private static string File1(string name, byte[] data) => $"{{\"name\":\"{name}\",\"size\":{data.Length},\"sha256\":\"{Sha(data)}\"}}";

    private static string IndexJson(string version) =>
        "{\"schema\":1,\"version\":\"" + version + "\",\"categories\":[{\"id\":\"memes\",\"label\":\"Memes\",\"kind\":\"lottie\",\"sort\":1},{\"id\":\"objects\",\"label\":\"Objects\",\"kind\":\"object\",\"sort\":2}],\"assets\":["
        + "{\"id\":\"party-hat\",\"name\":\"Party hat\",\"kind\":\"lottie\",\"category\":\"memes\",\"path\":\"memes/party-hat\",\"files\":{\"main\":" + File1("main.json", Lottie) + ",\"thumbnail\":" + File1("t.webp", Thumb) + ",\"preview\":" + File1("p.webp", Preview) + "}},"
        + "{\"id\":\"crate\",\"name\":\"Crate\",\"kind\":\"object\",\"category\":\"objects\",\"path\":\"objects/crate\",\"files\":{\"main\":" + File1("crate.glb", Model) + ",\"thumbnail\":" + File1("t.webp", ModelThumb) + ",\"preview\":" + File1("p.webp", ModelPreview) + "}}"
        + "]}";

    private static byte[] Archive(bool corrupt)
    {
        using var raw = new MemoryStream();
        using (var gzip = new GZipStream(raw, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip, leaveOpen: true))
        {
            void Add(string name, byte[] data)
            {
                if (corrupt && name.EndsWith("crate.glb")) data = Encoding.UTF8.GetBytes("tampered-bytes-00000");
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(data) });
            }

            Add("memes/party-hat/main.json", Lottie);
            Add("memes/party-hat/t.webp", Thumb);
            Add("memes/party-hat/p.webp", Preview);
            Add("objects/crate/crate.glb", Model);
            Add("objects/crate/t.webp", ModelThumb);
            Add("objects/crate/p.webp", ModelPreview);
            Add("unlisted/extra.bin", [1, 2, 3]);
        }

        return raw.ToArray();
    }

    private static string _version = "1.0.0";
    private static bool _corrupt;
    private static int _archiveFetches;

    private static readonly StubHttpClients Stub = new(request =>
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/index.json")) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(IndexJson(_version)) };
        if (path.EndsWith("/drift-assets.tar.gz"))
        {
            Interlocked.Increment(ref _archiveFetches);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Archive(_corrupt)) };
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    });

    public StockDriftAssetsTests(PostgresFixture postgres) : base(postgres, extra: b =>
    {
        b.UseSetting("Stock:DriftAssets:Repo", "CutWire-Studios/Drift-Assets");
        b.UseSetting("Stock:DriftAssets:BaseUrl", "https://mirror.example/release");
        b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IStockHttpClients>();
            s.AddSingleton<IStockHttpClients>(Stub);
        });
    })
    {
        _version = "1.0.0";
        _corrupt = false;
        _archiveFetches = 0;
    }

    private Task Sync() => F.Factory.Services.GetRequiredService<IEnumerable<IRecurringJob>>().OfType<DriftAssetsSyncJob>().Single().RunAsync(Ct);

    private async Task<JsonNode> Items() => await Ok(await Web().GetAsync(Market + "/sources/drift-assets/items", Ct));

    [Fact]
    public async Task Mirror_PublishesThePackFromBlobsAndSkipsUnchangedVersions()
    {
        await Sync();
        F.Factory.Services.GetRequiredService<ManifestCache>().Invalidate();

        var client = Web();
        var sources = (await Ok(await client.GetAsync(Market, Ct)))["sources"]!;
        sources["drift-assets"]!["categories"]!.AsArray().Select(c => c!["id"]!.GetValue<string>()).ShouldBe(["memes", "objects"]);
        var items = (await Items())["items"]!.AsArray();
        items.Select(i => i!["id"]!.GetValue<string>()).ShouldBe(["party-hat", "crate"]);
        var hat = items[0]!;
        hat["title"]!.GetValue<string>().ShouldBe("Party hat");
        hat["mediaKind"]!.GetValue<string>().ShouldBe("lottie");
        (await client.GetByteArrayAsync(PathOf(hat["thumbUrl"]!), Ct)).ShouldBe(Thumb);
        (await client.GetByteArrayAsync(PathOf(hat["previewUrl"]!), Ct)).ShouldBe(Preview);
        items[1]!["mediaKind"]!.GetValue<string>().ShouldBe("model3d");

        var install = await Ok(await client.PostAsJsonAsync(Market + "/installs", new { source = "drift-assets", itemId = "party-hat" }, Ct));
        var main = install["files"]!.AsArray().Single(f => f!["role"]!.GetValue<string>() == "main")!;
        main["size"]!.GetValue<long>().ShouldBe(Lottie.Length);
        (await client.GetByteArrayAsync(PathOf(main["url"]!), Ct)).ShouldBe(Lottie);

        await Sync();
        _archiveFetches.ShouldBe(1);
    }

    [Fact]
    public async Task Mirror_NewReleaseSwapsInAndKeepsOnlyTheLastTwoVersions()
    {
        await Sync();
        _version = "1.1.0";
        await Sync();
        _version = "1.2.0";
        await Sync();

        (await Items())["items"]!.AsArray().Count.ShouldBe(2);
        (await F.Db(db => db.DriftAssetPacks.OrderBy(p => p.Version).Select(p => p.Version + ":" + (p.IsCurrent ? "true" : "false")).ToListAsync(Ct))).ShouldBe(["1.1.0:false", "1.2.0:true"]);
        (await F.Db(db => db.Blobs.Where(b => b.RefCount > 0).CountAsync(Ct))).ShouldBe(6);
    }

    [Fact]
    public async Task Mirror_RejectsAnArchiveThatDoesNotMatchTheIndex()
    {
        _corrupt = true;

        await Sync();

        (await Web().GetAsync(Market + "/sources/drift-assets/items", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await F.Db(db => db.DriftAssetPacks.CountAsync(Ct))).ShouldBe(0);
    }

    [Fact]
    public async Task Categories_CanBeHiddenAndRelabelledThroughSettings()
    {
        await Sync();
        await F.Sql("update asset_categories set enabled = false where category = 'objects'; update asset_categories set label = 'Funny' where category = 'memes'");
        F.Factory.Services.GetRequiredService<ManifestCache>().Invalidate();

        var categories = (await Ok(await Web().GetAsync(Market, Ct)))["sources"]!["drift-assets"]!["categories"]!.AsArray();

        categories.Select(c => c!["label"]!.GetValue<string>()).ShouldBe(["Funny"]);
    }
}
