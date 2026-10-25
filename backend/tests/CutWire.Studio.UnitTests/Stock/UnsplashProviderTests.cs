using System.Net;
using CutWire.Studio.Application.Stock;
using CutWire.Studio.Infrastructure.Stock;
using CutWire.Studio.Infrastructure.Stock.Providers;
using Microsoft.Extensions.Options;
using Shouldly;

namespace CutWire.Studio.UnitTests.Stock;

public class UnsplashProviderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static UnsplashProvider Create(FakeHandler h) =>
        new(h.Client(c =>
            {
                c.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Client-ID AK");
                c.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Version", "v1");
            }), new NullProviderCache(), Options.Create(new StockOptions()), Options.Create(new UnsplashOptions { AccessKey = "AK" }));

    private static ProviderQuery Query(string? q, int limit = 2, string? cursor = null, params (string, string)[] filters) =>
        new("photo", q, limit, cursor, filters.ToDictionary(f => f.Item1, f => f.Item2));

    [Fact]
    public async Task Search_maps_items_filters_and_headers()
    {
        var h = new FakeHandler().Json("/search/photos", "unsplash/search.json");
        var page = await Create(h).SearchAsync(Query("barn", 2, null, ("orientation", "landscape"), ("color", "red")), Ct);

        h.Last.Uri.AbsolutePath.ShouldBe("/search/photos");
        h.Last.Query["query"].ShouldBe("barn");
        h.Last.Query["orientation"].ShouldBe("landscape");
        h.Last.Query["color"].ShouldBe("red");
        h.Last.Headers["Authorization"].ShouldBe("Client-ID AK");
        h.Last.Headers["Accept-Version"].ShouldBe("v1");

        page.Items.Count.ShouldBe(2);
        var a = page.Items[0];
        a.Native.ShouldBe("abc1");
        a.Type.ShouldBe("photo");
        a.Title.ShouldBe("a red barn");
        a.Creator!.Name.ShouldBe("John Doe");
        a.Creator.Url.ShouldBe("https://unsplash.com/@jdoe");
        a.ThumbSrc.ShouldEndWith("small");
        a.PreviewSrc.ShouldEndWith("reg");
        a.License!.Attribution.ShouldBe("Photo by John Doe on Unsplash");

        var b = page.Items[1];
        b.Title.ShouldBe("Untitled");
        b.Creator!.Name.ShouldBe("anon");
        b.License!.Attribution.ShouldBe("Photo by anon on Unsplash");
        page.NextCursor.ShouldBe("2");
    }

    [Fact]
    public async Task Search_stops_at_last_page()
    {
        var h = new FakeHandler().Json("/search/photos", "unsplash/search.json");
        var page = await Create(h).SearchAsync(Query("barn", 2, "2"), Ct);
        page.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task Empty_query_uses_editorial_feed_array()
    {
        var h = new FakeHandler().Json("/photos", "unsplash/feed.json");
        var page = await Create(h).SearchAsync(Query(null, 1), Ct);

        h.Last.Uri.AbsolutePath.ShouldBe("/photos");
        h.Last.Query["query"].ShouldBeNull();
        page.Items.ShouldHaveSingleItem().Title.ShouldBe("Morning coffee");
        page.NextCursor.ShouldBe("2");
    }

    [Fact]
    public async Task GetItem_reads_single_photo()
    {
        var h = new FakeHandler().Json("/photos/Dwu85P9SOIk", "unsplash/photo.json");
        var item = await Create(h).GetItemAsync("photo", "Dwu85P9SOIk", Ct);
        item!.Title.ShouldBe("A misty mountain at dawn");
        item.Width.ShouldBe(6000);
    }

    [Fact]
    public async Task Fetch_registers_download_then_downloads_full()
    {
        using var ws = new TempWorkspace();
        var h = new FakeHandler()
            .Json("/photos/Dwu85P9SOIk/download", "unsplash/photo.json")
            .Json("/photos/Dwu85P9SOIk", "unsplash/photo.json")
            .Bytes("images.unsplash.com", [7, 7]);
        var asset = await Create(h).FetchAsync(new FulfillRequest("photo", "Dwu85P9SOIk", null), ws.Create(h.Client()), Ct);

        var urls = h.Seen.Select(s => s.Uri.ToString()).ToList();
        urls[0].ShouldContain("/photos/Dwu85P9SOIk");
        urls[1].ShouldContain("/photos/Dwu85P9SOIk/download?ixid=abc");
        urls[2].ShouldContain("q=85&fm=jpg");
        asset.Mime.ShouldBe("image/jpeg");
        asset.Filename.ShouldBe("A_misty_mountain_at_dawn.jpg");
        asset.Attribution.ShouldBe("Photo by Jane Roe on Unsplash");
        asset.License!.Name.ShouldBe("Unsplash License");
        File.ReadAllBytes(asset.Path).ShouldBe([7, 7]);
    }

    [Fact]
    public async Task Fetch_still_succeeds_when_download_ping_fails()
    {
        using var ws = new TempWorkspace();
        var h = new FakeHandler()
            .Status("/download?ixid=abc", HttpStatusCode.TooManyRequests)
            .Json("/photos/Dwu85P9SOIk", "unsplash/photo.json")
            .Bytes("images.unsplash.com", [1]);
        var asset = await Create(h).FetchAsync(new FulfillRequest("photo", "Dwu85P9SOIk", null), ws.Create(h.Client()), Ct);
        File.Exists(asset.Path).ShouldBeTrue();
    }

    [Fact]
    public async Task Errors_map_to_reasons()
    {
        var notFound = new FakeHandler().Status("/photos/", HttpStatusCode.NotFound);
        (await Should.ThrowAsync<StockException>(() => Create(notFound).GetItemAsync("photo", "x", Ct))).Reason.ShouldBe(StockReason.ItemMissing);

        var limited = new FakeHandler().Status("/search/photos", HttpStatusCode.TooManyRequests);
        (await Should.ThrowAsync<StockException>(() => Create(limited).SearchAsync(Query("x"), Ct))).Reason.ShouldBe(StockReason.SourceBlocked);
    }

    [Fact]
    public void Descriptor_uses_select_filters()
    {
        var d = Create(new FakeHandler()).Descriptor;
        d.Serves.ShouldHaveSingleItem().Type.ShouldBe("photo");
        d.Serving("photo")!.Filters!.ShouldAllBe(f => f.Kind == FilterKinds.Select && f.Options!.Count > 0);
    }
}
