using System.Net;
using CutWire.Studio.Application.Stock;
using CutWire.Studio.Infrastructure.Stock;
using CutWire.Studio.Infrastructure.Stock.Providers;
using Microsoft.Extensions.Options;
using Shouldly;

namespace CutWire.Studio.UnitTests.Stock;

public class JamendoProviderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JamendoProvider Create(FakeHandler h) =>
        new(h.Client(), new NullProviderCache(), Options.Create(new StockOptions()), Options.Create(new JamendoOptions { ClientId = "CID" }));

    private static ProviderQuery Query(string? q, int limit = 2, string? cursor = null, params (string, string)[] filters) =>
        new("music", q, limit, cursor, filters.ToDictionary(f => f.Item1, f => f.Item2));

    [Fact]
    public async Task Search_maps_tracks_and_skips_undownloadable()
    {
        var h = new FakeHandler().Json("/tracks/", "jamendo/tracks.json");
        var page = await Create(h).SearchAsync(Query("drive", 2, null, ("order", "releasedate_desc"), ("tags", "rock")), Ct);

        var q = h.Last.Query;
        q["client_id"].ShouldBe("CID");
        q["format"].ShouldBe("json");
        q["search"].ShouldBe("drive");
        q["order"].ShouldBe("releasedate_desc");
        q["tags"].ShouldBe("rock");
        q["audioformat"].ShouldBe("mp32");
        q["include"].ShouldBe("musicinfo licenses");
        q["limit"].ShouldBe("2");
        q["offset"].ShouldBe("0");

        var item = page.Items.ShouldHaveSingleItem();
        item.Native.ShouldBe("1532");
        item.Type.ShouldBe("music");
        item.Title.ShouldBe("Sunrise Drive");
        item.DurationMs.ShouldBe(187000);
        item.Creator!.Name.ShouldBe("Skylar");
        item.Creator.Url.ShouldBe("https://www.jamendo.com/artist/354");
        item.ThumbSrc.ShouldEndWith("track/1.jpg");
        item.PreviewMime.ShouldBe("audio/mpeg");
        item.License!.Name.ShouldBe("Creative Commons");
        item.License.Url.ShouldBe("http://creativecommons.org/licenses/by-nc/3.0/");
        item.License.Attribution.ShouldBe("Sunrise Drive by Skylar (Jamendo)");
        page.NextCursor.ShouldBe("2");
    }

    [Fact]
    public async Task Empty_query_defaults_to_popularity_and_short_page_ends()
    {
        var h = new FakeHandler().Json("/tracks/", "jamendo/tracks.json");
        var page = await Create(h).SearchAsync(Query(null, 30, "60"), Ct);

        h.Last.Query["order"].ShouldBe("popularity_total");
        h.Last.Query["search"].ShouldBeNull();
        h.Last.Query["offset"].ShouldBe("60");
        page.NextCursor.ShouldBeNull();
    }

    [Theory]
    [InlineData("30-120", "30_120")]
    [InlineData("30-", "30_3600")]
    [InlineData("-90", "0_90")]
    [InlineData("120-30", "30_120")]
    public async Task Duration_range_becomes_durationbetween(string input, string expected)
    {
        var h = new FakeHandler().Json("/tracks/", "jamendo/tracks.json");
        await Create(h).SearchAsync(Query("x", 2, null, ("duration", input)), Ct);
        h.Last.Query["durationbetween"].ShouldBe(expected);
    }

    [Fact]
    public async Task Unparsable_duration_is_ignored()
    {
        var h = new FakeHandler().Json("/tracks/", "jamendo/tracks.json");
        await Create(h).SearchAsync(Query("x", 2, null, ("duration", "long")), Ct);
        h.Last.Query["durationbetween"].ShouldBeNull();
    }

    [Fact]
    public async Task In_body_failure_is_source_error()
    {
        var h = new FakeHandler().Json("/tracks/", "jamendo/error.json");
        (await Should.ThrowAsync<StockException>(() => Create(h).SearchAsync(Query("x"), Ct))).Reason.ShouldBe(StockReason.SourceError);
    }

    [Fact]
    public async Task GetItem_missing_track_is_item_missing()
    {
        var h = new FakeHandler().Json("id=1532", "jamendo/tracks.json").Json("id=0", "jamendo/empty.json");
        var p = Create(h);
        (await p.GetItemAsync("music", "1532", Ct))!.Native.ShouldBe("1532");
        (await Should.ThrowAsync<StockException>(() => p.GetItemAsync("music", "0", Ct))).Reason.ShouldBe(StockReason.ItemMissing);
    }

    [Fact]
    public async Task Fetch_downloads_audiodownload_with_attribution()
    {
        using var ws = new TempWorkspace();
        var h = new FakeHandler().Json("id=1532", "jamendo/tracks.json").Bytes("/download/track/1532", [3, 3, 3]);
        var asset = await Create(h).FetchAsync(new FulfillRequest("music", "1532", null), ws.Create(h.Client()), Ct);

        asset.Mime.ShouldBe("audio/mpeg");
        asset.Filename.ShouldBe("Sunrise_Drive.mp3");
        asset.Attribution.ShouldBe("Sunrise Drive by Skylar (Jamendo)");
        asset.Duration.ShouldBe(TimeSpan.FromSeconds(187));
        File.ReadAllBytes(asset.Path).ShouldBe([3, 3, 3]);
    }

    [Fact]
    public async Task Fetch_falls_back_to_stream_when_download_not_allowed()
    {
        using var ws = new TempWorkspace();
        var h = new FakeHandler().Json("id=1533", "jamendo/locked.json").Bytes("trackid=1533", [1]);
        await Create(h).FetchAsync(new FulfillRequest("music", "1533", null), ws.Create(h.Client()), Ct);
        h.Last.Uri.ToString().ShouldContain("trackid=1533");
    }

    [Fact]
    public async Task Errors_map_to_reasons()
    {
        var notFound = new FakeHandler().Status("/tracks/", HttpStatusCode.NotFound);
        (await Should.ThrowAsync<StockException>(() => Create(notFound).GetItemAsync("music", "1", Ct))).Reason.ShouldBe(StockReason.ItemMissing);

        var limited = new FakeHandler().Status("/tracks/", HttpStatusCode.TooManyRequests);
        (await Should.ThrowAsync<StockException>(() => Create(limited).SearchAsync(Query("x"), Ct))).Reason.ShouldBe(StockReason.SourceBlocked);
    }

    [Fact]
    public void Descriptor_serves_music_with_select_and_range_filters()
    {
        var svc = Create(new FakeHandler()).Descriptor.Serves.ShouldHaveSingleItem();
        svc.Type.ShouldBe("music");
        svc.Filters!.Select(f => f.Kind).ShouldBe([FilterKinds.Select, FilterKinds.Select, FilterKinds.Range]);
    }
}
