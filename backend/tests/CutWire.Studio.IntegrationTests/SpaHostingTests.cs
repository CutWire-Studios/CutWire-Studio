using System.Net;
using Shouldly;

namespace CutWire.Studio.IntegrationTests;

public sealed class SpaHostingTests : IDisposable
{
    private readonly StudioFactory _factory = new();
    private readonly HttpClient _client;
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public SpaHostingTests()
    {
        foreach (var app in new[] { "shell", "forge", "themes", "edit" })
        {
            _factory.WriteSpa($"{app}/index.html", $"<html>{app}</html>");
            _factory.WriteSpa($"{app}/assets/app.abc123.js", "console.log(1)");
        }

        _factory.WriteSpa("shell/favicon.svg", "<svg/>");
        _client = _factory.CreateClient();
    }

    public void Dispose() => _factory.Dispose();

    private async Task<(HttpResponseMessage Response, string Body)> Get(string path)
    {
        var response = await _client.GetAsync(path, Ct);
        return (response, await response.Content.ReadAsStringAsync(Ct));
    }

    [Theory]
    [InlineData("/", "shell")]
    [InlineData("/community/items/42", "shell")]
    [InlineData("/forge", "forge")]
    [InlineData("/forge/some/deep/route", "forge")]
    [InlineData("/themes/editor", "themes")]
    [InlineData("/edit", "edit")]
    [InlineData("/edit/project/1", "edit")]
    public async Task ExtensionlessPaths_FallBackToOwningAppIndex(string path, string app)
    {
        var (response, body) = await Get(path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe($"<html>{app}</html>");
        response.Headers.CacheControl!.NoCache.ShouldBeTrue();
    }

    [Fact]
    public async Task HashedAssets_AreImmutable()
    {
        var (response, body) = await Get("/forge/assets/app.abc123.js");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("console.log(1)");
        response.Headers.CacheControl!.ToString().ShouldBe("public, max-age=31536000, immutable");
    }

    [Fact]
    public async Task MissingAsset_Is404NotIndexHtml()
    {
        var (response, body) = await Get("/forge/assets/missing.js");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldNotContain("<html>");
    }

    [Fact]
    public async Task RootStaticFile_IsServedFromShell()
    {
        var (response, body) = await Get("/favicon.svg");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("<svg/>");
    }

    [Theory]
    [InlineData("/api/v1/unknown")]
    [InlineData("/api")]
    [InlineData("/connect/unknown")]
    [InlineData("/.well-known/unknown")]
    [InlineData("/mcp")]
    [InlineData("/files/unknown")]
    public async Task ReservedPrefixes_NeverFallBackToSpa(string path)
    {
        var (response, body) = await Get(path);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldNotContain("<html>");
    }

    [Fact]
    public async Task Edit_SendsCrossOriginIsolationHeaders()
    {
        var (response, _) = await Get("/edit/anything");

        response.Headers.GetValues("Cross-Origin-Opener-Policy").ShouldBe(["same-origin"]);
        response.Headers.GetValues("Cross-Origin-Embedder-Policy").ShouldBe(["require-corp"]);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/forge")]
    [InlineData("/themes")]
    public async Task OtherApps_DoNotSendCrossOriginIsolationHeaders(string path)
    {
        var (response, _) = await Get(path);

        response.Headers.Contains("Cross-Origin-Opener-Policy").ShouldBeFalse();
        response.Headers.Contains("Cross-Origin-Embedder-Policy").ShouldBeFalse();
    }
}
