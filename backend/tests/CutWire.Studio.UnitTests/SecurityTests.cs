using CutWire.Studio.Api.Http;
using CutWire.Studio.Api.Spa;
using System.Diagnostics;
using CutWire.Studio.Application.Quality;
using Shouldly;

namespace CutWire.Studio.UnitTests;

public class TextGateAbuseTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OversizedDescription_IsRejectedWithoutRunningTheQuadraticLinkScan()
    {
        var gate = new TextGate(new ContentRulesOptions());
        var clock = Stopwatch.StartNew();

        var found = await gate.CheckAsync(Drafts.Valid() with { Description = string.Concat(Enumerable.Repeat("a.", 100_000)) }, Ct);

        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
        found.Codes().ShouldContain("too_long");
    }

    [Fact]
    public async Task OversizedTitleAndSummary_AreRejectedQuickly()
    {
        var clock = Stopwatch.StartNew();
        var words = new TitleGate(new ContentRulesOptions(), Drafts.Words);
        var text = new TextGate(new ContentRulesOptions());
        var draft = Drafts.Valid() with { Title = string.Concat(Enumerable.Repeat("a.", 100_000)), Summary = string.Concat(Enumerable.Repeat("a.", 100_000)) };

        (await words.CheckAsync(draft, Ct)).Codes().ShouldContain("too_long");
        (await text.CheckAsync(draft, Ct)).Codes().ShouldContain("too_long");

        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }
}

public class StockLinkTests
{
    [Theory]
    [InlineData("https://example.com/u/1", "https://example.com/u/1")]
    [InlineData(" http://example.com ", "http://example.com")]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("data:text/html,<script>alert(1)</script>", null)]
    [InlineData("//example.com", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ProviderLinks_AreHttpOrDropped(string? raw, string? expected) =>
        CutWire.Studio.Application.Stock.StockAssembler.HttpUrl(raw).ShouldBe(expected);
}

public class SecurityHeaderRulesTests
{
    [Theory]
    [InlineData("/files/abc", RouteClass.Bytes)]
    [InlineData("/api/v1/stock/files/token", RouteClass.Bytes)]
    [InlineData("/api/v1/stock/media/token", RouteClass.Bytes)]
    [InlineData("/api/v1/stock/sfx/files/previews/a.opus", RouteClass.Bytes)]
    [InlineData("/api/v1/marketplace", RouteClass.Api)]
    [InlineData("/api/v1/csp-report", RouteClass.Api)]
    [InlineData("/connect/token", RouteClass.Api)]
    [InlineData("/mcp", RouteClass.Api)]
    [InlineData("/.well-known/jwks", RouteClass.Api)]
    [InlineData("/", RouteClass.Spa)]
    [InlineData("/forge/some/route", RouteClass.Spa)]
    [InlineData("/filesystem", RouteClass.Spa)]
    public void Classify_PicksTheRouteClass(string path, RouteClass expected) =>
        SecurityHeadersMiddleware.Classify(path).ShouldBe(expected);

    [Theory]
    [InlineData("image/png", true)]
    [InlineData("image/webp; charset=binary", true)]
    [InlineData("audio/ogg", true)]
    [InlineData("video/mp4", true)]
    [InlineData("image/svg+xml", false)]
    [InlineData("text/html", false)]
    [InlineData("application/json", false)]
    [InlineData("application/zip", false)]
    [InlineData(null, false)]
    public void OnlyPassiveMedia_MayRenderInline(string? contentType, bool inline) =>
        SecurityHeadersMiddleware.IsInlineSafe(contentType).ShouldBe(inline);

    private static SecurityPolicies Policies(SecurityHeadersOptions? options = null, string? imgproxy = null, bool development = false) =>
        new(options ?? new SecurityHeadersOptions(), AppHostRegistry.Default, imgproxy, development);

    private static AppHost Host(string dir) => AppHostRegistry.Default.Single(h => h.Dir == dir);

    [Fact]
    public void SpaPolicy_IsStrictByDefault()
    {
        var csp = Policies().Spa(Host("shell"));

        csp.ShouldContain("default-src 'self'");
        csp.ShouldContain("script-src 'self';");
        csp.ShouldContain("style-src 'self';");
        csp.ShouldContain("frame-ancestors 'none'");
        csp.ShouldContain("base-uri 'none'");
        csp.ShouldContain("object-src 'none'");
        csp.ShouldContain("upgrade-insecure-requests");
        csp.ShouldContain("report-uri /api/v1/csp-report");
        csp.ShouldNotContain("unsafe-eval");
        csp.ShouldNotContain("script-src 'self' 'unsafe-inline'");
    }

    [Fact]
    public void OnlyForge_AllowsWebAssembly()
    {
        var policies = Policies();

        policies.Spa(Host("forge")).ShouldContain("script-src 'self' 'wasm-unsafe-eval'");
        foreach (var dir in new[] { "shell", "themes", "edit" })
            policies.Spa(Host(dir)).ShouldNotContain("wasm-unsafe-eval");
    }

    [Fact]
    public void ImgproxyOrigin_IsAddedToImageAndMediaSources()
    {
        var csp = Policies(imgproxy: "https://img.example.com").Spa(Host("shell"));

        csp.ShouldContain("img-src 'self' data: blob: https://img.example.com;");
        csp.ShouldContain("media-src 'self' blob: https://img.example.com;");
    }

    [Fact]
    public void AppOverrides_ReplaceOrRemoveDirectives()
    {
        var options = new SecurityHeadersOptions();
        options.Apps["themes"] = new AppSecurityOptions { Csp = { ["font-src"] = "'self' data:", ["worker-src"] = "~" } };

        var csp = Policies(options).Spa(Host("themes"));

        csp.ShouldContain("font-src 'self' data:");
        csp.ShouldNotContain("worker-src");
        Policies(options).Spa(Host("shell")).ShouldContain("font-src 'self';");
    }

    [Fact]
    public void Development_DoesNotUpgradeToHttps() =>
        Policies(development: true).Spa(Host("shell")).ShouldNotContain("upgrade-insecure-requests");

    [Fact]
    public void Forge_KeepsTheMicrophoneOthersDenyIt()
    {
        var policies = Policies();

        policies.Permissions(Host("forge")).ShouldContain("microphone=(self)");
        policies.Permissions(Host("shell")).ShouldContain("microphone=()");
        policies.Permissions(Host("shell")).ShouldContain("camera=()");
    }

    [Fact]
    public void CspReportSummary_IsBoundedAndFreeOfControlCharacters()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(
            "{\"blocked-uri\":\"https://evil.example/\\r\\nFAKE LOG LINE\",\"violated-directive\":\"" + new string('x', 5000) + "\"}");

        var line = CspReportEndpoint.Summarize(doc.RootElement);

        line.ShouldNotContain("\n");
        line.ShouldNotContain("\r");
        line.Length.ShouldBeLessThan(500);
    }
}

public class LocalRedirectTests
{
    [Theory]
    [InlineData("/community/items/1?x=1#y", "/community/items/1?x=1#y")]
    [InlineData("/", "/")]
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("/\t/evil.example", "/")]
    [InlineData("/\n/evil.example", "/")]
    [InlineData("/a\\b", "/")]
    [InlineData("https://evil.example", "/")]
    [InlineData("javascript:alert(1)", "/")]
    [InlineData("", "/")]
    [InlineData(null, "/")]
    public void ReturnTargets_StaySameOrigin(string? value, string expected) =>
        ResultExtensions.SafeLocalPath(value).ShouldBe(expected);
}

public class ImageBombTests
{
    private static byte[] Chunk(string type, byte[] data)
    {
        var body = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        var crc = ~body.Aggregate(0xFFFFFFFFu, (c, b) =>
        {
            c ^= b;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            return c;
        });
        return [.. ((uint)data.Length).ToBigEndian(), .. body, .. crc.ToBigEndian()];
    }

    [Fact]
    public void AHeaderClaimingHugeDimensions_IsRefusedBeforeDecoding()
    {
        const int side = 8000;
        var ihdr = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0), side);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), side);
        ihdr[8] = 8;
        using var idat = new MemoryStream();
        using (var z = new System.IO.Compression.ZLibStream(idat, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[side + 1];
            for (var y = 0; y < side; y++) z.Write(row);
        }

        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Chunk("IHDR", ihdr), .. Chunk("IDAT", idat.ToArray()), .. Chunk("IEND", [])];

        using (var codec = SkiaSharp.SKCodec.Create(new SkiaSharp.SKMemoryStream(png))) codec!.Info.Width.ShouldBe(side);
        new CutWire.Studio.Infrastructure.Quality.SkiaImageAnalyzer().Probe(png).ShouldBeNull();
    }
}

internal static class UInt32Extensions
{
    public static byte[] ToBigEndian(this uint value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
}

public class ProviderCacheKeyTests
{
    [Fact]
    public void StoredKeys_DoNotCarryProviderApiKeys()
    {
        var stored = CutWire.Studio.Infrastructure.Stock.ProviderCache.Digest("https://pixabay.com/api/?key=SECRET-KEY&q=cat");

        stored.ShouldNotContain("SECRET");
        stored.Length.ShouldBe(64);
    }
}
