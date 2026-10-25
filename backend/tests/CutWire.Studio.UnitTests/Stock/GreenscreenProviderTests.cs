using System.Net;
using CutWire.Studio.Application.Stock;
using CutWire.Studio.Infrastructure.Stock;
using CutWire.Studio.Infrastructure.Stock.Providers;
using Microsoft.Extensions.Options;
using Shouldly;

namespace CutWire.Studio.UnitTests.Stock;

public class GreenscreenProviderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PexelsProvider Pexels(FakeHandler h) =>
        new(h.Client(), new NullProviderCache(), Options.Create(new StockOptions()), Options.Create(new PexelsOptions { ApiKey = "K" }));

    private static ProviderQuery Query(string? q) => new("greenscreen", q, 1, null, new Dictionary<string, string> { ["orientation"] = "landscape" });

    [Fact]
    public void Descriptor_is_distinct_id_keeps_label_and_serves_only_greenscreen()
    {
        var d = new GreenscreenProvider(Pexels(new FakeHandler()), "green screen").Descriptor;
        d.Id.ShouldBe("pexels_greenscreen");
        d.Label.ShouldBe("Pexels");
        d.Serving("greenscreen").ShouldNotBeNull();
        d.Serving("video").ShouldBeNull();
        d.Serving("greenscreen")!.Filters!.Select(f => f.Id).ShouldContain("orientation");
        d.Capabilities.ShouldBe(ProviderCapabilities.Search | ProviderCapabilities.Fulfill);
    }

    [Fact]
    public async Task Search_prepends_keywords_queries_video_and_retypes_items()
    {
        var h = new FakeHandler().Json("/videos/search", "pexels/videos.json");
        var page = await new GreenscreenProvider(Pexels(h), "green screen").SearchAsync(Query("explosion"), Ct);

        h.Last.Uri.AbsolutePath.ShouldBe("/videos/search");
        h.Last.Query["query"].ShouldBe("green screen explosion");
        h.Last.Query["orientation"].ShouldBe("landscape");
        page.Items.ShouldHaveSingleItem().Type.ShouldBe("greenscreen");
        page.Items[0].Native.ShouldBe("3195394");
    }

    [Fact]
    public async Task Empty_query_searches_the_keywords_alone()
    {
        var h = new FakeHandler().Json("/videos/search", "pexels/videos.json");
        await new GreenscreenProvider(Pexels(h), "green screen").SearchAsync(Query(null), Ct);
        h.Last.Query["query"].ShouldBe("green screen");
    }

    [Fact]
    public async Task GetItem_and_Fetch_round_trip_native_ids_to_the_inner_video_type()
    {
        using var ws = new TempWorkspace();
        var h = new FakeHandler().Json("/videos/videos/3195394", "pexels/video.json").Bytes("/files/", [1]);
        var p = new GreenscreenProvider(Pexels(h), "green screen");

        var item = await p.GetItemAsync("greenscreen", "3195394", Ct);
        item!.Type.ShouldBe("greenscreen");
        item.Variants.Count.ShouldBe(3);

        var asset = await p.FetchAsync(new FulfillRequest("greenscreen", "3195394", "103"), ws.Create(h.Client()), Ct);
        h.Last.Uri.ToString().ShouldContain("103-hd.mp4");
        asset.Mime.ShouldBe("video/mp4");
        asset.Attribution.ShouldBe("Ruvim Miksanskiy on Pexels");
    }

    [Fact]
    public async Task Wraps_pixabay_too()
    {
        var h = new FakeHandler().Json("/api/videos/", "pixabay/videos.json");
        var inner = new PixabayProvider(h.Client(), new NullProviderCache(), Options.Create(new StockOptions()), Options.Create(new PixabayOptions { ApiKey = "PK" }));
        var p = new GreenscreenProvider(inner, "green screen");

        p.Descriptor.Id.ShouldBe("pixabay_greenscreen");
        var page = await p.SearchAsync(Query("smoke"), Ct);
        h.Last.Query["q"].ShouldBe("green screen smoke");
        page.Items.ShouldHaveSingleItem().Type.ShouldBe("greenscreen");
    }

    [Fact]
    public async Task Inner_errors_propagate()
    {
        var h = new FakeHandler().Status("/videos/", HttpStatusCode.TooManyRequests);
        var p = new GreenscreenProvider(Pexels(h), "green screen");
        (await Should.ThrowAsync<StockException>(() => p.SearchAsync(Query("x"), Ct))).Reason.ShouldBe(StockReason.SourceBlocked);
    }
}
