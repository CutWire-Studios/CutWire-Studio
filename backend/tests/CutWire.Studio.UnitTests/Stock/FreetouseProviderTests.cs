using System.Net;
using CutWire.Studio.Application.Stock;
using CutWire.Studio.Infrastructure.Stock;
using CutWire.Studio.Infrastructure.Stock.Providers;
using Microsoft.Extensions.Options;
using Shouldly;

namespace CutWire.Studio.UnitTests.Stock;

public class FreetouseProviderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FreetouseProvider Create(FakeHandler h) =>
        new(h.Client(), new NullProviderCache(), Options.Create(new StockOptions()), Options.Create(new FreetouseOptions()));

    private static ProviderQuery Query(string? q, int limit = 2, string? cursor = null) =>
        new("music", q, limit, cursor, new Dictionary<string, string>());

    [Fact]
    public async Task Featured_lists_all_tracks_skipping_premium_and_enforcing_limit()
    {
        var h = new FakeHandler().Json("/music/tracks/all", "freetouse/tracks.json");
        var page = await Create(h).SearchAsync(Query(null, 2), Ct);

        h.Last.Query["limit"].ShouldBe("2");
        h.Last.Query["offset"].ShouldBe("0");
        page.Items.Select(i => i.Native).ShouldBe(["76bfc7a1", "1914d68a"]);
        var item = page.Items[0];
        item.Type.ShouldBe("music");
        item.Title.ShouldBe("last dance");
        item.DurationMs.ShouldBe(146375);
        item.Creator!.Name.ShouldBe("massobeats");
        item.ThumbSrc.ShouldEndWith("md.webp");
        item.PreviewSrc.ShouldEndWith("file.mp3");
        item.PreviewMime.ShouldBe("audio/mpeg");
        item.License!.Name.ShouldBe("Free To Use");
        item.License.Attribution.ShouldBe("last dance by massobeats (freetouse.com)");
        page.Items[1].Creator.ShouldBeNull();
        page.NextCursor.ShouldBe("2");
    }

    [Fact]
    public async Task Search_uses_search_endpoint_and_offset_cursor()
    {
        var h = new FakeHandler().Json("/music/tracks/search", "freetouse/tracks.json");
        var page = await Create(h).SearchAsync(Query("lofi", 50, "100"), Ct);

        h.Last.Query["query"].ShouldBe("lofi");
        h.Last.Query["offset"].ShouldBe("100");
        page.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task GetItem_and_missing()
    {
        var h = new FakeHandler().Json("/tracks/76bfc7a1", "freetouse/track.json").Json("/tracks/gone", "freetouse/missing.json");
        var p = Create(h);
        (await p.GetItemAsync("music", "76bfc7a1", Ct))!.Title.ShouldBe("last dance");
        (await Should.ThrowAsync<StockException>(() => p.GetItemAsync("music", "gone", Ct))).Reason.ShouldBe(StockReason.ItemMissing);
    }

    [Fact]
    public async Task Fetch_downloads_mp3_with_attribution()
    {
        using var ws = new TempWorkspace();
        var h = new FakeHandler().Json("/music/tracks/76bfc7a1", "freetouse/track.json").Bytes("file.mp3", [4, 2]);
        var asset = await Create(h).FetchAsync(new FulfillRequest("music", "76bfc7a1", null), ws.Create(h.Client()), Ct);

        asset.Mime.ShouldBe("audio/mpeg");
        asset.Filename.ShouldBe("last_dance.mp3");
        asset.Attribution.ShouldBe("last dance by massobeats (freetouse.com)");
        asset.License!.Url.ShouldBe("https://freetouse.com/license");
        asset.Duration!.Value.TotalSeconds.ShouldBe(146.375);
        File.ReadAllBytes(asset.Path).ShouldBe([4, 2]);
    }

    [Fact]
    public async Task Fetch_premium_is_item_missing()
    {
        using var ws = new TempWorkspace();
        var h = new FakeHandler().Json("/music/tracks/premium1", "freetouse/premium.json");
        (await Should.ThrowAsync<StockException>(() => Create(h).FetchAsync(new FulfillRequest("music", "premium1", null), ws.Create(h.Client()), Ct)))
            .Reason.ShouldBe(StockReason.ItemMissing);
    }

    [Fact]
    public async Task Errors_map_to_reasons()
    {
        var notFound = new FakeHandler().Status("/music/tracks/", HttpStatusCode.NotFound);
        (await Should.ThrowAsync<StockException>(() => Create(notFound).GetItemAsync("music", "x", Ct))).Reason.ShouldBe(StockReason.ItemMissing);

        var limited = new FakeHandler().Status("/music/tracks/", HttpStatusCode.TooManyRequests);
        (await Should.ThrowAsync<StockException>(() => Create(limited).SearchAsync(Query(null), Ct))).Reason.ShouldBe(StockReason.SourceBlocked);
    }

    [Fact]
    public void Descriptor_serves_music_without_filters()
    {
        var d = Create(new FakeHandler()).Descriptor;
        d.Id.ShouldBe("freetouse");
        d.Serves.ShouldHaveSingleItem().Type.ShouldBe("music");
        d.Serves[0].FilterList.ShouldBeEmpty();
    }
}
