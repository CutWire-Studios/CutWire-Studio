using System.Net;
using System.Text.Json.Nodes;
using CutWire.Studio.Api.Http;
using CutWire.Studio.Application.Marketplace;
using CutWire.Studio.Domain.Marketplace;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace CutWire.Studio.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class MarketplaceTests(PostgresFixture postgres) : StockTestBase(postgres, extra: b => b.UseSetting("DriftClient:Keys:0", Key))
{
    private const string Key = "integration-drift-key";
    private const string Target = "/api/v1/marketplace";
    private static readonly string ClientId = new('b', 64);

    private static HttpRequestMessage Signed(string distribution = "appimage", string version = "1.2.3", string? ifNoneMatch = null)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonce = Guid.NewGuid().ToString("N");
        var request = new HttpRequestMessage(HttpMethod.Get, Target);
        request.Headers.Add("X-Cutwire-Client", ClientId);
        request.Headers.Add("X-Cutwire-Timestamp", timestamp);
        request.Headers.Add("X-Cutwire-Nonce", nonce);
        request.Headers.Add("X-Cutwire-App", $"Drift/{version} (linux)");
        request.Headers.Add("X-Cutwire-Distribution", distribution);
        request.Headers.Add("X-Cutwire-Signature", DriftClientAuth.SignatureFor(Key, timestamp, nonce, ClientId, "GET", Target, []));
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

        pages.ShouldBe(["home", "photo", "audio", "community"], ignoreOrder: true);
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
                {"id":"gone","type":"shelf","title":"x","source":{"id":"sfx"},"seeAll":{"page":"sfx"}},
                {"id":"photos","type":"shelf","title":"p","source":{"id":"stock.photo"},"seeAll":{"page":"sfx"}},
                {"id":"hero","type":"hero","title":"h","action":{"type":"open-page","page":"sfx"}}]},
              {"id":"sfx","title":"Sfx","sections":[{"id":"b","type":"sfx-browser","source":{"id":"sfx"}}]}
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
        PageIds(manifest).ShouldBe(["photo", "audio"], ignoreOrder: true);
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
}
