using System.Net;
using System.Text;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Blobs;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace CutWire.Studio.IntegrationTests;

public sealed class SecurityHeadersTests : IDisposable
{
    private readonly StudioFactory _factory = new();
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public SecurityHeadersTests()
    {
        foreach (var app in new[] { "shell", "forge", "themes", "edit" })
            _factory.WriteSpa($"{app}/index.html", $"<html>{app}</html>");
    }

    public void Dispose() => _factory.Dispose();

    private static string One(HttpResponseMessage response, string name) => response.Headers.Concat(response.Content.Headers)
        .Single(h => h.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value.Single();

    [Theory]
    [InlineData("/", "shell")]
    [InlineData("/community/items/1", "shell")]
    [InlineData("/forge/home", "forge")]
    [InlineData("/themes/editor", "themes")]
    [InlineData("/edit/project", "edit")]
    public async Task SpaHtml_GetsAStrictPolicy(string path, string app)
    {
        var response = await _factory.Browser().GetAsync(path, Ct);

        var csp = One(response, "Content-Security-Policy");
        foreach (var directive in new[]
                 {
                     "default-src 'self'", "style-src 'self';", "frame-ancestors 'none'", "base-uri 'none'", "form-action 'self'", "object-src 'none'",
                     "upgrade-insecure-requests", "connect-src", "report-uri /api/v1/csp-report", "report-to csp-endpoint",
                 })
            csp.ShouldContain(directive);
        csp.ShouldNotContain("'unsafe-eval'");
        csp.ShouldNotContain("script-src 'self' 'unsafe-inline'");
        var script = csp.Split("; ").Single(d => d.StartsWith("script-src ", StringComparison.Ordinal));
        script.ShouldBe(app == "forge" ? "script-src 'self' 'wasm-unsafe-eval'" : "script-src 'self'");
        response.Headers.Contains("Content-Security-Policy-Report-Only").ShouldBeFalse();
        One(response, "Reporting-Endpoints").ShouldBe("csp-endpoint=\"/api/v1/csp-report\"");
    }

    [Fact]
    public async Task EverythingGetsTheBaselineHeaders()
    {
        var response = await _factory.Browser().GetAsync("/", Ct);

        One(response, "X-Content-Type-Options").ShouldBe("nosniff");
        One(response, "Referrer-Policy").ShouldBe("strict-origin-when-cross-origin");
        One(response, "Cross-Origin-Opener-Policy").ShouldBe("same-origin");
        One(response, "Cross-Origin-Resource-Policy").ShouldBe("same-origin");
        One(response, "X-Frame-Options").ShouldBe("DENY");
        One(response, "Strict-Transport-Security").ShouldBe("max-age=31536000; includeSubDomains");
        var permissions = One(response, "Permissions-Policy");
        foreach (var feature in new[] { "camera=()", "microphone=()", "geolocation=()", "payment=()", "usb=()" }) permissions.ShouldContain(feature);
        response.Headers.Contains("Server").ShouldBeFalse();
    }

    [Fact]
    public async Task Hsts_IsOnlySentOverHttps()
    {
        var plain = await _factory.CreateClient().GetAsync("/", Ct);

        plain.Headers.Contains("Strict-Transport-Security").ShouldBeFalse();
    }

    [Fact]
    public async Task Forge_KeepsTheMicrophoneAndRemoteAiEndpoints()
    {
        var response = await _factory.Browser().GetAsync("/forge", Ct);

        One(response, "Permissions-Policy").ShouldContain("microphone=(self)");
        One(response, "Content-Security-Policy").ShouldContain("connect-src 'self' https: http://localhost:*");
        var shell = await _factory.Browser().GetAsync("/", Ct);
        One(shell, "Content-Security-Policy").ShouldContain("connect-src 'self';");
    }

    [Fact]
    public async Task Edit_KeepsCrossOriginIsolation()
    {
        var response = await _factory.Browser().GetAsync("/edit", Ct);

        One(response, "Cross-Origin-Opener-Policy").ShouldBe("same-origin");
        One(response, "Cross-Origin-Embedder-Policy").ShouldBe("require-corp");
    }

    [Theory]
    [InlineData("/api/v1/unknown")]
    [InlineData("/connect/unknown")]
    [InlineData("/.well-known/unknown")]
    [InlineData("/mcp")]
    public async Task ApiRoutes_GetANonePolicy(string path)
    {
        var response = await _factory.Browser().GetAsync(path, Ct);

        One(response, "Content-Security-Policy").ShouldStartWith("default-src 'none'; frame-ancestors 'none'");
        One(response, "Cross-Origin-Resource-Policy").ShouldBe("same-origin");
        One(response, "X-Content-Type-Options").ShouldBe("nosniff");
    }

    [Theory]
    [InlineData("/files/unknown", "cross-origin")]
    [InlineData("/api/v1/stock/files/token", "same-origin")]
    [InlineData("/api/v1/stock/media/token", "same-origin")]
    [InlineData("/api/v1/stock/sfx/files/previews/a.opus", "same-origin")]
    public async Task ByteRoutes_AreSandboxed(string path, string corp)
    {
        var response = await _factory.Browser().GetAsync(path, Ct);

        One(response, "Content-Security-Policy").ShouldStartWith("default-src 'none'; sandbox");
        One(response, "Cross-Origin-Resource-Policy").ShouldBe(corp);
        One(response, "X-Content-Type-Options").ShouldBe("nosniff");
    }

    [Fact]
    public async Task UnhandledExceptions_DoNotLeakDetails()
    {
        using var factory = new StudioFactory(configure: b => b.ConfigureTestServices(s => s.AddSingleton<CutWire.Studio.Api.Endpoints.IApiContributor, TestEndpoints>()));
        var response = await factory.Browser().GetAsync("/api/v1/_test/boom", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldNotContain("secret-internal-detail");
        body.ShouldNotContain("InvalidOperationException");
        body.ShouldNotContain(" at CutWire");
        One(response, "Content-Security-Policy").ShouldStartWith("default-src 'none'");
    }

    [Fact]
    public async Task ReportOnlyMode_SwapsTheHeaderForEveryClass()
    {
        using var factory = new StudioFactory(configure: b => b.UseSetting("SecurityHeaders:CspReportOnly", "true"));
        factory.WriteSpa("shell/index.html", "<html/>");
        var client = factory.Browser();

        foreach (var path in new[] { "/", "/api/v1/unknown", "/files/x" })
        {
            var response = await client.GetAsync(path, Ct);
            response.Headers.Contains("Content-Security-Policy").ShouldBeFalse(path);
            One(response, "Content-Security-Policy-Report-Only").ShouldContain("default-src");
        }
    }

    [Fact]
    public async Task ConfigurationOverridesAnAppsDirectives()
    {
        using var factory = new StudioFactory(configure: b =>
        {
            b.UseSetting("SecurityHeaders:Apps:themes:Csp:font-src", "'self' data:");
            b.UseSetting("SecurityHeaders:Apps:themes:Csp:worker-src", "~");
            b.UseSetting("Stock:Media:Imgproxy:Prefix", "https://img.example.com/img");
        });
        factory.WriteSpa("themes/index.html", "<html/>");
        factory.WriteSpa("shell/index.html", "<html/>");
        var client = factory.Browser();

        var themes = One(await client.GetAsync("/themes", Ct), "Content-Security-Policy");
        themes.ShouldContain("font-src 'self' data:");
        themes.ShouldNotContain("worker-src");
        var shell = One(await client.GetAsync("/", Ct), "Content-Security-Policy");
        shell.ShouldContain("font-src 'self';");
        shell.ShouldContain("img-src 'self' data: blob: https://img.example.com;");
    }

    [Fact]
    public async Task CspReports_AreAcceptedWithoutACsrfHeaderAndRateLimited()
    {
        using var factory = new StudioFactory(configure: b => b.UseSetting("RateLimiting:CspReportPermitLimit", "3"));
        var client = factory.CreateClient();
        StringContent Report(string body, string type) => new(body, Encoding.UTF8, type);

        (await client.PostAsync("/api/v1/csp-report",
            Report("{\"csp-report\":{\"blocked-uri\":\"inline\",\"violated-directive\":\"script-src\"}}", "application/csp-report"), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PostAsync("/api/v1/csp-report",
            Report("[{\"type\":\"csp-violation\",\"body\":{\"blockedURL\":\"eval\",\"effectiveDirective\":\"script-src\"}}]", "application/reports+json"), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PostAsync("/api/v1/csp-report", Report("not json", "application/json"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PostAsync("/api/v1/csp-report", Report("{}", "application/json"), Ct)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task CspReports_OverTheSizeCap_AreRefused()
    {
        var response = await _factory.CreateClient().PostAsync("/api/v1/csp-report",
            new StringContent("{\"csp-report\":{\"blocked-uri\":\"" + new string('a', 20_000) + "\"}}", Encoding.UTF8, "application/csp-report"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }
}

[Collection(PostgresCollection.Name)]
public sealed class FileSecurityTests(PostgresFixture postgres) : IDisposable
{
    private readonly StudioFactory _factory = new(postgres.ConnectionString);
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _factory.Dispose();

    private async Task<string> Seed(string content, string mime)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<StoreBlobCommand, StoreBlobResult>>();
        return (await handler.Handle(new StoreBlobCommand(new MemoryStream(Encoding.UTF8.GetBytes(content)), mime, 1024), Ct)).Value.Hash.ToString();
    }

    [Theory]
    [InlineData("image/svg+xml", "<svg xmlns='http://www.w3.org/2000/svg' onload='alert(1)'/>")]
    [InlineData("text/html", "<script>alert(1)</script>")]
    [InlineData("application/json", "{}")]
    [InlineData("application/zip", "PK")]
    [InlineData("text/plain", "hello")]
    public async Task NonPassiveTypes_AreForcedToDownload(string mime, string content)
    {
        var sha = await Seed(content, mime);

        var response = await _factory.Browser().GetAsync($"/files/{sha}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldStartWith("default-src 'none'; sandbox");
    }

    [Fact]
    public async Task NamedDownloads_StaySanitisedAttachments()
    {
        var sha = await Seed("{}", "application/json");

        var response = await _factory.Browser().GetAsync($"/files/{sha}?name=../../evil\"\r\n.html", Ct);

        var disposition = response.Content.Headers.ContentDisposition!;
        disposition.DispositionType.ShouldBe("attachment");
        disposition.FileName!.ShouldNotContain("/");
        disposition.FileName!.ShouldNotContain("\"");
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("audio/ogg")]
    [InlineData("video/mp4")]
    public async Task PassiveMedia_StaysInline(string mime)
    {
        var sha = await Seed("0123", mime);

        var response = await _factory.Browser().GetAsync($"/files/{sha}", Ct);

        response.Content.Headers.ContentDisposition.ShouldBeNull();
    }

    [Fact]
    public async Task DeviceFlow_IsRateLimitedBeforeAuthentication()
    {
        using var factory = new StudioFactory(postgres.ConnectionString, configure: b => b.UseSetting("RateLimiting:DeviceFlowPermitLimit", "2"));
        var client = factory.Browser();

        for (var i = 0; i < 2; i++)
            (await client.GetAsync("/connect/verify?user_code=ABCD-EFGH", Ct)).StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);

        (await client.GetAsync("/connect/verify?user_code=ABCD-EFGH", Ct)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }
}
