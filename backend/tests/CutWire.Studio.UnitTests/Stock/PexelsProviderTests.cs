using System.Net;
using CutWire.Studio.Application.Stock;
using CutWire.Studio.Infrastructure.Stock;
using CutWire.Studio.Infrastructure.Stock.Providers;
using Microsoft.Extensions.Options;
using Shouldly;

namespace CutWire.Studio.UnitTests.Stock;

public class PexelsProviderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PexelsProvider Create(FakeHandler h) =>
        new(h.Client(c => c.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "KEY")), new NullProviderCache(),
            Options.Create(new StockOptions()), Options.Create(new PexelsOptions { ApiKey = "KEY" }));

    private static ProviderQuery Query(string type, string? q, int limit = 2, string? cursor = null, params (string, string)[] filters) =>
        new(type, q, limit, cursor, filters.ToDictionary(f => f.Item1, f => f.Item2));

    [Fact]
    public async Task Search_photos_maps_items_and_filters()
    {
        var h = new FakeHandler().Json("/v1/search", "pexels/photos.json");
        var page = await Create(h).SearchAsync(Query("photo", "ocean", 2, null, ("orientation", "landscape"), ("size", "large"), ("color", "blue")), Ct);

        h.Last.Uri.AbsolutePath.ShouldBe("/v1/search");
        h.Last.Query["query"].ShouldBe("ocean");
        h.Last.Query["per_page"].ShouldBe("2");
        h.Last.Query["page"].ShouldBe("1");
        h.Last.Query["orientation"].ShouldBe("landscape");
        h.Last.Query["size"].ShouldBe("large");
        h.Last.Query["color"].ShouldBe("blue");
        h.Last.Headers["Authorization"].ShouldBe("KEY");

        page.NextCursor.ShouldBe("2");
        page.Items.Count.ShouldBe(2);
        var first = page.Items[0];
        first.Native.ShouldBe("1108099");
        first.Type.ShouldBe("photo");
        first.Title.ShouldBe("Two dogs running on green grass");
        first.Creator!.Name.ShouldBe("Chevanon Photography");
        first.Creator.Url.ShouldBe("https://www.pexels.com/@chevanon");
        first.Width.ShouldBe(4000);
        first.ThumbSrc.ShouldEndWith("w=650");
        first.PreviewMime.ShouldBe("image/jpeg");
        first.License!.Name.ShouldBe("Pexels License");
        page.Items[1].Title.ShouldBe("Golden sunset over the sea");
    }

    [Fact]
    public async Task Search_photos_empty_query_uses_curated_and_stops_on_short_page()
    {
        var h = new FakeHandler().Json("/v1/curated", "pexels/photos.json");
        var page = await Create(h).SearchAsync(Query("photo", "", 5, "3"), Ct);

        h.Last.Uri.AbsolutePath.ShouldBe("/v1/curated");
        h.Last.Query["page"].ShouldBe("3");
        h.Last.Query["query"].ShouldBeNull();
        page.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task Search_videos_maps_variants_preview_and_duration_range()
    {
        var h = new FakeHandler().Json("/videos/search", "pexels/videos.json");
        var page = await Create(h).SearchAsync(Query("video", "waves", 1, null, ("duration", "10-60")), Ct);

        h.Last.Query["min_duration"].ShouldBe("10");
        h.Last.Query["max_duration"].ShouldBe("60");
        var item = page.Items.ShouldHaveSingleItem();
        item.Type.ShouldBe("video");
        item.Title.ShouldBe("Waves crashing on the beach");
        item.DurationMs.ShouldBe(15000);
        item.Variants.Select(v => v.Id).ShouldBe(["102", "103", "101"]);
        item.Variants[0].Label.ShouldBe("3840x2160");
        item.Variants[0].Mime.ShouldBe("video/mp4");
        item.Variants[0].Size.ShouldBe(90000);
        item.PreviewSrc.ShouldBe("https://videos.pexels.com/files/101-sd.mp4");
        item.PreviewMime.ShouldBe("video/mp4");
        page.NextCursor.ShouldBe("2");
    }

    [Fact]
    public async Task Search_videos_featured_and_unparsable_duration_ignored()
    {
        var h = new FakeHandler().Json("/videos/popular", "pexels/videos.json");
        await Create(h).SearchAsync(Query("video", null, 1, null, ("duration", "soon")), Ct);

        h.Last.Uri.AbsolutePath.ShouldBe("/videos/popular");
        h.Last.Query["min_duration"].ShouldBeNull();
        h.Last.Query["max_duration"].ShouldBeNull();
    }

    [Fact]
    public async Task Search_videos_open_ended_range_sends_only_one_bound()
    {
        var h = new FakeHandler().Json("/videos/search", "pexels/videos.json");
        await Create(h).SearchAsync(Query("video", "x", 1, null, ("duration", "30-")), Ct);

        h.Last.Query["min_duration"].ShouldBe("30");
        h.Last.Query["max_duration"].ShouldBeNull();
    }

    [Fact]
    public void Descriptor_declares_types_filters_and_capabilities()
    {
        var d = Create(new FakeHandler()).Descriptor;
        d.Id.ShouldBe("pexels");
        d.Capabilities.ShouldBe(ProviderCapabilities.Search | ProviderCapabilities.Fulfill);
        d.Serving("photo")!.Filters!.Select(f => f.Id).ShouldBe(["orientation", "size", "color"]);
        d.Serving("photo")!.Filters![2].Kind.ShouldBe(FilterKinds.Select);
        d.Serving("video")!.Filters!.Single(f => f.Id == "duration").Kind.ShouldBe(FilterKinds.Range);
    }

    [Fact]
    public async Task GetItem_photo_and_video()
    {
        var h = new FakeHandler().Json("/v1/photos/1108099", "pexels/photo.json").Json("/videos/videos/3195394", "pexels/video.json");
        var p = Create(h);

        (await p.GetItemAsync("photo", "1108099", Ct))!.Native.ShouldBe("1108099");
        (await p.GetItemAsync("video", "3195394", Ct))!.Variants.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Fetch_photo_downloads_original_with_attribution()
    {
        using var ws = new TempWorkspace();
        var h = new FakeHandler().Json("/v1/photos/1108099", "pexels/photo.json").Bytes("pexels-photo-1108099.jpeg", [1, 2, 3]);
        var asset = await Create(h).FetchAsync(new FulfillRequest("photo", "1108099", null), ws.Create(h.Client()), Ct);

        asset.Mime.ShouldBe("image/jpeg");
        asset.Filename.ShouldBe("Two_dogs_running_on_green_grass.jpg");
        asset.License!.Name.ShouldBe("Pexels License");
        asset.Attribution.ShouldBe("Chevanon Photography on Pexels");
        File.ReadAllBytes(asset.Path).ShouldBe([1, 2, 3]);
        h.Seen.Last().Uri.AbsolutePath.ShouldContain("pexels-photo-1108099.jpeg");
    }

    [Fact]
    public async Task Fetch_video_defaults_to_widest_and_honours_variant()
    {
        using var ws = new TempWorkspace();
        var h = new FakeHandler().Json("/videos/videos/3195394", "pexels/video.json").Bytes("/files/", [9]);
        var p = Create(h);

        var best = await p.FetchAsync(new FulfillRequest("video", "3195394", null), ws.Create(h.Client()), Ct);
        h.Seen.Last().Uri.ToString().ShouldContain("102-uhd.mp4");
        best.Mime.ShouldBe("video/mp4");
        best.Duration.ShouldBe(TimeSpan.FromSeconds(15));
        best.Attribution.ShouldBe("Ruvim Miksanskiy on Pexels");

        await p.FetchAsync(new FulfillRequest("video", "3195394", "103"), ws.Create(h.Client()), Ct);
        h.Seen.Last().Uri.ToString().ShouldContain("103-hd.mp4");
    }

    [Fact]
    public async Task Fetch_unknown_variant_is_item_missing()
    {
        using var ws = new TempWorkspace();
        var h = new FakeHandler().Json("/videos/videos/3195394", "pexels/video.json");
        var ex = await Should.ThrowAsync<StockException>(() => Create(h).FetchAsync(new FulfillRequest("video", "3195394", "999"), ws.Create(h.Client()), Ct));
        ex.Reason.ShouldBe(StockReason.ItemMissing);
    }

    [Fact]
    public async Task Errors_map_to_reasons()
    {
        var notFound = new FakeHandler().Status("/v1/photos/", HttpStatusCode.NotFound);
        (await Should.ThrowAsync<StockException>(() => Create(notFound).GetItemAsync("photo", "1", Ct))).Reason.ShouldBe(StockReason.ItemMissing);

        var limited = new FakeHandler().Status("/v1/search", HttpStatusCode.TooManyRequests);
        (await Should.ThrowAsync<StockException>(() => Create(limited).SearchAsync(Query("photo", "x"), Ct))).Reason.ShouldBe(StockReason.SourceBlocked);
    }
}
