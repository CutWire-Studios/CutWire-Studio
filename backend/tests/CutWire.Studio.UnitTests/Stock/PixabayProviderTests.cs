using System.Net;
using CutWire.Studio.Application.Stock;
using CutWire.Studio.Infrastructure.Stock;
using CutWire.Studio.Infrastructure.Stock.Providers;
using Microsoft.Extensions.Options;
using Shouldly;

namespace CutWire.Studio.UnitTests.Stock;

public class PixabayProviderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PixabayProvider Create(FakeHandler h) =>
        new(h.Client(), new NullProviderCache(), Options.Create(new StockOptions()), Options.Create(new PixabayOptions { ApiKey = "PK" }));

    private static ProviderQuery Query(string type, string? q, int limit = 2, string? cursor = null, params (string, string)[] filters) =>
        new(type, q, limit, cursor, filters.ToDictionary(f => f.Item1, f => f.Item2));

    private sealed class RecordingCache : IProviderCache
    {
        public TimeSpan? Ttl;
        public Task<string?> GetAsync(string key, CancellationToken ct) => Task.FromResult<string?>(null);

        public Task SetAsync(string key, string body, TimeSpan ttl, CancellationToken ct)
        {
            Ttl = ttl;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Search_photos_maps_items_and_params()
    {
        var h = new FakeHandler().Json("pixabay.com/api/", "pixabay/images.json");
        var page = await Create(h).SearchAsync(Query("photo", "flower", 2, null, ("orientation", "horizontal"), ("category", "nature"), ("editors_choice", "true")), Ct);

        h.Last.Uri.AbsolutePath.ShouldBe("/api/");
        h.Last.Query["key"].ShouldBe("PK");
        h.Last.Query["q"].ShouldBe("flower");
        h.Last.Query["image_type"].ShouldBe("photo");
        h.Last.Query["safesearch"].ShouldBe("true");
        h.Last.Query["orientation"].ShouldBe("horizontal");
        h.Last.Query["category"].ShouldBe("nature");
        h.Last.Query["editors_choice"].ShouldBe("true");
        h.Last.Query["per_page"].ShouldBe("3");

        page.Items.Count.ShouldBe(2);
        var a = page.Items[0];
        a.Native.ShouldBe("195893");
        a.Title.ShouldBe("Blossom, bloom, flower");
        a.Creator!.Name.ShouldBe("Josch13");
        a.Creator.Url.ShouldBe("https://pixabay.com/users/Josch13-48777/");
        a.License!.Name.ShouldBe("Pixabay Content License");
        a.ThumbSrc.ShouldContain("_150");
        page.Items[1].Title.ShouldBe("Untitled");
        page.Items[1].Creator.ShouldBeNull();
        page.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task Search_without_query_omits_q_and_toggle_off_omits_editors_choice()
    {
        var h = new FakeHandler().Json("/api/videos/", "pixabay/videos.json");
        await Create(h).SearchAsync(Query("video", null, 30, null, ("video_type", "film"), ("editors_choice", "")), Ct);

        h.Last.Uri.AbsolutePath.ShouldBe("/api/videos/");
        h.Last.Query["q"].ShouldBeNull();
        h.Last.Query["editors_choice"].ShouldBeNull();
        h.Last.Query["video_type"].ShouldBe("film");
        h.Last.Query["image_type"].ShouldBeNull();
    }

    [Fact]
    public async Task Search_videos_maps_renditions_sorted_widest_first()
    {
        var h = new FakeHandler().Json("/api/videos/", "pixabay/videos.json");
        var page = await Create(h).SearchAsync(Query("video", "flowers", 1), Ct);

        var item = page.Items.ShouldHaveSingleItem();
        item.Type.ShouldBe("video");
        item.Title.ShouldBe("Flowers, yellow, blossom");
        item.DurationMs.ShouldBe(12000);
        item.Width.ShouldBe(1920);
        item.Variants.Select(v => v.Id).ShouldBe(["large", "medium", "small"]);
        item.Variants[0].Label.ShouldBe("1920x1080");
        item.Variants[0].Size.ShouldBe(9000);
        item.PreviewSrc.ShouldEndWith("125-small.mp4");
        item.ThumbSrc.ShouldEndWith("125-large.jpg");
    }

    [Fact]
    public async Task GetItem_and_empty_hits()
    {
        var h = new FakeHandler().Json("id=195893", "pixabay/images.json").Json("id=0", "pixabay/empty.json");
        var p = Create(h);
        (await p.GetItemAsync("photo", "195893", Ct))!.Native.ShouldBe("195893");
        h.Last.Query["id"].ShouldBe("195893");

        (await Should.ThrowAsync<StockException>(() => p.GetItemAsync("video", "0", Ct))).Reason.ShouldBe(StockReason.ItemMissing);
    }

    [Fact]
    public async Task Fetch_photo_prefers_fullhd_then_large()
    {
        using var ws = new TempWorkspace();
        var h = new FakeHandler().Json("id=195893", "pixabay/images.json").Bytes("/get/", [5]);
        var asset = await Create(h).FetchAsync(new FulfillRequest("photo", "195893", null), ws.Create(h.Client()), Ct);

        h.Last.Uri.ToString().ShouldBe("https://pixabay.com/get/flower_1280.jpg");
        asset.Mime.ShouldBe("image/jpeg");
        asset.Filename.ShouldBe("Blossom__bloom__flower.jpg");
        asset.Attribution.ShouldBe("Josch13 on Pixabay");
        asset.License!.Url.ShouldBe("https://pixabay.com/service/license-summary/");
    }

    [Fact]
    public async Task Fetch_video_honours_variant_and_rejects_unknown()
    {
        using var ws = new TempWorkspace();
        var h = new FakeHandler().Json("id=125", "pixabay/videos.json").Bytes("cdn.pixabay.com/video", [5, 5]);
        var p = Create(h);

        var best = await p.FetchAsync(new FulfillRequest("video", "125", null), ws.Create(h.Client()), Ct);
        h.Last.Uri.ToString().ShouldEndWith("125-large.mp4");
        best.Duration.ShouldBe(TimeSpan.FromSeconds(12));
        best.Mime.ShouldBe("video/mp4");

        await p.FetchAsync(new FulfillRequest("video", "125", "small"), ws.Create(h.Client()), Ct);
        h.Last.Uri.ToString().ShouldEndWith("125-small.mp4");

        (await Should.ThrowAsync<StockException>(() => p.FetchAsync(new FulfillRequest("video", "125", "tiny"), ws.Create(h.Client()), Ct)))
            .Reason.ShouldBe(StockReason.ItemMissing);
    }

    [Fact]
    public async Task Cache_ttl_is_at_least_24_hours()
    {
        var h = new FakeHandler().Json("/api/", "pixabay/images.json");
        var cache = new RecordingCache();
        var p = new PixabayProvider(h.Client(), cache, Options.Create(new StockOptions { SearchCacheTtl = TimeSpan.FromMinutes(5) }), Options.Create(new PixabayOptions { ApiKey = "PK" }));
        await p.SearchAsync(Query("photo", "x"), Ct);
        cache.Ttl.ShouldBe(TimeSpan.FromHours(24));

        var longer = new PixabayProvider(h.Client(), cache, Options.Create(new StockOptions { SearchCacheTtl = TimeSpan.FromDays(7) }), Options.Create(new PixabayOptions { ApiKey = "PK" }));
        await longer.SearchAsync(Query("photo", "x"), Ct);
        cache.Ttl.ShouldBe(TimeSpan.FromDays(7));
    }

    [Fact]
    public async Task Errors_map_to_reasons()
    {
        var notFound = new FakeHandler().Status("/api/", HttpStatusCode.NotFound);
        (await Should.ThrowAsync<StockException>(() => Create(notFound).SearchAsync(Query("photo", "x"), Ct))).Reason.ShouldBe(StockReason.ItemMissing);

        var limited = new FakeHandler().Status("/api/", HttpStatusCode.TooManyRequests);
        (await Should.ThrowAsync<StockException>(() => Create(limited).SearchAsync(Query("video", "x"), Ct))).Reason.ShouldBe(StockReason.SourceBlocked);
    }

    [Fact]
    public void Descriptor_has_toggle_and_select_filters()
    {
        var d = Create(new FakeHandler()).Descriptor;
        d.Serving("photo")!.Filters!.Single(f => f.Id == "editors_choice").Kind.ShouldBe(FilterKinds.Toggle);
        d.Serving("video")!.Filters!.Single(f => f.Id == "category").Options!.Count.ShouldBe(20);
    }
}
