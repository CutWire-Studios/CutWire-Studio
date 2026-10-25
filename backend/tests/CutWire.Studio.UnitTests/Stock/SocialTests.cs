using CutWire.Studio.Application.Stock;
using CutWire.Studio.Infrastructure.Stock.Providers;
using Shouldly;

namespace CutWire.Studio.UnitTests.Stock;

public class SocialTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SocialProvider Make(string id, FakeProcessRunner fake)
    {
        var runner = YtdlpFixtures.Runner(fake);
        return id switch
        {
            "instagram" => SocialProvider.Instagram(runner),
            "facebook" => SocialProvider.Facebook(runner),
            "tiktok" => SocialProvider.TikTok(runner),
            "vimeo" => SocialProvider.Vimeo(runner),
            "x" => SocialProvider.X(runner),
            "soundcloud" => SocialProvider.SoundCloud(runner),
            _ => throw new ArgumentException(id),
        };
    }

    private static readonly Dictionary<string, string[]> OwnedHosts = new()
    {
        ["instagram"] = ["https://www.instagram.com/reel/Cabc123/", "https://m.instagram.com/p/Cabc123/"],
        ["facebook"] = ["https://www.facebook.com/watch/?v=123", "https://fb.watch/abc123/"],
        ["tiktok"] = ["https://www.tiktok.com/@user/video/123", "https://vm.tiktok.com/ZMabc/"],
        ["vimeo"] = ["https://vimeo.com/123456", "https://player.vimeo.com/video/123456"],
        ["x"] = ["https://x.com/user/status/123", "https://twitter.com/user/status/123"],
        ["soundcloud"] = ["https://soundcloud.com/artist/track", "https://on.soundcloud.com/abc"],
    };

    public static TheoryData<string, string[], string[]> Hosts => new()
    {
        { "instagram", ["https://www.instagram.com/reel/Cabc123/", "https://instagram.com/p/Cabc123/", "https://m.instagram.com/p/Cabc123/", "https://ddinstagram.com/p/x/"],
            ["https://instagram.com.evil.test/p/1/", "https://notinstagram.com/p/1/", "https://evil.test/https://instagram.com/p/1/"] },
        { "facebook", ["https://www.facebook.com/watch/?v=123", "https://fb.watch/abc123/", "https://web.facebook.com/reel/123", "https://m.facebook.com/x", "https://facebook.com/x"],
            ["https://facebook.com.attacker.test/watch", "https://fb.watch.evil.test/x", "https://notfacebook.com/x"] },
        { "tiktok", ["https://www.tiktok.com/@user/video/123", "https://vm.tiktok.com/ZMabc/", "https://vt.tiktok.com/ZSabc/", "https://tiktok.com/@a/video/1", "https://m.tiktok.com/v/1"],
            ["https://tiktok.com.evil.test/v/1", "https://nottiktok.com/v/1", "https://xvm.tiktok.com/x"] },
        { "vimeo", ["https://vimeo.com/123456", "https://www.vimeo.com/123456", "https://player.vimeo.com/video/123456"],
            ["https://vimeo.com.evil.test/1", "https://myvimeo.com/1", "https://vimeo.co/1"] },
        { "x", ["https://x.com/user/status/123", "https://www.x.com/u/status/1", "https://twitter.com/user/status/123", "https://www.twitter.com/u/status/1", "https://mobile.twitter.com/user/status/123"],
            ["https://x.com.evil.test/a", "https://notx.com/a", "https://t.co/abc", "https://fx.com/a"] },
        { "soundcloud", ["https://soundcloud.com/artist/track", "https://www.soundcloud.com/a/b", "https://m.soundcloud.com/a/b", "https://on.soundcloud.com/abc"],
            ["https://soundcloud.com.evil.test/x", "https://mysoundcloud.com/x", "https://api.soundcloud.com/x"] },
    };

    [Theory]
    [MemberData(nameof(Hosts))]
    public void OwnsUrl_AcceptsItsHostsAndNothingElse(string id, string[] owned, string[] foreign)
    {
        var provider = Make(id, FakeProcessRunner.Json("instagram-reel"));
        foreach (var url in owned) provider.OwnsUrl(url).ShouldBeTrue(url);

        var hostile = new[]
        {
            "http://169.254.169.254/latest/meta-data/", "file:///etc/passwd", "", "://malformed", "javascript:alert(1)", "ftp://" + new Uri(owned[0]).Host + "/x",
            "file://" + new Uri(owned[0]).Host + "/etc/passwd", "https://evil.test@" + new Uri(owned[0]).Host + "/x",
            "https://user:pw@" + new Uri(owned[0]).Host + "/x", owned[0] + "\n--exec=id", owned[0] + "\\x",
        };
        foreach (var url in foreign.Concat(hostile)) provider.OwnsUrl(url).ShouldBeFalse(url);
    }

    [Fact]
    public void OwnsUrl_NoProviderClaimsAnotherProvidersHost()
    {
        var all = new[] { "instagram", "facebook", "tiktok", "vimeo", "x", "soundcloud" }.Select(id => Make(id, FakeProcessRunner.Json("instagram-reel"))).ToList();
        foreach (var provider in all)
        {
            var urls = OwnedHosts[provider.Descriptor.Id];
            foreach (var other in all.Where(o => o != provider))
                urls.ShouldAllBe(u => !other.OwnsUrl(u));
        }
    }

    [Theory]
    [InlineData("instagram", "video")]
    [InlineData("facebook", "video")]
    [InlineData("tiktok", "video")]
    [InlineData("vimeo", "video")]
    [InlineData("x", "video")]
    [InlineData("soundcloud", "audio")]
    public void Descriptors_AreResolveOnlyWithAClientQuota(string id, string type)
    {
        var d = Make(id, FakeProcessRunner.Json("instagram-reel")).Descriptor;
        d.Id.ShouldBe(id);
        d.Capabilities.ShouldBe(ProviderCapabilities.Resolve | ProviderCapabilities.Fulfill);
        d.Serves.Count.ShouldBe(1);
        d.Serves[0].Type.ShouldBe(type);
        d.Serves[0].Quota.ShouldBe(new QuotaDefault(3, "day", "client"));
        d.Attribution.ShouldBe((type == "audio" ? "Track from " : "Video from ") + d.Label);
    }

    [Fact]
    public async Task Resolve_MapsMetadataAndFallsBackToAnUploaderTitle()
    {
        var fake = FakeProcessRunner.Json("instagram-reel");
        var url = "https://www.instagram.com/reel/Cabc123xyz/";
        var item = await Make("instagram", fake).ResolveAsync(url, Ct);

        item.Native.ShouldBe(url);
        item.Type.ShouldBe("video");
        item.Title.ShouldBe("Instagram video by someone");
        item.Creator.ShouldBe(new ProviderCreator("someone", "https://www.instagram.com/someone"));
        item.DurationMs.ShouldBe(14_200);
        item.Width.ShouldBe(720);
        item.Height.ShouldBe(1280);
        item.ThumbSrc.ShouldBe("https://scontent.cdninstagram.com/v/t51/abc.jpg");
        item.License.ShouldBe(new ProviderLicense("Instagram Terms of Use", "https://help.instagram.com/581066165581870"));
        fake.Calls.Single().Args[^1].ShouldBe(url);
        fake.Calls.Single().Args[^2].ShouldBe("--");
    }

    [Fact]
    public async Task SoundCloud_ServesAudioNotVideo()
    {
        var item = await Make("soundcloud", FakeProcessRunner.Json("soundcloud-track")).ResolveAsync("https://soundcloud.com/an-artist/night-drive", Ct);

        item.Type.ShouldBe("audio");
        item.Title.ShouldBe("Night Drive");
        item.Creator.ShouldBe(new ProviderCreator("An Artist", "https://soundcloud.com/an-artist"));
        item.ThumbSrc.ShouldBe("https://i1.sndcdn.com/artworks-abc-t500x500.jpg");
    }

    [Fact]
    public void Title_FallsBackWithTheRightNoun()
    {
        var empty = new YtdlpInfo { Title = "  ", Uploader = "An Artist" };
        Make("soundcloud", FakeProcessRunner.Json("instagram-reel")).Title(empty).ShouldBe("SoundCloud track by An Artist");
        Make("tiktok", FakeProcessRunner.Json("instagram-reel")).Title(new YtdlpInfo { Uploader = "Someone" }).ShouldBe("TikTok video by Someone");
        Make("instagram", FakeProcessRunner.Json("instagram-reel")).Title(new YtdlpInfo()).ShouldBe("Instagram video");
        Make("instagram", FakeProcessRunner.Json("instagram-reel")).Title(new YtdlpInfo { Title = "A caption", Uploader = "x" }).ShouldBe("A caption");
    }

    [Fact]
    public async Task ForeignUrlsNeverReachTheExtractor()
    {
        var fake = FakeProcessRunner.Json("instagram-reel");
        var provider = Make("instagram", fake);
        var ex = await Should.ThrowAsync<StockException>(() => provider.GetItemAsync("video", "https://evil.test/x", Ct));
        ex.Reason.ShouldBe(StockReason.ItemMissing);
        ex = await Should.ThrowAsync<StockException>(() => provider.FetchAsync(new FulfillRequest("video", "http://169.254.169.254/", null), YtdlpFixtures.Workspace("/tmp"), Ct));
        ex.Reason.ShouldBe(StockReason.ItemMissing);
        ex = await Should.ThrowAsync<StockException>(() => provider.ResolveAsync("https://evil.test/x", Ct));
        ex.Reason.ShouldBe(StockReason.LinkUnsupported);
        fake.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Fetch_DownloadsAndAttributesTheCreator()
    {
        var dir = YtdlpFixtures.TempDir();
        try
        {
            var url = "https://www.instagram.com/reel/Cabc123xyz/";
            var file = Path.Combine(dir, "Cabc123xyz.mp4");
            var fake = new FakeProcessRunner(spec =>
            {
                if (spec.OnStdoutLine is null) return FakeProcessRunner.Ok(YtdlpFixtures.Read("instagram-reel"));
                File.WriteAllText(file, "x");
                spec.OnStdoutLine(file);
                return FakeProcessRunner.Ok("");
            });
            var progress = new List<double>();

            var asset = await Make("instagram", fake).FetchAsync(new FulfillRequest("video", url, null), YtdlpFixtures.Workspace(dir, progress), Ct);

            asset.Path.ShouldBe(file);
            asset.Mime.ShouldBe("video/mp4");
            asset.Filename.ShouldBe("Instagram_video_by_someone.mp4");
            asset.Attribution.ShouldBe("someone on Instagram");
            asset.License!.Name.ShouldBe("Instagram Terms of Use");
            progress.ShouldContain(0.1);
            var download = fake.Calls[1].Args;
            download[^1].ShouldBe(url);
            download[^2].ShouldBe("--");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task SoundCloudFetch_UsesTheAudioLadder()
    {
        var dir = YtdlpFixtures.TempDir();
        try
        {
            var file = Path.Combine(dir, "1234567.m4a");
            var fake = new FakeProcessRunner(spec =>
            {
                if (spec.OnStdoutLine is null) return FakeProcessRunner.Ok(YtdlpFixtures.Read("soundcloud-track"));
                File.WriteAllText(file, "x");
                spec.OnStdoutLine(file);
                return FakeProcessRunner.Ok("");
            });
            var asset = await Make("soundcloud", fake).FetchAsync(new FulfillRequest("audio", "https://soundcloud.com/an-artist/night-drive", null), YtdlpFixtures.Workspace(dir), Ct);

            asset.Mime.ShouldBe("audio/mp4");
            asset.Filename.ShouldBe("Night_Drive.m4a");
            var args = fake.Calls[1].Args;
            args[args.ToList().IndexOf("-f") + 1].ShouldBe("ba[ext=m4a]/ba/b");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
