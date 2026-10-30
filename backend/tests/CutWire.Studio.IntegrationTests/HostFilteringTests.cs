using System.Net;
using Microsoft.AspNetCore.TestHost;
using Shouldly;

namespace CutWire.Studio.IntegrationTests;

public sealed class HostFilteringTests : IDisposable
{
    private readonly StudioFactory _factory = new(configure: b =>
    {
        b.UseSetting("AllowedHosts", "cutwire.studio");
        b.UseSetting("Stock:Media:InternalUrl", "http://studio:8080");
    });
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public HostFilteringTests() => _factory.WriteSpa("shell/index.html", "<html>shell</html>");

    public void Dispose() => _factory.Dispose();

    [Theory]
    [InlineData("cutwire.studio", HttpStatusCode.OK)]
    [InlineData("localhost", HttpStatusCode.OK)]
    [InlineData("127.0.0.1", HttpStatusCode.OK)]
    [InlineData("studio:8080", HttpStatusCode.OK)]
    [InlineData("evil.example", HttpStatusCode.BadRequest)]
    public async Task ConfiguredHosts_PlusLoopbackAndInternalUrl_AreAllowed(string host, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = host;

        var response = await _factory.Browser().SendAsync(request, Ct);

        response.StatusCode.ShouldBe(expected);
    }
}
