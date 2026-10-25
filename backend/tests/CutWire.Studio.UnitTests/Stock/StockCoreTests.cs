using System.Net;
using CutWire.Studio.Application.Stock;
using CutWire.Studio.Domain.Stock;
using CutWire.Studio.Infrastructure.Stock;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace CutWire.Studio.UnitTests.Stock;

public class ItemIdTests
{
    [Fact]
    public void RoundTrips_AndKeepsColonsInTheNativeId()
    {
        var id = ItemIds.Encode("pexels", "video", "a:b:c/d?e=f");

        ItemIds.TryDecode(id, out var r).ShouldBeTrue();
        r.ShouldBe(new ItemRef("pexels", "video", "a:b:c/d?e=f"));
        id.ShouldNotContain("=");
        id.ShouldNotContain("+");
        id.ShouldNotContain("/");
    }

    [Theory]
    [InlineData("")]
    [InlineData("!!!")]
    [InlineData("cGV4ZWxz")]
    [InlineData("OjpwZXhlbHM")]
    public void RejectsMalformedIds(string id) => ItemIds.TryDecode(id, out _).ShouldBeFalse();

    [Fact]
    public void RejectsProviderOrTypeContainingTheSeparator() =>
        Should.Throw<ArgumentException>(() => ItemIds.Encode("a:b", "video", "1"));
}

public class RangeFilterTests
{
    [Theory]
    [InlineData("10-60", 10.0, 60.0)]
    [InlineData("10..60", 10.0, 60.0)]
    [InlineData("10 to 60", 10.0, 60.0)]
    [InlineData("10–60", 10.0, 60.0)]
    [InlineData("60-10", 10.0, 60.0)]
    [InlineData(" 10 - 60 ", 10.0, 60.0)]
    [InlineData("10-", 10.0, null)]
    [InlineData("-60", null, 60.0)]
    [InlineData("30", 30.0, 30.0)]
    [InlineData("1.5-2.5", 1.5, 2.5)]
    [InlineData("10s-60s", 10.0, 60.0)]
    public void ParsesLeniently(string raw, double? min, double? max) => RangeFilter.Parse(raw).ShouldBe(new RangeFilter(min, max));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("abc")]
    [InlineData("-")]
    public void UnparsableIsEmpty(string? raw) => RangeFilter.Parse(raw).IsEmpty.ShouldBeTrue();
}

public class QuotaWindowTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 13, 45, 12, TimeSpan.FromHours(2));

    [Fact]
    public void DayWindowStartsAtUtcMidnight()
    {
        StockQuota.WindowStart("day", Now).ShouldBe(new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero));
        StockQuota.WindowEnd("day", Now).ShouldBe(new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void HourWindowTruncatesToTheUtcHour()
    {
        StockQuota.WindowStart("hour", Now).ShouldBe(new DateTimeOffset(2026, 10, 25, 11, 0, 0, TimeSpan.Zero));
        StockQuota.WindowEnd("hour", Now).ShouldBe(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void AccountScopeUsesTheUserOnlyWhenSignedIn()
    {
        var user = Guid.NewGuid();
        var account = new ProviderSetting { QuotaScope = "account" };
        var client = new ProviderSetting { QuotaScope = "client" };

        StockQuota.SubjectFor(account, new StockCaller("i:abc", user)).ShouldBe("u:" + user);
        StockQuota.SubjectFor(account, new StockCaller("i:abc", null)).ShouldBe("i:abc");
        StockQuota.SubjectFor(client, new StockCaller("i:abc", user)).ShouldBe("i:abc");
    }

    [Fact]
    public void QuotaStatusIsExhaustedOnlyWhenLimitedAndNothingRemains()
    {
        new QuotaStatus(2, 2, 0, "day", Now).Exhausted.ShouldBeTrue();
        new QuotaStatus(2, 1, 1, "day", Now).Exhausted.ShouldBeFalse();
        new QuotaStatus(0, 0, 0, "day", Now).Exhausted.ShouldBeFalse();
    }
}

public class FilterCleaningTests
{
    private static readonly StockService Service = new("video",
    [
        new FilterDef("orientation", "Orientation", FilterKinds.Select, [new FilterOption("landscape", "L"), new FilterOption("portrait", "P")]),
        new FilterDef("hd", "HD", FilterKinds.Toggle),
        new FilterDef("duration", "Duration", FilterKinds.Range),
    ]);

    [Fact]
    public void KeepsOnlyDeclaredAndValidValues()
    {
        var clean = StockBrowse.CleanFilters(Service, new Dictionary<string, string>
        {
            ["orientation"] = "landscape", ["hd"] = "true", ["duration"] = "10-60", ["extra"] = "x",
        });

        clean.ShouldBe(new Dictionary<string, string> { ["orientation"] = "landscape", ["hd"] = "1", ["duration"] = "10-60" });
    }

    [Fact]
    public void DropsUnknownOptionsFalseTogglesAndBlanks()
    {
        StockBrowse.CleanFilters(Service, new Dictionary<string, string> { ["orientation"] = "diagonal", ["hd"] = "0", ["duration"] = " " }).ShouldBeEmpty();
    }
}

public class SfxIndexTests
{
    private const string Json = """
        {
          "schema": 1,
          "categories": [
            {"id": "ui", "label": "UI", "subcategories": [{"id": "click", "label": "Clicks"}]},
            {"id": "empty", "label": "Empty", "subcategories": []}
          ],
          "sounds": [
            {"id": "a", "name": "Soft click", "category": "ui", "subcategory": "click", "duration": 0.4256, "size": 1234, "path": "ui/a.flac", "preview": "ab12.opus"},
            {"id": "evil", "name": "x", "category": "ui", "subcategory": "click", "duration": 1, "size": 1, "path": "../secret.flac", "preview": "p.opus"},
            {"id": "abs", "name": "x", "category": "ui", "subcategory": "click", "duration": 1, "size": 1, "path": "/etc/passwd", "preview": "p.opus"}
          ]
        }
        """;

    [Fact]
    public void MapsSoundsAndDropsUnsafePathsAndEmptyCategories()
    {
        var data = SfxIndex.Parse(Json, "v1");

        data.Version.ShouldBe("v1");
        var ui = data.Categories.ShouldHaveSingleItem();
        ui.Id.ShouldBe("ui");
        var sound = ui.Sounds.ShouldHaveSingleItem();
        sound.Title.ShouldBe("Soft click");
        sound.Tag.ShouldBe("Clicks");
        sound.DurationMs.ShouldBe(426);
        sound.LibraryPath.ShouldBe("ui/a.flac");
        sound.PreviewPath.ShouldBe("ab12.opus");
    }

    [Theory]
    [InlineData("a/b.flac", true)]
    [InlineData("", false)]
    [InlineData("../a", false)]
    [InlineData("a/../b", false)]
    [InlineData("/a", false)]
    [InlineData("a//b", false)]
    [InlineData("a\\b", false)]
    public void SafeRel(string path, bool safe) => SfxIndex.SafeRel(path).ShouldBe(safe);

    [Fact]
    public void FileSourceReloadsWhenTheIndexChanges()
    {
        var dir = Directory.CreateTempSubdirectory("sfx-test-").FullName;
        try
        {
            var source = new FileSfxSource(Options.Create(new SfxOptions { Path = dir }), NullLogger<FileSfxSource>.Instance);
            source.Load().Categories.ShouldBeEmpty();

            File.WriteAllText(Path.Combine(dir, "sfx.json"), Json);
            var first = source.Load();
            first.Categories.Count.ShouldBe(1);

            File.WriteAllText(Path.Combine(dir, "sfx.json"), Json.Replace("Soft click", "Hard click"));
            File.SetLastWriteTimeUtc(Path.Combine(dir, "sfx.json"), DateTime.UtcNow.AddMinutes(1));
            var second = source.Load();
            second.Categories[0].Sounds[0].Title.ShouldBe("Hard click");
            second.Version.ShouldNotBe(first.Version);

            File.WriteAllText(Path.Combine(dir, "sfx.json"), "{ broken");
            File.SetLastWriteTimeUtc(Path.Combine(dir, "sfx.json"), DateTime.UtcNow.AddMinutes(2));
            source.Load().Categories[0].Sounds[0].Title.ShouldBe("Hard click");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FileSourceResolveRefusesTraversal()
    {
        var dir = Directory.CreateTempSubdirectory("sfx-test-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "library"));
            File.WriteAllText(Path.Combine(dir, "library", "a.flac"), "x");
            File.WriteAllText(Path.Combine(dir, "sfx.json"), "{}");
            var source = new FileSfxSource(Options.Create(new SfxOptions { Path = dir }), NullLogger<FileSfxSource>.Instance);

            source.Resolve("library", "a.flac").ShouldNotBeNull();
            source.Resolve("library", "../sfx.json").ShouldBeNull();
            source.Resolve("library", "missing.flac").ShouldBeNull();
            source.Resolve("other", "a.flac").ShouldBeNull();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

public class DriftIndexTests
{
    private static string Sha(char c) => new(c, 64);

    private static string Index(string extra = "", string version = "1.2.0") => $$"""
        {
          "schema": 1, "version": "{{version}}",
          "categories": [{"id": "memes", "label": "Memes", "kind": "lottie", "sort": 2}],
          "assets": [{
            "id": "party-hat", "name": "Party hat", "kind": "lottie", "category": "memes", "path": "memes/party-hat",
            "files": {
              "main": {"name": "main.json", "size": 10, "sha256": "{{Sha('a')}}"},
              "thumbnail": {"name": "t.webp", "size": 5, "sha256": "{{Sha('b')}}"},
              "preview": {"name": "p.webp", "size": 5, "sha256": "{{Sha('c')}}"}
            }{{extra}}
          }]
        }
        """;

    [Fact]
    public void ParsesAValidIndexAndListsEveryFile()
    {
        var index = DriftIndex.Parse(Index());

        index.Version.ShouldBe("1.2.0");
        index.Files().Keys.ShouldBe(["memes/party-hat/main.json", "memes/party-hat/t.webp", "memes/party-hat/p.webp"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("""{"schema": 2, "version": "1", "categories": [], "assets": []}""")]
    [InlineData("""{"schema": 1, "version": "../x", "categories": [], "assets": []}""")]
    [InlineData("not json")]
    public void RejectsBrokenIndexes(string json) => Should.Throw<FormatException>(() => DriftIndex.Parse(json));

    [Fact]
    public void RejectsPathsThatEscapeOrBadHashes()
    {
        Should.Throw<FormatException>(() => DriftIndex.Parse(Index().Replace("memes/party-hat", "../party-hat")));
        Should.Throw<FormatException>(() => DriftIndex.Parse(Index().Replace(Sha('a'), "zz")));
        Should.Throw<FormatException>(() => DriftIndex.Parse(Index().Replace("main.json", "a/main.json")));
    }

    [Fact]
    public void RejectsUnknownCategory() =>
        Should.Throw<FormatException>(() => DriftIndex.Parse(Index().Replace("\"category\": \"memes\"", "\"category\": \"nope\"")));

    [Theory]
    [InlineData("object", "model3d")]
    [InlineData("lottie", "lottie")]
    [InlineData("face-prop", "face-prop")]
    public void MapsKinds(string kind, string expected) => DriftAssetsCatalog.KindFor(kind).ShouldBe(expected);
}

public class MediaTests
{
    private static MediaSealer Sealer(string key = "k", TimeProvider? _ = null, FakeClock? clock = null) =>
        new(Options.Create(new StockMediaOptions { SealKey = key }), clock ?? new FakeClock(), NullLogger<MediaSealer>.Instance);

    private sealed class FakeClock : CutWire.Studio.Application.Abstractions.IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    }

    [Fact]
    public void SealOpenRoundTripsAndIsDeterministic()
    {
        var sealer = Sealer();
        var claims = new MediaClaims("https://origin.example/a.mp4", null, null, "video/mp4", DateTimeOffset.Parse("2026-10-26T00:00:00Z").ToUnixTimeSeconds());

        var token = sealer.Seal(claims);

        token.ShouldBe(sealer.Seal(claims));
        token.ShouldNotContain("origin");
        sealer.Open(token).ShouldBe(claims);
    }

    [Fact]
    public void OpenRejectsTamperedForeignKeyAndExpiredTokens()
    {
        var clock = new FakeClock();
        var sealer = Sealer(clock: clock);
        var token = sealer.Seal(new MediaClaims("https://o.example/x", null, null, null, clock.UtcNow.AddMinutes(5).ToUnixTimeSeconds()));

        sealer.Open(token[..^2] + (token[^1] == 'A' ? "BB" : "AA")).ShouldBeNull();
        sealer.Open("garbage").ShouldBeNull();
        Sealer("other").Open(token).ShouldBeNull();

        clock.UtcNow = clock.UtcNow.AddMinutes(6);
        sealer.Open(token).ShouldBeNull();
    }

    [Fact]
    public void ImgproxyUrlsAreSignedAndNeverContainTheOrigin()
    {
        var options = Options.Create(new StockMediaOptions
        {
            SealKey = "k",
            InternalUrl = "http://studio:8087",
            Imgproxy = new ImgproxyOptions { Prefix = "https://img.example/img", Key = "00112233445566778899aabbccddeeff", Salt = "ffeeddccbbaa99887766554433221100" },
        });
        var clock = new FakeClock();
        var urls = new MediaUrls(new MediaSealer(options, clock, NullLogger<MediaSealer>.Instance), options, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), clock);

        var thumb = urls.Thumb("https://secret-origin.example/photo.jpg");

        thumb.ShouldStartWith("https://img.example/img/");
        thumb.ShouldContain("/rs:fit:480:480:0/q:80/");
        var encoded = thumb[(thumb.LastIndexOf('/') + 1)..];
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded.Replace('-', '+').Replace('_', '/').PadRight((encoded.Length + 3) / 4 * 4, '=')));
        decoded.ShouldStartWith("http://studio:8087/api/v1/stock/media/");
        thumb.ShouldNotContain("secret-origin");
        urls.Thumb("https://secret-origin.example/photo.jpg").ShouldBe(thumb);
    }

    [Fact]
    public void WithoutImgproxyThumbsGoThroughTheRelay()
    {
        var options = Options.Create(new StockMediaOptions { SealKey = "k" });
        var clock = new FakeClock();
        var urls = new MediaUrls(new MediaSealer(options, clock, NullLogger<MediaSealer>.Instance), options, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), clock);

        urls.Thumb("https://o.example/a.jpg").ShouldStartWith("/api/v1/stock/media/");
        urls.Preview("https://o.example/a.mp4", "video/mp4").ShouldStartWith("/api/v1/stock/media/");
        urls.FileUrl(new string('a', 64), "x.mp4", "video/mp4", clock.UtcNow.AddHours(1)).ShouldStartWith("/api/v1/stock/files/");
    }

    [Theory]
    [InlineData("93.184.216.34", true)]
    [InlineData("2606:2800:220:1:248:1893:25c8:1946", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.20.0.1", false)]
    [InlineData("172.32.0.1", true)]
    [InlineData("192.168.1.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    public void PublicAddressCheck(string ip, bool expected) => StockHttpClients.IsPublic(IPAddress.Parse(ip)).ShouldBe(expected);
}

public class ProviderSupportTests
{
    [Theory]
    [InlineData("https://example.com/a", true)]
    [InlineData("http://example.com", true)]
    [InlineData("ftp://example.com", false)]
    [InlineData("https://user:pw@example.com", false)]
    [InlineData("https://example.com/a\nb", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    public void ParsesOnlyPlainHttpUrls(string url, bool ok) => ProviderSupport.TryParseHttpUrl(url, out _).ShouldBe(ok);

    [Fact]
    public void FileNamesAreSanitised()
    {
        ProviderSupport.FileName("../../etc/passwd", "mp4").ShouldNotContain("/");
        ProviderSupport.FileName("", "jpg").ShouldBe("download.jpg");
        ProviderSupport.SanitizeFileName(".hidden").ShouldBe("hidden");
    }

    [Fact]
    public void PagingHelpers()
    {
        ProviderSupport.Page(null).ShouldBe(1);
        ProviderSupport.Page("3").ShouldBe(3);
        ProviderSupport.NextCursor(2, 30, 30).ShouldBe("3");
        ProviderSupport.NextCursor(2, 10, 30).ShouldBeNull();
    }
}
