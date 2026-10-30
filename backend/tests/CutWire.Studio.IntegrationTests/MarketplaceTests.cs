using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CutWire.Studio.Api.Http;
using CutWire.Studio.Application.Marketplace;
using CutWire.Studio.Application.Stock;
using CutWire.Studio.Domain.Marketplace;
using CutWire.Studio.Domain.Stock;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace CutWire.Studio.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class MarketplaceTests(PostgresFixture postgres) : StockTestBase(postgres, extra: b =>
{
    b.UseSetting("DriftClient:Keys:0", Key);
    b.UseSetting("Stock:Sfx:Path", SfxDir);
    b.ConfigureTestServices(services =>
    {
        services.AddSingleton<IStockProvider>(Beta);
        services.AddSingleton<IStockProvider>(Gamma);
        services.AddSingleton<IStockProvider>(Down);
        services.AddSingleton<ISfxPcmDecoder, FakePcmDecoder>();
    });
})
{
    private const string Key = "integration-drift-key";
    private const string Target = "/api/v1/marketplace";
    private static readonly string ClientId = new('b', 64);
    private static readonly FakeProvider Beta = new("fakeb", StockTypeIds.Photo);
    private static readonly FakeProvider Gamma = new("fakec", StockTypeIds.Photo);
    private static readonly FakeProvider Down = new("fakex", StockTypeIds.Photo, broken: true);

    private const string RainPeaks = "AP8=";
    private static readonly string RainSha = new('a', 64);

    private sealed class FakePcmDecoder : ISfxPcmDecoder
    {
        public Task<short[]> DecodeAsync(string path, CancellationToken ct) => Task.FromResult(new short[] { 16384, 16384 });
    }

    private static readonly string SfxDir = Directory.CreateTempSubdirectory("market-sfx-").FullName;

    static MarketplaceTests()
    {
        Directory.CreateDirectory(Path.Combine(SfxDir, "library", "ui"));
        Directory.CreateDirectory(Path.Combine(SfxDir, "previews"));
        File.WriteAllText(Path.Combine(SfxDir, "library", "ui", "click one.flac"), "FLACDATA-0123456789");
        File.WriteAllText(Path.Combine(SfxDir, "previews", "ab12.opus"), "OPUSDATA");
        File.WriteAllText(Path.Combine(SfxDir, "sfx.json"), """
            {"schema":1,
             "categories":[{"id":"ui","label":"UI","subcategories":[{"id":"click","label":"Clicks"}]},{"id":"nature","label":"Nature","subcategories":[{"id":"weather","label":"Weather"},{"id":"empty","label":"Empty"}]}],
             "sounds":[
               {"id":"s1","name":"Click one","category":"ui","subcategory":"click","duration":0.25,"size":19,"path":"ui/click one.flac","preview":"ab12.opus"},
               {"id":"s2","name":"Rain","category":"nature","subcategory":"weather","duration":3,"size":5,"path":"nature/rain.flac","preview":"rain.opus","peaks":"AP8=","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},
               {"id":"s3","name":"Thunder","category":"nature","subcategory":"","duration":4,"size":6,"path":"nature/thunder.flac","preview":"thunder.opus"}
             ]}
            """);
    }

    private static HttpRequestMessage Signed(string distribution = "appimage", string version = "1.2.3", string? ifNoneMatch = null) =>
        SignedTo(HttpMethod.Get, Target, null, distribution, version, ifNoneMatch);

    private static HttpRequestMessage SignedTo(HttpMethod method, string target, object? body = null, string distribution = "appimage", string version = "1.2.3", string? ifNoneMatch = null)
    {
        var bytes = body is null ? [] : JsonContent.Create(body).ReadAsByteArrayAsync().GetAwaiter().GetResult();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonce = Guid.NewGuid().ToString("N");
        var request = new HttpRequestMessage(method, target);
        if (body is not null) request.Content = new ByteArrayContent(bytes) { Headers = { ContentType = new("application/json") } };
        request.Headers.Add("X-Cutwire-Client", ClientId);
        request.Headers.Add("X-Cutwire-Timestamp", timestamp);
        request.Headers.Add("X-Cutwire-Nonce", nonce);
        request.Headers.Add("X-Cutwire-App", $"Drift/{version} (linux)");
        request.Headers.Add("X-Cutwire-Distribution", distribution);
        request.Headers.Add("X-Cutwire-Signature", DriftClientAuth.SignatureFor(Key, timestamp, nonce, ClientId, method.Method, target, bytes));
        if (ifNoneMatch is not null) request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        return request;
    }

    private async Task<JsonNode> Manifest(string distribution = "appimage", string version = "1.2.3") =>
        await Ok(await Client().SendAsync(Signed(distribution, version), Ct));

    private static string[] PageIds(JsonNode manifest) => manifest["pages"]!.AsArray().Select(p => p!["id"]!.GetValue<string>()).ToArray();

    private static string[] ProviderIds(JsonNode manifest, string source) =>
        manifest["sources"]![source]?["providers"]?.AsArray().Select(p => p!["id"]!.GetValue<string>()).ToArray() ?? [];

    private Task AddLayout(string name, int priority, string pagesJson, string[]? distributions = null, string? min = null, string? max = null, DateTimeOffset? at = null) =>
        F.Db(async db =>
        {
            db.MarketplaceLayouts.Add(new MarketplaceLayout
            {
                Name = name, Priority = priority, PagesJson = pagesJson, Distributions = [.. distributions ?? []],
                MinAppVersion = min, MaxAppVersion = max, UpdatedAt = at ?? DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(Ct);
            return 0;
        });

    private Task AddFlag(string key, bool enabled, string? valueJson = null, string[]? distributions = null, string? min = null, string? max = null, DateTimeOffset? at = null) =>
        F.Db(async db =>
        {
            db.FeatureFlags.Add(new FeatureFlag
            {
                Key = key, Enabled = enabled, ValueJson = valueJson, Distributions = [.. distributions ?? []],
                MinAppVersion = min, MaxAppVersion = max, UpdatedAt = at ?? DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(Ct);
            return 0;
        });

    private static string OnePage(string id) => $$"""[{"id":"{{id}}","title":"{{id}}","sections":[{"id":"n","type":"notice","text":"hello"}]}]""";

    [Fact]
    public async Task Unsigned_NonSameOrigin_Is401_SameOriginIsWeb()
    {
        (await Client().GetAsync(Target, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var web = Client();
        web.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
        var manifest = await Ok(await web.GetAsync(Target, Ct));
        manifest["schema"]!.GetValue<int>().ShouldBe(1);
    }

    [Fact]
    public async Task Manifest_HasStockSourcesWithProvidersAndCapabilities()
    {
        var manifest = await Manifest();

        manifest["sources"]!["stock.photo"]!["kind"]!.GetValue<string>().ShouldBe("stock");
        manifest["sources"]!["stock.photo"]!["mediaKind"]!.GetValue<string>().ShouldBe("image");
        manifest["sources"]!["stock.photo"]!["canResolve"]!.GetValue<bool>().ShouldBeTrue();
        manifest["sources"]!["stock.photo"]!["searchable"]!.GetValue<bool>().ShouldBeTrue();
        ProviderIds(manifest, "stock.photo").ShouldContain("fake");
        var fake = manifest["sources"]!["stock.photo"]!["providers"]!.AsArray().Single(p => p!["id"]!.GetValue<string>() == "fake")!;
        fake["quota"]!["limit"]!.GetValue<int>().ShouldBe(2);
        fake["quota"]!["remaining"].ShouldBeNull();
        fake["filters"]!.AsArray().Count.ShouldBe(3);
        manifest["sources"]!["stock.audio"]!["mediaKind"]!.GetValue<string>().ShouldBe("audio");
    }

    [Fact]
    public async Task Manifest_HidesProvidersHiddenForTheDistribution()
    {
        await Manifest();
        await F.Sql("update provider_settings set hidden_for = ARRAY['android-play'] where provider = 'fake'");
        F.Factory.Services.GetRequiredService<ManifestCache>().Invalidate();

        ProviderIds(await Manifest("android-play"), "stock.photo").ShouldNotContain("fake");
        ProviderIds(await Manifest("appimage"), "stock.photo").ShouldContain("fake");
    }

    [Fact]
    public async Task Manifest_SourcesAreCachedUntilInvalidated()
    {
        await Manifest("windows");
        await F.Sql("update provider_settings set hidden_for = ARRAY['windows'] where provider = 'fake'");

        ProviderIds(await Manifest("windows"), "stock.photo").ShouldContain("fake");
        F.Factory.Services.GetRequiredService<ManifestCache>().Invalidate();
        ProviderIds(await Manifest("windows"), "stock.photo").ShouldNotContain("fake");
    }

    [Fact]
    public async Task Manifest_ListsEnabledCommunityKindsWithCategoriesAndSorts()
    {
        var manifest = await Manifest();

        var sources = manifest["sources"]!.AsObject().Select(s => s.Key).Where(k => k.StartsWith("community.")).ToList();
        sources.ShouldContain("community.effect");
        sources.ShouldNotContain("community.language-pack");
        var effect = manifest["sources"]!["community.effect"]!;
        effect["kind"]!.GetValue<string>().ShouldBe("community");
        effect["sorts"]!.AsArray().Select(s => s!.GetValue<string>()).ShouldBe(["top", "trending", "new", "downloads"]);
        effect["categories"]!.AsArray().Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Manifest_WithoutALayout_ServesTheSeedPrunedToExistingSources()
    {
        var pages = PageIds(await Manifest());

        pages.ShouldBe(["home", "sfx", "photo", "audio", "community"], ignoreOrder: true);
    }

    [Fact]
    public async Task Layout_SelectedByVersionRange()
    {
        await AddLayout("old", 10, OnePage("old"), max: "2.0.0");
        await AddLayout("new", 10, OnePage("new"), min: "2.0.0");

        PageIds(await Manifest(version: "1.9.9")).ShouldBe(["old"]);
        PageIds(await Manifest(version: "2.0.0")).ShouldBe(["new"]);
        PageIds(await Manifest(version: "2.5.0")).ShouldBe(["new"]);
    }

    [Fact]
    public async Task Layout_HighestPriorityWins_TieGoesToNewest_DisabledIgnored_DistributionTargeted()
    {
        var now = DateTimeOffset.UtcNow;
        await AddLayout("low", 1, OnePage("low"));
        await AddLayout("tie-old", 5, OnePage("tie-old"), at: now.AddHours(-2));
        await AddLayout("tie-new", 5, OnePage("tie-new"), at: now.AddHours(-1));
        await AddLayout("only-windows", 9, OnePage("windows"), distributions: ["windows"]);
        await AddLayout("off", 99, OnePage("off"));
        await F.Sql("update marketplace_layouts set enabled = false where name = 'off'");

        PageIds(await Manifest("appimage")).ShouldBe(["tie-new"]);
        PageIds(await Manifest("windows")).ShouldBe(["windows"]);
    }

    [Fact]
    public async Task Layout_InvalidStoredPages_FallsBackToTheNextLayout()
    {
        await AddLayout("broken", 10, """[{"id":"x"}]""");
        await AddLayout("fine", 1, OnePage("fine"));

        PageIds(await Manifest()).ShouldBe(["fine"]);
    }

    [Fact]
    public async Task Layout_PrunesMissingSourcesAndEmptyPages_AndDanglingLinks()
    {
        await AddLayout("custom", 10, """
            [
              {"id":"home","title":"Home","sections":[
                {"id":"gone","type":"shelf","title":"x","source":{"id":"stock.video"},"seeAll":{"page":"video"}},
                {"id":"photos","type":"shelf","title":"p","source":{"id":"stock.photo"},"seeAll":{"page":"video"}},
                {"id":"hero","type":"hero","title":"h","action":{"type":"open-page","page":"video"}}]},
              {"id":"video","title":"Video","sections":[{"id":"b","type":"stock-browser","source":{"id":"stock.video"}}]}
            ]
            """);

        var manifest = await Manifest();

        PageIds(manifest).ShouldBe(["home"]);
        var sections = manifest["pages"]![0]!["sections"]!.AsArray();
        sections.Select(s => s!["id"]!.GetValue<string>()).ShouldBe(["photos", "hero"]);
        sections[0]!.AsObject().ContainsKey("seeAll").ShouldBeFalse();
        sections[1]!.AsObject().ContainsKey("action").ShouldBeFalse();
    }

    [Fact]
    public async Task Flags_FoldByKey_MostSpecificThenNewestWins()
    {
        var now = DateTimeOffset.UtcNow;
        await AddFlag("a", true, at: now.AddHours(-3));
        await AddFlag("a", false, distributions: ["appimage"], at: now.AddHours(-5));
        await AddFlag("b", true, at: now.AddHours(-2));
        await AddFlag("b", false, at: now.AddHours(-1));
        await AddFlag("n", true, "{\"max\":5}");
        await AddFlag("n", true, min: "9.0.0");
        await AddFlag("w", true, distributions: ["windows"]);
        await AddFlag("v", true, min: "1.0.0", max: "1.2.3");

        var features = (await Manifest())["features"]!.AsObject();

        features["a"]!.GetValue<bool>().ShouldBeFalse();
        features["b"]!.GetValue<bool>().ShouldBeFalse();
        features["n"]!["max"]!.GetValue<int>().ShouldBe(5);
        features.ContainsKey("w").ShouldBeFalse();
        features.ContainsKey("v").ShouldBeFalse();
    }

    [Fact]
    public async Task Flag_ValueJsonWinsOverEnabled()
    {
        await AddFlag("limit", true, "12");

        (await Manifest())["features"]!["limit"]!.GetValue<int>().ShouldBe(12);
    }

    [Fact]
    public async Task CommunityFlag_RemovesCommunitySourcesAndPrunesSections()
    {
        await AddFlag("community", false);

        var manifest = await Manifest();

        manifest["sources"]!.AsObject().Select(s => s.Key).ShouldNotContain(k => k.StartsWith("community."));
        manifest["sources"]!.AsObject().ContainsKey("stock.photo").ShouldBeTrue();
        PageIds(manifest).ShouldBe(["sfx", "photo", "audio"], ignoreOrder: true);
    }

    [Fact]
    public async Task CommunityKindFlag_RemovesOnlyThatKind()
    {
        await AddFlag("community.look", false);

        var sources = (await Manifest())["sources"]!.AsObject();

        sources.ContainsKey("community.look").ShouldBeFalse();
        sources.ContainsKey("community.effect").ShouldBeTrue();
    }

    [Fact]
    public async Task SourceFlag_RemovesAnySource()
    {
        await AddFlag("source.stock.audio", false);

        var manifest = await Manifest();

        manifest["sources"]!.AsObject().ContainsKey("stock.audio").ShouldBeFalse();
        PageIds(manifest).ShouldNotContain("audio");
    }

    [Fact]
    public async Task Etag_Returns304_VaryAndCacheControl()
    {
        var first = await Client().SendAsync(Signed(), Ct);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var etag = first.Headers.ETag!.ToString();
        first.Headers.Vary.ShouldContain("X-Cutwire-Distribution");
        first.Headers.Vary.ShouldContain("X-Cutwire-App");
        first.Headers.CacheControl!.ToString().ShouldBe("no-cache, private");

        (await Client().SendAsync(Signed(ifNoneMatch: etag), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotModified);
        (await Client().SendAsync(Signed(ifNoneMatch: "W/\"other\""), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await AddFlag("x", true);
        (await Client().SendAsync(Signed(ifNoneMatch: etag), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private const string Items = Target + "/sources/";

    private Task<HttpResponseMessage> Send(HttpMethod method, string target, object? body = null, string distribution = "appimage", string version = "1.2.3") =>
        Client().SendAsync(SignedTo(method, target, body, distribution, version), Ct);

    private async Task<JsonNode> Get(string target, string distribution = "appimage", string version = "1.2.3") =>
        await Ok(await Send(HttpMethod.Get, target, null, distribution, version));

    private async Task<JsonNode> Poll(string target, Func<JsonNode, bool> done)
    {
        for (var i = 0; i < 100; i++)
        {
            var body = await Get(target);
            if (done(body)) return body;
            await Task.Delay(100, Ct);
        }

        throw new TimeoutException(target);
    }

    private static string[] Ids(JsonNode page) => page["items"]!.AsArray().Select(i => i!["id"]!.GetValue<string>()).ToArray();

    private static string Sha(byte[] b) => Convert.ToHexStringLower(SHA256.HashData(b));

    private static JsonObject DriftFile(string name, string content) =>
        new() { ["name"] = name, ["size"] = content.Length, ["sha256"] = Sha(Encoding.UTF8.GetBytes(content)) };

    private static JsonObject DriftFiles(string prefix, string main) =>
        new() { ["main"] = DriftFile(main + ".json", prefix), ["thumbnail"] = DriftFile("t.webp", prefix + "-t"), ["preview"] = DriftFile("p.webp", prefix + "-p") };

    private static JsonObject DriftAsset(string id, string name, string kind, string category, bool variants)
    {
        var asset = new JsonObject { ["id"] = id, ["name"] = name, ["kind"] = kind, ["category"] = category, ["path"] = category + "/" + id, ["files"] = DriftFiles(id + "-main", id) };
        if (id == "crate") asset["license"] = "CC0-1.0";
        if (id == "party-hat") asset["license"] = "Custom-1.0";
        if (variants) asset["variants"] = new JsonArray(new JsonObject { ["id"] = "gold", ["name"] = "Gold", ["files"] = DriftFiles(id + "-gold", "gold") });
        return asset;
    }

    private Task SeedDriftPack() => F.Db(async db =>
    {
        var index = new JsonObject
        {
            ["schema"] = 1,
            ["version"] = "1.0.0",
            ["categories"] = new JsonArray(
                new JsonObject { ["id"] = "memes", ["label"] = "Memes", ["kind"] = "lottie", ["sort"] = 1 },
                new JsonObject { ["id"] = "objects", ["label"] = "Objects", ["kind"] = "object", ["sort"] = 2 }),
            ["assets"] = new JsonArray(
                DriftAsset("party-hat", "Party hat", "lottie", "memes", true),
                DriftAsset("confetti", "Confetti", "lottie", "memes", false),
                DriftAsset("crate", "Wooden crate", "object", "objects", false)),
        };
        db.DriftAssetPacks.Add(new DriftAssetPack { Version = "1.0.0", IndexJson = index.ToJsonString(), FilesJson = "{}", IsCurrent = true, SyncedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(Ct);
        return 0;
    });

    [Fact]
    public async Task Items_UnknownSource_Is404WithCode()
    {
        var response = await Send(HttpMethod.Get, Items + "nope/items");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Json(Ct))["code"]!.GetValue<string>().ShouldBe("unknown_source");
        (await Send(HttpMethod.Get, Items + "nope/items/x")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Post, Target + "/installs", new { source = "nope", itemId = "x" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Items_UnauthenticatedWithoutSignatureOrSameOrigin_Is401()
    {
        (await Client().GetAsync(Items + "sfx/items", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Items_SourceHiddenByFlagIsUnknown()
    {
        await AddFlag("source.sfx", false);

        (await Send(HttpMethod.Get, Items + "sfx/items")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DriftAssets_PagesWithOpaqueCursorAndMapsItems()
    {
        await SeedDriftPack();

        var first = await Get(Items + "drift-assets/items?limit=2");
        Ids(first).ShouldBe(["party-hat", "confetti"]);
        var cursor = first["nextCursor"]!.GetValue<string>();
        cursor.ShouldNotContain("=");
        var second = await Get(Items + $"drift-assets/items?limit=2&cursor={cursor}");
        Ids(second).ShouldBe(["crate"]);
        second["nextCursor"].ShouldBeNull();

        var hat = first["items"]![0]!;
        hat["source"]!.GetValue<string>().ShouldBe("drift-assets");
        hat["title"]!.GetValue<string>().ShouldBe("Party hat");
        hat["mediaKind"]!.GetValue<string>().ShouldBe("lottie");
        hat["category"]!.GetValue<string>().ShouldBe("memes");
        hat["thumbUrl"]!.GetValue<string>().ShouldStartWith("http");
        hat["variants"]!.AsArray().Select(v => v!["id"]!.GetValue<string>()).ShouldBe(["gold"]);
        hat["license"]!["name"]!.GetValue<string>().ShouldBe("Custom-1.0");
        second["items"]![0]!["license"]!["name"]!.GetValue<string>().ShouldBe("CC0 1.0");
        second["items"]![0]!["license"]!["url"]!.GetValue<string>().ShouldBe("https://creativecommons.org/publicdomain/zero/1.0/");
        second["items"]![0]!["mediaKind"]!.GetValue<string>().ShouldBe("model3d");
        (await Send(HttpMethod.Get, Items + "drift-assets/items?cursor=garbage")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DriftAssets_SearchMatchesNameCategoryAndFiltersByCategory()
    {
        await SeedDriftPack();

        Ids(await Get(Items + "drift-assets/items?q=CRATE")).ShouldBe(["crate"]);
        Ids(await Get(Items + "drift-assets/items?q=memes")).ShouldBe(["party-hat", "confetti"]);
        Ids(await Get(Items + "drift-assets/items?q=party+hat")).ShouldBe(["party-hat"]);
        Ids(await Get(Items + "drift-assets/items?category=objects")).ShouldBe(["crate"]);
        Ids(await Get(Items + "drift-assets/items?q=zzz")).ShouldBeEmpty();
        (await Get(Items + "drift-assets/items/confetti"))["id"]!.GetValue<string>().ShouldBe("confetti");
        (await Send(HttpMethod.Get, Items + "drift-assets/items/missing")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DriftAssets_InstallReturnsEveryFileWithSha256AndVariantFiles()
    {
        await SeedDriftPack();

        var plain = await Ok(await Send(HttpMethod.Post, Target + "/installs", new { source = "drift-assets", itemId = "crate" }));
        plain["status"]!.GetValue<string>().ShouldBe("ready");
        plain["license"]!["name"]!.GetValue<string>().ShouldBe("CC0 1.0");
        var files = plain["files"]!.AsArray();
        files.Select(f => f!["role"]!.GetValue<string>()).ShouldBe(["main", "preview", "thumbnail"]);
        files[0]!["sha256"]!.GetValue<string>().ShouldBe(Sha(Encoding.UTF8.GetBytes("crate-main")));
        files[0]!["size"]!.GetValue<long>().ShouldBe("crate-main".Length);
        files[0]!["url"]!.GetValue<string>().ShouldStartWith("http");

        var gold = await Ok(await Send(HttpMethod.Post, Target + "/installs", new { source = "drift-assets", itemId = "party-hat", variantId = "gold" }));
        gold["files"]![0]!["sha256"]!.GetValue<string>().ShouldBe(Sha(Encoding.UTF8.GetBytes("party-hat-gold")));
        (await Send(HttpMethod.Post, Target + "/installs", new { source = "drift-assets", itemId = "party-hat", variantId = "nope" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sfx_PagesSearchesByNameTagAndCategory()
    {
        var first = await Get(Items + "sfx/items?limit=2");
        Ids(first).ShouldBe(["s1", "s2"]);
        Ids(await Get(Items + $"sfx/items?limit=2&cursor={first["nextCursor"]!.GetValue<string>()}")).ShouldBe(["s3"]);

        Ids(await Get(Items + "sfx/items?q=rain")).ShouldBe(["s2"]);
        Ids(await Get(Items + "sfx/items?q=clicks")).ShouldBe(["s1"]);
        Ids(await Get(Items + "sfx/items?q=nature")).ShouldBe(["s2", "s3"]);
        Ids(await Get(Items + "sfx/items?category=ui")).ShouldBe(["s1"]);
        Ids(await Get(Items + "sfx/items?category=ui/click")).ShouldBe(["s1"]);
        Ids(await Get(Items + "sfx/items?category=nature/weather")).ShouldBe(["s2"]);
        Ids(await Get(Items + "sfx/items?category=nature/empty")).ShouldBeEmpty();
        Ids(await Get(Items + "sfx/items?category=ui/weather")).ShouldBeEmpty();

        var click = first["items"]![0]!;
        click["mediaKind"]!.GetValue<string>().ShouldBe("audio");
        click["durationMs"]!.GetValue<int>().ShouldBe(250);
        click["tags"]!.AsArray().Select(t => t!.GetValue<string>()).ShouldBe(["Clicks"]);
        click["previewUrl"]!.GetValue<string>().ShouldStartWith("http");
    }

    [Fact]
    public async Task Sfx_ManifestListsSubcategoriesAsChildrenWithCategorySlashSubIds()
    {
        var categories = (await Manifest())["sources"]!["sfx"]!["categories"]!.AsArray();

        var ui = categories.Single(c => c!["id"]!.GetValue<string>() == "ui")!;
        ui["children"]!.AsArray().Select(c => c!["id"]!.GetValue<string>()).ShouldBe(["ui/click"]);
        var nature = categories.Single(c => c!["id"]!.GetValue<string>() == "nature")!;
        nature["children"]!.AsArray().Select(c => c!["id"]!.GetValue<string>()).ShouldBe(["nature/weather"]);
        nature["children"]![0]!["label"]!.GetValue<string>().ShouldBe("Weather");
    }

    [Fact]
    public async Task Sfx_PeaksPassThroughFromTheIndex_AndAreComputedInTheBackgroundOtherwise()
    {
        var rain = await Get(Items + "sfx/items/s2");
        rain["extra"]!["peaks"]!.GetValue<string>().ShouldBe(RainPeaks);

        JsonNode? click = null;
        for (var i = 0; i < 100 && click is null; i++)
        {
            var item = await Get(Items + "sfx/items/s1");
            if (item["extra"]?["peaks"] is not null) click = item;
            else await Task.Delay(50, Ct);
        }

        click.ShouldNotBeNull();
        Convert.FromBase64String(click["extra"]!["peaks"]!.GetValue<string>()).Length.ShouldBe(64);
        (await Get(Items + "sfx/items/s3"))["extra"].ShouldBeNull();
        Ids(await Get(Items + "sfx/items?limit=3"))[0].ShouldBe("s1");
    }

    [Fact]
    public async Task Sfx_InstallCarriesTheIndexSha256_OrOneComputedFromTheFile()
    {
        var rain = await Ok(await Send(HttpMethod.Post, Target + "/installs", new { source = "sfx", itemId = "s2" }));
        rain["files"]![0]!["sha256"]!.GetValue<string>().ShouldBe(RainSha);

        for (var i = 0; i < 100; i++)
        {
            var job = await Ok(await Send(HttpMethod.Post, Target + "/installs", new { source = "sfx", itemId = "s1" }));
            if (job["files"]![0]!["sha256"] is { } sha)
            {
                sha.GetValue<string>().ShouldBe(Sha(Encoding.UTF8.GetBytes("FLACDATA-0123456789")));
                return;
            }

            await Task.Delay(50, Ct);
        }

        Assert.Fail("sha256 was never computed");
    }

    [Fact]
    public async Task Sfx_InstallHasOneMainFile_AndSignedFetchOfItsBytesWorks()
    {
        var job = await Ok(await Send(HttpMethod.Post, Target + "/installs", new { source = "sfx", itemId = "s1" }));

        var file = job["files"]!.AsArray().Single()!;
        file["role"]!.GetValue<string>().ShouldBe("main");
        file["name"]!.GetValue<string>().ShouldBe("click one.flac");
        file["mime"]!.GetValue<string>().ShouldBe("audio/flac");
        file["size"]!.GetValue<long>().ShouldBe(19);

        var bytes = await Send(HttpMethod.Get, PathOf(file["url"]!));
        bytes.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await bytes.Content.ReadAsStringAsync(Ct)).ShouldBe("FLACDATA-0123456789");
    }

    [Fact]
    public async Task Thumbnails_AndPreviews_AreFetchableWithSignedHeaders()
    {
        var item = (await Get(Items + "sfx/items/s1"));

        var preview = await Send(HttpMethod.Get, PathOf(item["previewUrl"]!));

        preview.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await preview.Content.ReadAsStringAsync(Ct)).ShouldBe("OPUSDATA");
        var head = await Client().SendAsync(SignedTo(HttpMethod.Head, PathOf(item["previewUrl"]!)), Ct);
        head.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Stock_SingleProviderQueryReturnsQuotaAndPassesFiltersThrough()
    {
        var page = await Get(Items + "stock.photo/items?provider=fake&q=p1&orientation=landscape&hd=1&bogus=x");

        Ids(page).Length.ShouldBe(1);
        page["quota"]!["limit"]!.GetValue<int>().ShouldBe(2);
        page["quota"]!["remaining"]!.GetValue<int>().ShouldBe(2);
        var item = page["items"]![0]!;
        item["source"]!.GetValue<string>().ShouldBe("stock.photo");
        item["mediaKind"]!.GetValue<string>().ShouldBe("image");
        item["subtitle"]!.GetValue<string>().ShouldBe("Ada");
        item["thumbUrl"]!.GetValue<string>().ShouldStartWith("http");
        item["thumbUrl"]!.GetValue<string>().ShouldNotContain("secret-origin");
        item["extra"]!["provider"]!.GetValue<string>().ShouldBe("fake");
        Fake.Queries.Last().Filters.ShouldContainKeyAndValue("orientation", "landscape");
        Fake.Queries.Last().Filters.ShouldContainKeyAndValue("hd", "1");
        Fake.Queries.Last().Filters.ShouldNotContainKey("bogus");
        (await Send(HttpMethod.Get, Items + "stock.photo/items?provider=nobody")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Stock_FanOutInterleavesUnmeteredProvidersSkipsFailuresAndRoundTripsTheCursor()
    {
        var page = await Get(Items + "stock.photo/items?limit=24&orientation=portrait");

        var providers = page["items"]!.AsArray().Select(i => i!["extra"]!["provider"]!.GetValue<string>()).ToArray();
        providers.Length.ShouldBe(6);
        providers.Distinct().Order().ShouldBe(["fakeb", "fakec"]);
        providers.Zip(providers.Skip(1)).ShouldAllBe(pair => pair.First != pair.Second);
        page["warnings"]!.AsArray().Select(w => w!.GetValue<string>()).ShouldBe(["fakex:stock.source_blocked"]);
        page["quota"].ShouldBeNull();
        Beta.Queries.Last().Limit.ShouldBe(8);
        Beta.Queries.Last().Filters.ShouldContainKeyAndValue("orientation", "portrait");

        var cursor = page["nextCursor"]!.GetValue<string>();
        var state = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(cursor.Replace('-', '+').Replace('_', '/').PadRight((cursor.Length + 3) / 4 * 4, '='))))!.AsObject();
        state.Select(kv => kv.Key).Order().ShouldBe(["fakeb", "fakec"]);
        state["fakeb"]!.GetValue<string>().ShouldBe("2");

        var second = await Get(Items + $"stock.photo/items?limit=24&cursor={cursor}");
        Beta.Queries.Last().Cursor.ShouldBe("2");
        Gamma.Queries.Last().Cursor.ShouldBe("2");
        second["nextCursor"].ShouldBeNull();
        (await Send(HttpMethod.Get, Items + "stock.photo/items?cursor=garbage")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Stock_FanOutSkipsMeteredProvidersAndExplicitStarIsTheSame()
    {
        var page = await Get(Items + "stock.photo/items?provider=*");

        page["items"]!.AsArray().Select(i => i!["extra"]!["provider"]!.GetValue<string>()).ShouldNotContain("fake");
    }

    [Fact]
    public async Task Stock_GetItemChecksTheSourceType()
    {
        var id = ItemId("fake", "photo", "p2");

        (await Get(Items + $"stock.photo/items/{id}"))["title"]!.GetValue<string>().ShouldBe("Item p2");
        (await Send(HttpMethod.Get, Items + $"stock.audio/items/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Stock_InstallImageIsReadyAtOnce_QuotaIsEnforced_FileFetchableWithSignedHeaders()
    {
        var itemId = ItemId("fake", "photo", "p1");

        var job = await Ok(await Send(HttpMethod.Post, Target + "/installs", new { source = "stock.photo", itemId }));

        job["status"]!.GetValue<string>().ShouldBe("ready");
        job["id"]!.GetValue<string>().ShouldStartWith("stock_");
        job["source"]!.GetValue<string>().ShouldBe("stock.photo");
        var file = job["files"]!.AsArray().Single()!;
        file["sha256"]!.GetValue<string>().Length.ShouldBe(64);
        (await Send(HttpMethod.Get, PathOf(file["url"]!))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Get(Target + "/installs/" + job["id"]!.GetValue<string>()))["status"]!.GetValue<string>().ShouldBe("ready");

        (await Send(HttpMethod.Post, Target + "/installs", new { source = "stock.photo", itemId })).StatusCode.ShouldBe(HttpStatusCode.Created);
        var third = await Send(HttpMethod.Post, Target + "/installs", new { source = "stock.photo", itemId });
        third.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await third.Json(Ct))["code"]!.GetValue<string>().ShouldBe("stock.quota_exceeded");
    }

    [Fact]
    public async Task Stock_InstallAudioIsAJobThatPollingCompletes()
    {
        var created = await Ok(await Send(HttpMethod.Post, Target + "/installs", new { source = "stock.audio", itemId = ItemId("fakeaudio", "audio", "p1") }));
        var id = created["id"]!.GetValue<string>();

        var done = await Poll(Target + "/installs/" + id, j => j["status"]!.GetValue<string>() is "ready" or "failed");

        done["status"]!.GetValue<string>().ShouldBe("ready");
        done["source"]!.GetValue<string>().ShouldBe("stock.audio");
        done["files"]![0]!["mime"]!.GetValue<string>().ShouldBe("audio/wav");
        (await Send(HttpMethod.Post, Target + "/installs", new { source = "stock.photo", itemId = ItemId("fakeaudio", "audio", "p1") })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Install_UnknownIdsAndBadBodies()
    {
        (await Send(HttpMethod.Get, Target + "/installs/ready_abc")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Get, Target + "/installs/stock_" + Guid.NewGuid())).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Post, Target + "/installs", new { source = "sfx" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Send(HttpMethod.Post, Target + "/installs", new { source = "sfx", itemId = "nope" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Resolve_StartsAJobAndPollingYieldsAMarketItem()
    {
        var started = await Send(HttpMethod.Post, Target + "/resolve", new { url = "https://fake.example/photo/1" });
        started.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var id = (await started.Json(Ct))["id"]!.GetValue<string>();

        var job = await Poll(Target + "/resolve/" + id, j => j["status"]!.GetValue<string>() is "ready" or "failed");

        job["status"]!.GetValue<string>().ShouldBe("ready");
        job["item"]!["source"]!.GetValue<string>().ShouldBe("stock.photo");
        job["item"]!["thumbUrl"]!.GetValue<string>().ShouldStartWith("http");
        (await Send(HttpMethod.Get, Target + "/resolve/nothing")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Resolve_IsUnavailableWhenTheFlagIsOff()
    {
        await AddFlag("resolve", false);

        var response = await Send(HttpMethod.Post, Target + "/resolve", new { url = "https://fake.example/photo/1" });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Json(Ct))["code"]!.GetValue<string>().ShouldBe("resolve_unavailable");
    }

    private async Task<(string Id, string Sha)> PublishCommunityEffect(string title, string? minAppVersion = null)
    {
        var (client, _, _) = await F.User();
        var item = await F.PublishEffect(client, title);
        var id = item["id"]!.GetValue<string>();
        if (minAppVersion is not null) await F.Sql($"update item_versions set min_app_version = '{minAppVersion}' where item_id = '{id}'");
        return (id, item["versions"]![0]!["sha256"]!.GetValue<string>());
    }

    [Fact]
    public async Task Community_ListingExcludesItemsNeedingANewerApp_AndMapsFields()
    {
        var (fits, _) = await PublishCommunityEffect("Fits glow");
        var (tooNew, _) = await PublishCommunityEffect("Too new glow", "9.0.0");

        Ids(await Get(Items + "community.effect/items?sort=new", version: "1.2.3")).ShouldBe([fits]);
        Ids(await Get(Items + "community.effect/items?sort=new", version: "9.0.0")).ShouldBe([tooNew, fits]);

        var web = Client();
        web.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
        Ids(await Ok(await web.GetAsync(Items + "community.effect/items?sort=new", Ct))).ShouldBe([tooNew, fits]);

        var item = (await Get(Items + "community.effect/items?q=fits"))["items"]![0]!;
        item["source"]!.GetValue<string>().ShouldBe("community.effect");
        item["webUrl"]!.GetValue<string>().ShouldEndWith("/community/effect/" + item["extra"]!["slug"]!.GetValue<string>());
        item["category"]!.GetValue<string>().ShouldBe("dreamy");
        item["extra"]!["downloads"]!.GetValue<long>().ShouldBe(0);
        item["extra"]!["owner"]!.GetValue<string>().ShouldNotBeNullOrEmpty();
        (await Get(Items + "community.effect/items?category=nothing")).ShouldNotBeNull();
        Ids(await Get(Items + "community.effect/items?category=nothing")).ShouldBeEmpty();
        (await Send(HttpMethod.Get, Items + "community.effect/items?sort=sideways")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await Get(Items + $"community.effect/items/{fits}"))["extra"]!["minAppVersion"]!.GetValue<string>().ShouldBe("0.7.0");
        (await Send(HttpMethod.Get, Items + $"community.look/items/{fits}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Get, Items + "community.effect/items/not-a-guid")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Community_InstallRecordsADownloadForTheClientAndReturnsTheSha()
    {
        var (id, sha) = await PublishCommunityEffect("Install me glow");

        var job = await Ok(await Send(HttpMethod.Post, Target + "/installs", new { source = "community.effect", itemId = id }));

        job["status"]!.GetValue<string>().ShouldBe("ready");
        job["source"]!.GetValue<string>().ShouldBe("community.effect");
        job["minAppVersion"]!.GetValue<string>().ShouldBe("0.7.0");
        var file = job["files"]!.AsArray().Single()!;
        file["role"]!.GetValue<string>().ShouldBe("main");
        file["sha256"]!.GetValue<string>().ShouldBe(sha);
        file["url"]!.GetValue<string>().ShouldContain("/files/" + sha);
        file["name"]!.GetValue<string>().ShouldEndWith(".driftfx");
        file["size"]!.GetValue<long>().ShouldBeGreaterThan(0);
        var itemGuid = Guid.Parse(id);
        var installId = "c:" + ClientId[..62];
        (await F.Db(db => db.DownloadEvents.CountAsync(e => e.ItemId == itemGuid && e.InstallId == installId, Ct))).ShouldBe(1);
        (await Send(HttpMethod.Get, PathOf(file["url"]!))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Send(HttpMethod.Post, Target + "/installs", new { source = "community.look", itemId = id })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Community_InstallIsRefusedWith409WhenTheAppIsTooOld()
    {
        var (id, _) = await PublishCommunityEffect("Needs new glow", "9.0.0");

        var response = await Send(HttpMethod.Post, Target + "/installs", new { source = "community.effect", itemId = id });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await response.Json(Ct);
        problem["code"]!.GetValue<string>().ShouldBe("app_too_old");
        problem["minAppVersion"]!.GetValue<string>().ShouldBe("9.0.0");
        (await F.Db(db => db.DownloadEvents.CountAsync(Ct))).ShouldBe(0);
        (await Send(HttpMethod.Post, Target + "/installs", new { source = "community.effect", itemId = id }, version: "9.0.0")).StatusCode.ShouldBe(HttpStatusCode.Created);
    }
}
