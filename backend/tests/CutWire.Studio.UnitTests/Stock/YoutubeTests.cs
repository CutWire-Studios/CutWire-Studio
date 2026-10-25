using CutWire.Studio.Application.Stock;
using CutWire.Studio.Infrastructure.Stock.Providers;
using Shouldly;

namespace CutWire.Studio.UnitTests.Stock;

public class YoutubeTests
{
    private const string Id = "dQw4w9WgXcQ";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ProviderQuery Query(string? q, int limit = 0) => new("video", q, limit, null, new Dictionary<string, string>());

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=" + Id)]
    [InlineData("https://youtube.com/watch?v=" + Id + "&t=42s")]
    [InlineData("https://m.youtube.com/watch?v=" + Id)]
    [InlineData("https://youtu.be/" + Id)]
    [InlineData("https://youtu.be/" + Id + "?si=abc")]
    [InlineData("https://www.youtube.com/shorts/" + Id)]
    [InlineData("https://www.youtube.com/embed/" + Id)]
    [InlineData("https://www.youtube.com/live/" + Id)]
    [InlineData("https://music.youtube.com/watch?v=" + Id)]
    [InlineData("  https://www.youtube.com/watch?v=" + Id + "  ")]
    public void VideoId_IsExtractedFromEveryUrlShape(string url) => YoutubeUrls.VideoId(url).ShouldBe(Id);

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("https://www.youtube.com/")]
    [InlineData("https://www.youtube.com/@channel")]
    [InlineData("https://www.youtube.com/watch?v=tooshort")]
    [InlineData("https://www.youtube.com/playlist?list=PL123")]
    public void VideoId_YieldsNothingRatherThanSomethingWrong(string url) => YoutubeUrls.VideoId(url).ShouldBe("");

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=" + Id, true, false)]
    [InlineData("https://youtube.com/watch?v=" + Id, true, false)]
    [InlineData("https://m.youtube.com/watch?v=" + Id, true, false)]
    [InlineData("https://youtu.be/" + Id, true, false)]
    [InlineData("https://www.youtu.be/" + Id, true, false)]
    [InlineData("HTTPS://WWW.YOUTUBE.COM/watch?v=" + Id, true, false)]
    [InlineData("https://music.youtube.com/watch?v=" + Id, false, true)]
    [InlineData("https://youtube.com.evil.test/watch?v=" + Id, false, false)]
    [InlineData("https://evil.test/https://www.youtube.com/watch?v=" + Id, false, false)]
    [InlineData("https://notyoutube.com/watch?v=" + Id, false, false)]
    [InlineData("https://www.youtube.com.evil.test/", false, false)]
    [InlineData("https://vimeo.com/12345", false, false)]
    [InlineData("", false, false)]
    [InlineData("://malformed", false, false)]
    [InlineData("file://www.youtube.com/watch?v=" + Id, false, false)]
    [InlineData("ftp://youtu.be/" + Id, false, false)]
    [InlineData("javascript:alert(1)", false, false)]
    [InlineData("https://evil.test@www.youtube.com/watch?v=" + Id, false, false)]
    [InlineData("https://user:pw@music.youtube.com/watch?v=" + Id, false, false)]
    [InlineData("https://www.youtube.com/watch?v=" + Id + "\n--exec=id", false, false)]
    [InlineData("http://169.254.169.254/latest/meta-data/", false, false)]
    public void OwnsUrl_IsHostExact(string url, bool video, bool music)
    {
        var runner = YtdlpFixtures.Runner(FakeProcessRunner.Json("youtube-video"));
        new YoutubeVideoProvider(runner).OwnsUrl(url).ShouldBe(video);
        new YoutubeMusicProvider(runner).OwnsUrl(url).ShouldBe(music);
    }

    [Fact]
    public void Descriptors_ShipWithTightQuotasAndSearch()
    {
        var video = YoutubeVideoProvider.Info;
        video.Id.ShouldBe("youtube");
        video.Capabilities.ShouldBe(ProviderCapabilities.Search | ProviderCapabilities.Resolve | ProviderCapabilities.Fulfill);
        video.Serves.Single().Type.ShouldBe("video");
        video.Serves.Single().Quota.ShouldBe(new QuotaDefault(1, "day", "client"));

        var music = YoutubeMusicProvider.Info;
        music.Id.ShouldBe("youtube_music");
        music.Capabilities.ShouldBe(ProviderCapabilities.Search | ProviderCapabilities.Resolve | ProviderCapabilities.Fulfill);
        music.Serves.Single().Type.ShouldBe("audio");
        music.Serves.Single().Quota.ShouldBe(new QuotaDefault(1, "day", "client"));
    }

    [Fact]
    public async Task Resolve_MapsVideoMetadata()
    {
        var fake = FakeProcessRunner.Json("youtube-video");
        var item = await new YoutubeVideoProvider(YtdlpFixtures.Runner(fake)).ResolveAsync("https://youtu.be/" + Id + "?si=x", Ct);

        item.Native.ShouldBe(Id);
        item.Type.ShouldBe("video");
        item.Title.ShouldBe("Never Gonna Give You Up");
        item.Creator.ShouldBe(new ProviderCreator("Rick Astley", "https://www.youtube.com/channel/UCuAXFkgsw1L7xaCfnd5JJOw"));
        item.DurationMs.ShouldBe(212_000);
        item.Width.ShouldBe(1920);
        item.Height.ShouldBe(1080);
        item.ThumbSrc.ShouldBe("https://i.ytimg.com/vi/dQw4w9WgXcQ/sddefault.jpg");
        item.License.ShouldBe(new ProviderLicense("YouTube Standard License", "https://www.youtube.com/static?template=terms"));
        item.Variants.ShouldBeEmpty();
        fake.Calls.Single().Args[^1].ShouldBe("https://www.youtube.com/watch?v=" + Id);
    }

    [Fact]
    public async Task Resolve_OfAnUnownedOrIdlessUrlIsUnsupported()
    {
        var fake = FakeProcessRunner.Json("youtube-video");
        var provider = new YoutubeVideoProvider(YtdlpFixtures.Runner(fake));
        foreach (var url in new[] { "https://www.youtube.com/@channel", "https://music.youtube.com/watch?v=" + Id, "https://evil.test/watch?v=" + Id })
        {
            var ex = await Should.ThrowAsync<StockException>(() => provider.ResolveAsync(url, Ct));
            ex.Reason.ShouldBe(StockReason.LinkUnsupported);
        }
        fake.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetItem_RejectsMalformedIdsWithoutSpawning()
    {
        var fake = FakeProcessRunner.Json("youtube-video");
        var provider = new YoutubeVideoProvider(YtdlpFixtures.Runner(fake));
        foreach (var native in new[] { "short", "--exec=id", Id + "x", "https://evil.test/" })
        {
            var ex = await Should.ThrowAsync<StockException>(() => provider.GetItemAsync("video", native, Ct));
            ex.Reason.ShouldBe(StockReason.ItemMissing);
        }
        fake.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetItem_OfALiveStreamIsLiveStream()
    {
        var provider = new YoutubeVideoProvider(YtdlpFixtures.Runner(FakeProcessRunner.Json("youtube-live")));
        var ex = await Should.ThrowAsync<StockException>(() => provider.GetItemAsync("video", "jfKfPfyJRdk", Ct));
        ex.Reason.ShouldBe(StockReason.LiveStream);
    }

    [Fact]
    public async Task Search_MapsEntriesAndSkipsLiveAndNonVideoIds()
    {
        var fake = FakeProcessRunner.Json("youtube-search");
        var page = await new YoutubeVideoProvider(YtdlpFixtures.Runner(fake)).SearchAsync(Query("cats"), Ct);

        page.NextCursor.ShouldBeNull();
        page.Items.Select(i => i.Native).ShouldBe([Id, "9bZkp7q19f0"]);
        var first = page.Items[0];
        first.Type.ShouldBe("video");
        first.Title.ShouldBe("Never Gonna Give You Up");
        first.DurationMs.ShouldBe(212_000);
        first.Creator!.Name.ShouldBe("Rick Astley");
        first.ThumbSrc.ShouldBe("https://i.ytimg.com/vi/dQw4w9WgXcQ/hqdefault.jpg");
        var second = page.Items[1];
        second.Creator.ShouldBe(new ProviderCreator("officialpsy", "https://www.youtube.com/@officialpsy"));
        second.ThumbSrc.ShouldBe("https://i.ytimg.com/vi/9bZkp7q19f0/hqdefault.jpg");

        var args = fake.Calls.Single().Args;
        args[^2].ShouldBe("--");
        args[^1].ShouldBe("ytsearch20:cats");
    }

    [Theory]
    [InlineData(0, "ytsearch20:cats")]
    [InlineData(5, "ytsearch5:cats")]
    [InlineData(500, "ytsearch30:cats")]
    public async Task Search_ClampsTheLimit(int limit, string target)
    {
        var fake = FakeProcessRunner.Json("youtube-search");
        await new YoutubeVideoProvider(YtdlpFixtures.Runner(fake)).SearchAsync(Query("cats", limit), Ct);
        fake.Calls.Single().Args[^1].ShouldBe(target);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Search_WithAnEmptyQueryIsEmptyAndSpawnsNothing(string? q)
    {
        var fake = FakeProcessRunner.Json("youtube-search");
        (await new YoutubeVideoProvider(YtdlpFixtures.Runner(fake)).SearchAsync(Query(q), Ct)).ShouldBeSameAs(ProviderPage.Empty);
        (await new YoutubeMusicProvider(YtdlpFixtures.Runner(fake)).SearchAsync(Query(q), Ct)).ShouldBeSameAs(ProviderPage.Empty);
        fake.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("cats\n--exec=id")]
    [InlineData("a\0b")]
    public async Task Search_RejectsQueriesThatCouldInjectLines(string q)
    {
        var fake = FakeProcessRunner.Json("youtube-search");
        await Should.ThrowAsync<StockException>(() => new YoutubeVideoProvider(YtdlpFixtures.Runner(fake)).SearchAsync(Query(q), Ct));
        fake.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Search_AQueryThatLooksLikeAnOptionStaysInsideTheSearchTarget()
    {
        var fake = FakeProcessRunner.Json("youtube-search");
        await new YoutubeVideoProvider(YtdlpFixtures.Runner(fake)).SearchAsync(Query("--exec=id", 3), Ct);
        var args = fake.Calls.Single().Args;
        args[^1].ShouldBe("ytsearch3:--exec=id");
        args.Where(a => a.StartsWith("--exec")).ShouldBeEmpty();
    }

    [Fact]
    public async Task MusicSearch_UsesSongsSectionAndDerivesThumbnails()
    {
        var fake = FakeProcessRunner.Json("youtube-music-search");
        var page = await new YoutubeMusicProvider(YtdlpFixtures.Runner(fake)).SearchAsync(Query("sand storm & more"), Ct);

        fake.Calls.Single().Args[^1].ShouldBe("https://music.youtube.com/search?q=sand%20storm%20%26%20more#songs");
        page.Items.Select(i => i.Native).ShouldBe(["y6120QOlsfU", "4Tr0otuiQuV"]);
        page.Items[0].Type.ShouldBe("audio");
        page.Items[0].Title.ShouldBe("Sandstorm");
        page.Items[0].ThumbSrc.ShouldBe("https://i.ytimg.com/vi/y6120QOlsfU/hqdefault.jpg");
        page.Items[0].Creator!.Name.ShouldBe("Darude");
    }

    [Fact]
    public async Task MusicResolve_MapsMetadataAndFallsBackToUploader()
    {
        var item = await new YoutubeMusicProvider(YtdlpFixtures.Runner(FakeProcessRunner.Json("youtube-music")))
            .ResolveAsync("https://music.youtube.com/watch?v=y6120QOlsfU", Ct);

        item.Native.ShouldBe("y6120QOlsfU");
        item.Type.ShouldBe("audio");
        item.DurationMs.ShouldBe(225_500);
        item.Creator.ShouldBe(new ProviderCreator("Darude - Topic", "https://www.youtube.com/channel/UCAAA"));
        item.ThumbSrc.ShouldBe("https://i.ytimg.com/vi/y6120QOlsfU/sddefault.jpg");
        item.Width.ShouldBe(0);
    }

    [Fact]
    public async Task Fetch_DownloadsIntoTheWorkspaceAndDescribesTheAsset()
    {
        var dir = YtdlpFixtures.TempDir();
        try
        {
            var file = Path.Combine(dir, Id + ".mp4");
            var fake = new FakeProcessRunner(spec =>
            {
                if (spec.OnStdoutLine is null) return FakeProcessRunner.Ok(YtdlpFixtures.Read("youtube-video"));
                File.WriteAllText(file, "x");
                spec.OnStdoutLine(file);
                return FakeProcessRunner.Ok("");
            });
            var progress = new List<double>();

            var asset = await new YoutubeVideoProvider(YtdlpFixtures.Runner(fake))
                .FetchAsync(new FulfillRequest("video", Id, null), YtdlpFixtures.Workspace(dir, progress), Ct);

            asset.Path.ShouldBe(file);
            asset.Mime.ShouldBe("video/mp4");
            asset.Filename.ShouldBe("Never_Gonna_Give_You_Up.mp4");
            asset.Attribution.ShouldBe("Never Gonna Give You Up by Rick Astley (YouTube)");
            asset.Duration.ShouldBe(TimeSpan.FromSeconds(212));
            asset.License!.Name.ShouldBe("YouTube Standard License");
            progress.ShouldContain(0.1);
            progress.ShouldAllBe(p => p <= 0.66);
            var download = fake.Calls[1].Args;
            download[download.ToList().IndexOf("-f") + 1].ShouldContain("height<=?1080");
            download[^2].ShouldBe("--");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Fetch_OfALiveStreamIsRefusedBeforeDownloading()
    {
        var fake = FakeProcessRunner.Json("youtube-live");
        var ex = await Should.ThrowAsync<StockException>(() => new YoutubeVideoProvider(YtdlpFixtures.Runner(fake))
            .FetchAsync(new FulfillRequest("video", "jfKfPfyJRdk", null), YtdlpFixtures.Workspace("/tmp"), Ct));
        ex.Reason.ShouldBe(StockReason.LiveStream);
        fake.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task MusicFetch_ProducesAnAudioAsset()
    {
        var dir = YtdlpFixtures.TempDir();
        try
        {
            var file = Path.Combine(dir, "y6120QOlsfU.m4a");
            var fake = new FakeProcessRunner(spec =>
            {
                if (spec.OnStdoutLine is null) return FakeProcessRunner.Ok(YtdlpFixtures.Read("youtube-music"));
                File.WriteAllText(file, "x");
                spec.OnStdoutLine(file);
                return FakeProcessRunner.Ok("");
            });
            var asset = await new YoutubeMusicProvider(YtdlpFixtures.Runner(fake))
                .FetchAsync(new FulfillRequest("audio", "y6120QOlsfU", null), YtdlpFixtures.Workspace(dir), Ct);

            asset.Mime.ShouldBe("audio/mp4");
            asset.Filename.ShouldBe("Sandstorm.m4a");
            asset.Attribution.ShouldBe("Sandstorm by Darude - Topic (YouTube Music)");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
