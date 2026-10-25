using System.Diagnostics;
using System.Text;
using CutWire.Studio.Application.Stock;
using CutWire.Studio.Infrastructure.Stock;
using CutWire.Studio.Infrastructure.Stock.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace CutWire.Studio.UnitTests.Stock;

internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Func<ProcessSpec, ProcessOutput> _handler;
    public List<ProcessSpec> Calls { get; } = [];

    public FakeProcessRunner(Func<ProcessSpec, ProcessOutput> handler) => _handler = handler;

    public static FakeProcessRunner Json(string fixture) => new(_ => Ok(YtdlpFixtures.Read(fixture)));

    public static ProcessOutput Ok(string stdout) => new(0, Encoding.UTF8.GetBytes(stdout), "", false, false);

    public static ProcessOutput Fail(string stderr, int code = 1) => new(code, [], stderr, false, false);

    public Task<ProcessOutput> RunAsync(ProcessSpec spec, CancellationToken ct)
    {
        lock (Calls) Calls.Add(spec);
        return Task.FromResult(_handler(spec));
    }
}

internal static class YtdlpFixtures
{
    public static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "stock", "ytdlp", name + ".json"));

    public static YtdlpRunner Runner(IProcessRunner process, YtdlpOptions? options = null) => new(options ?? new YtdlpOptions(), "yt-dlp", process);

    public static StockWorkspace Workspace(string dir, List<double>? progress = null) =>
        new(dir, new HttpClient(), 1 << 30, progress is null ? null : progress.Add);

    public static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cw-ytdlp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}

public class YtdlpTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Extract_MapsMetadata()
    {
        var fake = FakeProcessRunner.Json("youtube-video");
        var info = await YtdlpFixtures.Runner(fake).ExtractAsync("https://www.youtube.com/watch?v=dQw4w9WgXcQ", Ct);

        info.Id.ShouldBe("dQw4w9WgXcQ");
        info.Title.ShouldBe("Never Gonna Give You Up");
        info.DurationMs.ShouldBe(212_000);
        info.WidthPx.ShouldBe(1920);
        info.IsLive.ShouldBeFalse();
        info.Creator().ShouldBe(("Rick Astley", "https://www.youtube.com/channel/UCuAXFkgsw1L7xaCfnd5JJOw"));
    }

    [Fact]
    public async Task BestThumbnail_PicksWidestUnder1280()
    {
        var info = await YtdlpFixtures.Runner(FakeProcessRunner.Json("youtube-video")).ExtractAsync("https://youtu.be/dQw4w9WgXcQ", Ct);
        info.BestThumbnail().ShouldBe("https://i.ytimg.com/vi/dQw4w9WgXcQ/sddefault.jpg");

        var sc = await YtdlpFixtures.Runner(FakeProcessRunner.Json("soundcloud-track")).ExtractAsync("https://soundcloud.com/a/b", Ct);
        sc.BestThumbnail().ShouldBe("https://i1.sndcdn.com/artworks-abc-t500x500.jpg");
    }

    [Fact]
    public async Task BestThumbnail_FallsBackToSingleThumbnail()
    {
        var info = await YtdlpFixtures.Runner(FakeProcessRunner.Json("instagram-reel")).ExtractAsync("https://www.instagram.com/reel/Cabc123xyz/", Ct);
        info.BestThumbnail().ShouldBe("https://scontent.cdninstagram.com/v/t51/abc.jpg");
    }

    [Fact]
    public async Task Extract_PassesTheUrlAfterDoubleDash()
    {
        var fake = FakeProcessRunner.Json("youtube-video");
        await YtdlpFixtures.Runner(fake).ExtractAsync("https://www.youtube.com/watch?v=dQw4w9WgXcQ", Ct);

        var args = fake.Calls.Single().Args;
        args[^1].ShouldBe("https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        args[^2].ShouldBe("--");
        args.ShouldContain("--ignore-config");
        args.ShouldContain("-J");
        args.ShouldContain("--no-playlist");
        args.ShouldNotContain("-6");
        args.ShouldNotContain("--cookies");
        args.ShouldNotContain("--extractor-args");
        args.ShouldNotContain("--plugin-dirs");
    }

    [Fact]
    public async Task Extract_WithoutIdIsItemMissing()
    {
        var ex = await Should.ThrowAsync<StockException>(() =>
            YtdlpFixtures.Runner(FakeProcessRunner.Json("no-id")).ExtractAsync("https://example.com/v", Ct));
        ex.Reason.ShouldBe(StockReason.ItemMissing);
    }

    [Fact]
    public async Task Extract_GarbageIsSourceError()
    {
        var ex = await Should.ThrowAsync<StockException>(() =>
            YtdlpFixtures.Runner(new FakeProcessRunner(_ => FakeProcessRunner.Ok("not json"))).ExtractAsync("https://example.com/v", Ct));
        ex.Reason.ShouldBe(StockReason.SourceError);
    }

    [Fact]
    public async Task Flat_TrimsToLimitAndUsesPlaylistItems()
    {
        var fake = FakeProcessRunner.Json("youtube-search");
        var entries = await YtdlpFixtures.Runner(fake).FlatAsync("ytsearch2:cats", 2, Ct);

        entries.Count.ShouldBe(2);
        var args = fake.Calls.Single().Args;
        args.ShouldContain("--flat-playlist");
        args[args.ToList().IndexOf("--playlist-items") + 1].ShouldBe("1:2");
        args[^2].ShouldBe("--");
        args[^1].ShouldBe("ytsearch2:cats");
    }

    [Fact]
    public async Task Metadata_IsCached()
    {
        var fake = FakeProcessRunner.Json("youtube-video");
        var runner = YtdlpFixtures.Runner(fake);
        await runner.ExtractAsync("https://youtu.be/dQw4w9WgXcQ", Ct);
        await runner.ExtractAsync("https://youtu.be/dQw4w9WgXcQ", Ct);
        fake.Calls.Count.ShouldBe(1);

        await runner.ExtractAsync("https://youtu.be/9bZkp7q19f0", Ct);
        fake.Calls.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Failures_AreNotCached()
    {
        var calls = 0;
        var fake = new FakeProcessRunner(_ => ++calls == 1 ? FakeProcessRunner.Fail("ERROR: HTTP Error 429") : FakeProcessRunner.Ok(YtdlpFixtures.Read("youtube-video")));
        var runner = YtdlpFixtures.Runner(fake);
        await Should.ThrowAsync<StockException>(() => runner.ExtractAsync("https://youtu.be/dQw4w9WgXcQ", Ct));
        (await runner.ExtractAsync("https://youtu.be/dQw4w9WgXcQ", Ct)).Id.ShouldBe("dQw4w9WgXcQ");
    }

    [Fact]
    public async Task Options_AreTranslatedToFlags()
    {
        var plugins = YtdlpFixtures.TempDir();
        try
        {
            var options = new YtdlpOptions
            {
                CookiesFile = "/tmp/cookies.txt",
                ExtractorArgs = "youtube:player_client=web",
                PluginDirs = string.Join(Path.PathSeparator, plugins, "/definitely/not/here"),
                PotBaseUrl = "http://bgutil:4416",
                ForceIPv6 = true,
            };
            var fake = FakeProcessRunner.Json("youtube-video");
            await YtdlpFixtures.Runner(fake, options).ExtractAsync("https://youtu.be/dQw4w9WgXcQ", Ct);

            var args = fake.Calls.Single().Args.ToList();
            args.ShouldContain("-6");
            args[args.IndexOf("--cookies") + 1].ShouldBe("/tmp/cookies.txt");
            args.Count(a => a == "--plugin-dirs").ShouldBe(1);
            args[args.IndexOf("--plugin-dirs") + 1].ShouldBe(plugins);
            var extractor = args.Select((a, i) => (a, i)).Where(x => x.a == "--extractor-args").Select(x => args[x.i + 1]).ToList();
            extractor.ShouldBe(["youtubepot-bgutilhttp:base_url=http://bgutil:4416", "youtube:player_client=web"]);
        }
        finally
        {
            Directory.Delete(plugins);
        }
    }

    [Theory]
    [InlineData("-J")]
    [InlineData("--exec=id")]
    [InlineData("  --exec=id")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/a")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:pw@example.com/a")]
    [InlineData("https://example.com/a\n--exec=id")]
    [InlineData("https://example.com/a\\b")]
    [InlineData("example.com/a")]
    [InlineData("")]
    [InlineData("ytsearch:cats")]
    [InlineData("ytsearchx:cats")]
    [InlineData("ytsearch5:")]
    [InlineData("ytsearch1000:cats")]
    public async Task Targets_ThatCouldBecomeOptionsOrOtherSchemes_AreRejected(string target)
    {
        var fake = FakeProcessRunner.Json("youtube-video");
        var ex = await Should.ThrowAsync<StockException>(() => YtdlpFixtures.Runner(fake).ExtractAsync(target, Ct));
        ex.Reason.ShouldBe(StockReason.LinkUnsupported);
        fake.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void Targets_AllowHttpUrlsAndSearches()
    {
        YtdlpRunner.CleanTarget("https://example.com/a?b=c").ShouldBe("https://example.com/a?b=c");
        YtdlpRunner.CleanTarget("http://example.com/a").ShouldBe("http://example.com/a");
        YtdlpRunner.CleanTarget("ytsearch20:lofi -beats").ShouldBe("ytsearch20:lofi -beats");
        Should.Throw<StockException>(() => YtdlpRunner.CleanTarget("ytsearch5:" + new string('a', 201)));
    }

    [Theory]
    [InlineData("ERROR: [youtube] x: Sign in to confirm you're not a bot. Use --cookies-from-browser", StockReason.SourceBlocked)]
    [InlineData("ERROR: HTTP Error 429: Too Many Requests", StockReason.SourceBlocked)]
    [InlineData("ERROR: Your IP address is blocked from accessing this post", StockReason.SourceBlocked)]
    [InlineData("ERROR: [youtube] x: Sign in to confirm your age. This video may be inappropriate for some users.", StockReason.ItemPrivate)]
    [InlineData("ERROR: [youtube] x: This video is age-restricted", StockReason.ItemPrivate)]
    [InlineData("ERROR: [youtube] x: Private video. Sign in if you've been granted access to this video", StockReason.ItemPrivate)]
    [InlineData("ERROR: [instagram] x: Instagram sent an empty media response. Check if this post is accessible in your browser without being logged-in. Use --cookies", StockReason.ItemPrivate)]
    [InlineData("ERROR: [youtube] x: Join this channel to get access to members-only content", StockReason.ItemPrivate)]
    [InlineData("ERROR: [youtube] x: The uploader has not made this video available in your country", StockReason.ItemGeoblocked)]
    [InlineData("ERROR: [youtube] x: Video unavailable. The uploader has blocked it in your country on copyright grounds", StockReason.ItemGeoblocked)]
    [InlineData("ERROR: [youtube] x: Video unavailable", StockReason.ItemRemoved)]
    [InlineData("ERROR: [youtube] x: Video unavailable. This video has been removed by the uploader", StockReason.ItemRemoved)]
    [InlineData("ERROR: [youtube] Incomplete YouTube ID abc. URL looks truncated.", StockReason.ItemRemoved)]
    [InlineData("ERROR: [facebook] x: Cannot parse data; the post not found", StockReason.ItemRemoved)]
    [InlineData("ERROR: Unsupported URL: https://example.com/", StockReason.LinkNoMedia)]
    [InlineData("ERROR: [generic] No video could be found in this page", StockReason.LinkNoMedia)]
    [InlineData("ERROR: [youtube] x: This live event will begin in 3 hours.", StockReason.LiveStream)]
    [InlineData("ERROR: Unable to download webpage: <urlopen error [Errno -3] Temporary failure in name resolution>", StockReason.SourceUnreachable)]
    [InlineData("ERROR: Unable to download webpage: The read operation timed out", StockReason.SourceUnreachable)]
    [InlineData("ERROR: something nobody has seen before", StockReason.SourceError)]
    [InlineData("", StockReason.SourceError)]
    public void Stderr_IsClassified(string stderr, StockReason expected) => YtdlpErrors.Reason(stderr).ShouldBe(expected);

    [Fact]
    public async Task NonZeroExit_IsClassifiedThroughTheRunner()
    {
        var fake = new FakeProcessRunner(_ => FakeProcessRunner.Fail("ERROR: [youtube] x: Video unavailable"));
        var ex = await Should.ThrowAsync<StockException>(() => YtdlpFixtures.Runner(fake).ExtractAsync("https://youtu.be/dQw4w9WgXcQ", Ct));
        ex.Reason.ShouldBe(StockReason.ItemRemoved);
        ex.Message.ShouldContain("Video unavailable");
    }

    [Fact]
    public async Task Timeout_IsSourceTimeoutEvenWithoutStderr()
    {
        var fake = new FakeProcessRunner(_ => new ProcessOutput(-1, [], "", false, true));
        var ex = await Should.ThrowAsync<StockException>(() => YtdlpFixtures.Runner(fake).ExtractAsync("https://youtu.be/dQw4w9WgXcQ", Ct));
        ex.Reason.ShouldBe(StockReason.SourceTimeout);
    }

    [Fact]
    public async Task OversizedOutput_IsSourceError()
    {
        var fake = new FakeProcessRunner(_ => new ProcessOutput(-1, [], "", true, false));
        var ex = await Should.ThrowAsync<StockException>(() => YtdlpFixtures.Runner(fake).ExtractAsync("https://youtu.be/dQw4w9WgXcQ", Ct));
        ex.Reason.ShouldBe(StockReason.SourceError);
    }

    [Fact]
    public async Task MetadataCalls_CarryTheMetadataLimits()
    {
        var fake = FakeProcessRunner.Json("youtube-video");
        await YtdlpFixtures.Runner(fake).ExtractAsync("https://youtu.be/dQw4w9WgXcQ", Ct);
        var spec = fake.Calls.Single();
        spec.StdoutLimit.ShouldBe(YtdlpRunner.MaxMetaBytes);
        spec.StderrLimit.ShouldBe(YtdlpRunner.MaxStderrBytes);
        spec.Timeout.ShouldBeLessThanOrEqualTo(YtdlpRunner.MetaTimeout);
    }

    [Fact]
    public async Task AtMostFourExtractionsRunAtOnce()
    {
        var running = 0;
        var peak = 0;
        var gate = new object();
        var fake = new FakeProcessRunner(_ =>
        {
            lock (gate) peak = Math.Max(peak, ++running);
            Thread.Sleep(50);
            lock (gate) running--;
            return FakeProcessRunner.Ok(YtdlpFixtures.Read("youtube-video"));
        });
        var runner = YtdlpFixtures.Runner(fake);
        await Task.WhenAll(Enumerable.Range(0, 12).Select(i => Task.Run(() => runner.ExtractAsync($"https://example.com/{i}", Ct), Ct)));
        peak.ShouldBeLessThanOrEqualTo(YtdlpRunner.MaxConcurrent);
        peak.ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task Download_ReportsProgressAndReturnsTheWrittenFile()
    {
        var dir = YtdlpFixtures.TempDir();
        try
        {
            var file = Path.Combine(dir, "dQw4w9WgXcQ.mp4");
            var fake = new FakeProcessRunner(spec =>
            {
                File.WriteAllText(file, "x");
                spec.OnStdoutLine!("cwprog:100/1000/NA");
                spec.OnStdoutLine("cwprog:500/None/1000");
                spec.OnStdoutLine("cwprog:50/1000/NA");
                spec.OnStdoutLine("cwprog:NA/NA/NA");
                spec.OnStdoutLine(file);
                return FakeProcessRunner.Ok("");
            });
            var progress = new List<double>();

            var path = await YtdlpFixtures.Runner(fake).DownloadAsync("https://youtu.be/dQw4w9WgXcQ", YtdlpFixtures.Workspace(dir, progress), "ba/b", Ct);

            path.ShouldBe(file);
            progress.Count.ShouldBe(2);
            progress.ShouldAllBe(p => p >= 0.1 && p <= 0.66);
            progress[0].ShouldBe(0.1 + 0.1 * 0.56, 1e-9);
            progress[1].ShouldBe(0.1 + 0.5 * 0.56, 1e-9);
            var args = fake.Calls.Single().Args;
            args[^2].ShouldBe("--");
            args[^1].ShouldBe("https://youtu.be/dQw4w9WgXcQ");
            args.ShouldNotContain("--no-progress");
            args.ShouldNotContain("--merge-output-format");
            args[args.ToList().IndexOf("-o") + 1].ShouldStartWith(dir);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Download_MergesIntoMp4WhenFormatJoinsStreams()
    {
        var dir = YtdlpFixtures.TempDir();
        try
        {
            var file = Path.Combine(dir, "a.mp4");
            var fake = new FakeProcessRunner(spec =>
            {
                File.WriteAllText(file, "x");
                spec.OnStdoutLine!(file);
                return FakeProcessRunner.Ok("");
            });
            await YtdlpFixtures.Runner(fake).DownloadAsync("https://youtu.be/dQw4w9WgXcQ", YtdlpFixtures.Workspace(dir), "bv*+ba/b", Ct);
            var args = fake.Calls.Single().Args.ToList();
            args[args.IndexOf("--merge-output-format") + 1].ShouldBe("mp4");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Download_RejectsAPathOutsideTheWorkspace()
    {
        var dir = YtdlpFixtures.TempDir();
        var outside = Path.Combine(Path.GetTempPath(), "cw-outside-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(outside, "x");
        try
        {
            var fake = new FakeProcessRunner(spec =>
            {
                spec.OnStdoutLine!(outside);
                return FakeProcessRunner.Ok("");
            });
            var ex = await Should.ThrowAsync<StockException>(() =>
                YtdlpFixtures.Runner(fake).DownloadAsync("https://youtu.be/dQw4w9WgXcQ", YtdlpFixtures.Workspace(dir), "b", Ct));
            ex.Reason.ShouldBe(StockReason.DownloadError);
        }
        finally
        {
            Directory.Delete(dir, true);
            File.Delete(outside);
        }
    }

    [Fact]
    public void FileWithin_RejectsEscapesSymlinksAndMissingFiles()
    {
        var dir = YtdlpFixtures.TempDir();
        try
        {
            var inside = Path.Combine(dir, "ok.mp4");
            File.WriteAllText(inside, "x");
            YtdlpRunner.FileWithin(dir, inside).ShouldBe(inside);

            Should.Throw<StockException>(() => YtdlpRunner.FileWithin(dir, null));
            Should.Throw<StockException>(() => YtdlpRunner.FileWithin(dir, ""));
            Should.Throw<StockException>(() => YtdlpRunner.FileWithin(dir, Path.Combine(dir, "missing.mp4")));
            Should.Throw<StockException>(() => YtdlpRunner.FileWithin(dir, Path.Combine(dir, "..", "x")));
            Should.Throw<StockException>(() => YtdlpRunner.FileWithin(dir, "/etc/passwd"));
            Should.Throw<StockException>(() => YtdlpRunner.FileWithin(dir, dir));

            if (!OperatingSystem.IsWindows())
            {
                var link = Path.Combine(dir, "link.mp4");
                File.CreateSymbolicLink(link, inside);
                Should.Throw<StockException>(() => YtdlpRunner.FileWithin(dir, link));
            }
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Hosts_AreExactMatchOnly()
    {
        YtdlpHosts.Owns("youtube", "https://www.youtube.com/watch?v=x").ShouldBeTrue();
        YtdlpHosts.Owns("youtube", "https://WWW.YouTube.com/watch?v=x").ShouldBeTrue();
        YtdlpHosts.Owns("youtube", "https://youtube.com.evil.test/").ShouldBeFalse();
        YtdlpHosts.Owns("youtube", "https://evil@www.youtube.com/").ShouldBeFalse();
        YtdlpHosts.Owns("youtube", "ftp://www.youtube.com/").ShouldBeFalse();
        YtdlpHosts.Owns("nope", "https://www.youtube.com/").ShouldBeFalse();
        YtdlpHosts.Owns("youtube", null).ShouldBeFalse();
    }

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>("Stock:Providers:Ytdlp:" + v.Key, v.Value))).Build();

    private static readonly string[] AllIds = ["youtube", "youtube_music", "instagram", "facebook", "tiktok", "vimeo", "x", "soundcloud"];

    [Fact]
    public void Registration_WithMissingBinary_RegistersUnavailableProviders()
    {
        var services = new ServiceCollection();
        YtdlpRegistration.Register(services, Config(("Path", "/definitely/not/here/yt-dlp")));
        using var provider = services.BuildServiceProvider();

        provider.GetServices<IStockProvider>().ShouldBeEmpty();
        var unavailable = provider.GetServices<UnavailableProvider>().ToList();
        unavailable.Select(u => u.Id).ShouldBe(AllIds, ignoreOrder: true);
        unavailable.ShouldAllBe(u => u.Reason == "yt-dlp not found (looked for /definitely/not/here/yt-dlp)");

        var youtube = unavailable.Single(u => u.Id == "youtube");
        youtube.OwnsUrl("https://youtu.be/dQw4w9WgXcQ").ShouldBeTrue();
        youtube.OwnsUrl("https://music.youtube.com/watch?v=dQw4w9WgXcQ").ShouldBeFalse();
        unavailable.Single(u => u.Id == "youtube_music").OwnsUrl("https://music.youtube.com/watch?v=dQw4w9WgXcQ").ShouldBeTrue();
        unavailable.Single(u => u.Id == "x").OwnsUrl("https://twitter.com/a/status/1").ShouldBeTrue();
        unavailable.ShouldAllBe(u => !u.OwnsUrl("https://evil.test/"));
    }

    [Fact]
    public void Registration_WhenDisabled_RegistersNothing()
    {
        var services = new ServiceCollection();
        YtdlpRegistration.Register(services, Config(("Enabled", "false"), ("Path", "/definitely/not/here/yt-dlp")));
        services.ShouldBeEmpty();
    }

    [Fact]
    public void Registration_WithBinary_RegistersAllProvidersSharingOneRunner()
    {
        if (!File.Exists("/bin/sh")) Assert.Skip("/bin/sh is not available");
        var dir = YtdlpFixtures.TempDir();
        try
        {
            var script = FakeScript.Write(dir, "echo '{}'");
            var services = new ServiceCollection();
            YtdlpRegistration.Register(services, Config(("Path", script)));
            using var provider = services.BuildServiceProvider();

            provider.GetServices<UnavailableProvider>().ShouldBeEmpty();
            var providers = provider.GetServices<IStockProvider>().ToList();
            providers.Select(p => p.Descriptor.Id).ShouldBe(AllIds, ignoreOrder: true);
            providers.ShouldAllBe(p => p.Descriptor.Capabilities.HasFlag(ProviderCapabilities.Resolve | ProviderCapabilities.Fulfill));
            provider.GetRequiredService<YtdlpRunner>().ShouldBeSameAs(provider.GetRequiredService<YtdlpRunner>());
            providers.Select(p => p.Descriptor.Id).Distinct().Count().ShouldBe(8);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ExecutableLocator_FindsOnPathAndExplicitPaths()
    {
        if (!File.Exists("/bin/sh")) Assert.Skip("/bin/sh is not available");
        ExecutableLocator.Find("/bin/sh").ShouldBe("/bin/sh");
        ExecutableLocator.Find("sh").ShouldNotBeNull();
        ExecutableLocator.Find("cutwire-no-such-binary").ShouldBeNull();
        ExecutableLocator.Find("/definitely/not/here").ShouldBeNull();
        ExecutableLocator.Find("").ShouldBeNull();
        ExecutableLocator.Find(null).ShouldBeNull();
    }
}

internal static class FakeScript
{
    public static string Write(string dir, string body)
    {
        var path = Path.Combine(dir, "fake-ytdlp");
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
}

public class RealProcessYtdlpTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void RequireSh()
    {
        if (!File.Exists("/bin/sh")) Assert.Skip("/bin/sh is not available, so the fake yt-dlp script cannot run");
    }

    [Fact]
    public async Task RealSpawn_ReadsFixtureJsonFromAFakeExecutable()
    {
        RequireSh();
        var dir = YtdlpFixtures.TempDir();
        try
        {
            var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "stock", "ytdlp", "youtube-video.json");
            var script = FakeScript.Write(dir, $"echo \"$@\" > '{dir}/args.txt'\ncat '{fixture}'");
            var runner = new YtdlpRunner(new YtdlpOptions(), script, new SystemProcessRunner());

            var info = await runner.ExtractAsync("https://youtu.be/dQw4w9WgXcQ", Ct);

            info.Id.ShouldBe("dQw4w9WgXcQ");
            (await File.ReadAllTextAsync(Path.Combine(dir, "args.txt"), Ct)).ShouldContain("-- https://youtu.be/dQw4w9WgXcQ");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task RealSpawn_NonZeroExitIsClassifiedFromStderr()
    {
        RequireSh();
        var dir = YtdlpFixtures.TempDir();
        try
        {
            var script = FakeScript.Write(dir, "echo 'ERROR: [youtube] x: Video unavailable' >&2\nexit 1");
            var runner = new YtdlpRunner(new YtdlpOptions(), script, new SystemProcessRunner());
            var ex = await Should.ThrowAsync<StockException>(() => runner.ExtractAsync("https://youtu.be/dQw4w9WgXcQ", Ct));
            ex.Reason.ShouldBe(StockReason.ItemRemoved);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task RealSpawn_TimeoutKillsTheWholeProcessTree()
    {
        RequireSh();
        var dir = YtdlpFixtures.TempDir();
        try
        {
            var script = FakeScript.Write(dir, $"sleep 300 &\necho $! > '{dir}/child.pid'\nwait");
            var runner = new YtdlpRunner(new YtdlpOptions(), script, new SystemProcessRunner(), metaTimeout: TimeSpan.FromSeconds(1));

            var clock = Stopwatch.StartNew();
            var ex = await Should.ThrowAsync<StockException>(() => runner.ExtractAsync("https://youtu.be/dQw4w9WgXcQ", Ct));

            ex.Reason.ShouldBe(StockReason.SourceTimeout);
            clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(20));
            var pid = int.Parse((await File.ReadAllTextAsync(Path.Combine(dir, "child.pid"), Ct)).Trim());
            await AssertGone(pid);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task RealSpawn_CallerCancellationKillsTheProcessAndThrows()
    {
        RequireSh();
        var dir = YtdlpFixtures.TempDir();
        try
        {
            var script = FakeScript.Write(dir, $"sleep 300 &\necho $! > '{dir}/child.pid'\nwait");
            var runner = new YtdlpRunner(new YtdlpOptions(), script, new SystemProcessRunner());
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            cts.CancelAfter(TimeSpan.FromMilliseconds(700));

            await Should.ThrowAsync<OperationCanceledException>(() => runner.ExtractAsync("https://youtu.be/dQw4w9WgXcQ", cts.Token));

            var pid = int.Parse((await File.ReadAllTextAsync(Path.Combine(dir, "child.pid"), Ct)).Trim());
            await AssertGone(pid);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task RealSpawn_OversizedStdoutIsRejectedAndKilled()
    {
        RequireSh();
        var dir = YtdlpFixtures.TempDir();
        try
        {
            var script = FakeScript.Write(dir, "yes '{\"id\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}'");
            var runner = new YtdlpRunner(new YtdlpOptions(), script, new SystemProcessRunner());
            var ex = await Should.ThrowAsync<StockException>(() => runner.ExtractAsync("https://youtu.be/dQw4w9WgXcQ", Ct));
            ex.Reason.ShouldBe(StockReason.SourceError);
            ex.Message.ShouldContain("cap");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task RealSpawn_MissingBinaryIsSourceError()
    {
        var runner = new YtdlpRunner(new YtdlpOptions(), "/definitely/not/here/yt-dlp", new SystemProcessRunner());
        var ex = await Should.ThrowAsync<StockException>(() => runner.ExtractAsync("https://youtu.be/dQw4w9WgXcQ", Ct));
        ex.Reason.ShouldBe(StockReason.SourceError);
    }

    private static async Task AssertGone(int pid)
    {
        for (var i = 0; i < 50; i++)
        {
            if (!Directory.Exists($"/proc/{pid}") || IsZombie(pid)) return;
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
        Directory.Exists($"/proc/{pid}").ShouldBeFalse("the child process survived the kill");
    }

    private static bool IsZombie(int pid)
    {
        try { return File.ReadAllText($"/proc/{pid}/stat").Contains(") Z "); }
        catch (IOException) { return true; }
    }
}
