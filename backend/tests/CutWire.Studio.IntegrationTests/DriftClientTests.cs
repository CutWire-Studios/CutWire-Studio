using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using CutWire.Studio.Api.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using CutWire.Studio.Api.Endpoints;

namespace CutWire.Studio.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class DriftClientTests(PostgresFixture postgres) : StockTestBase(postgres, extra: b =>
{
    b.UseSetting("DriftClient:Keys:0", Key);
    b.ConfigureTestServices(s => s.AddSingleton<IApiContributor, MarketplaceProbe>());
})
{
    private const string Key = "integration-drift-key";
    private static readonly string ClientId = new('a', 64);

    private sealed class MarketplaceProbe : IApiContributor
    {
        public void Map(RouteGroupBuilder api) =>
            api.MapGroup("/marketplace-probe").RequireMarketplaceCaller()
                .MapGet("", (HttpContext http) => Results.Ok(http.MarketplaceCallerOf()));
    }

    private static HttpRequestMessage Signed(HttpMethod method, string target, object? body = null, string? signature = null)
    {
        var bytes = body is null ? [] : JsonContent.Create(body).ReadAsByteArrayAsync().GetAwaiter().GetResult();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonce = Guid.NewGuid().ToString("N");
        var request = new HttpRequestMessage(method, target);
        if (body is not null) request.Content = new ByteArrayContent(bytes) { Headers = { ContentType = new("application/json") } };
        request.Headers.Add("X-Cutwire-Client", ClientId);
        request.Headers.Add("X-Cutwire-Timestamp", timestamp);
        request.Headers.Add("X-Cutwire-Nonce", nonce);
        request.Headers.Add("X-Cutwire-App", "Drift/1.2.3 (linux)");
        request.Headers.Add("X-Cutwire-Distribution", "appimage");
        request.Headers.Add("X-Cutwire-Signature", signature ?? DriftClientAuth.SignatureFor(Key, timestamp, nonce, ClientId, method.Method, target, bytes));
        return request;
    }

    [Fact]
    public async Task SignedRequestSucceeds()
    {
        var response = await Client().SendAsync(Signed(HttpMethod.Get, "/api/v1/marketplace"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BadSignatureIs401InvalidClient()
    {
        var response = await Client().SendAsync(Signed(HttpMethod.Get, "/api/v1/marketplace", signature: new string('0', 64)), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["code"]!.GetValue<string>().ShouldBe("invalid_client");
    }

    [Fact]
    public async Task SignedQuotaFollowsTheClientIdAcrossInstallIds()
    {
        await F.Sql("update provider_settings set quota_scope = 'client', quota_limit = 1 where provider = 'fake'");
        await Ok(await Client().SendAsync(Signed(HttpMethod.Get, "/api/v1/marketplace"), Ct));
        await F.Sql("update provider_settings set quota_scope = 'client', quota_limit = 1 where provider = 'fake'");
        F.Factory.Services.GetRequiredService<CutWire.Studio.Application.Marketplace.ManifestCache>().Invalidate();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 2; i++)
        {
            var request = Signed(HttpMethod.Post, "/api/v1/marketplace/installs", new { source = "stock.photo", itemId = ItemId("fake", "photo", "p1") });
            request.Headers.Add("X-Studio-Install", "install-" + i);
            statuses.Add((await F.Factory.Browser(cookies: false).SendAsync(request, Ct)).StatusCode);
        }

        statuses.ShouldBe([HttpStatusCode.Created, HttpStatusCode.TooManyRequests]);
    }

    [Fact]
    public async Task MarketplaceFilterAcceptsSignedAndSameOriginOnly()
    {
        var signed = await Ok(await Client().SendAsync(Signed(HttpMethod.Get, "/api/v1/marketplace-probe"), Ct));
        signed["distribution"]!.GetValue<string>().ShouldBe("appimage");
        signed["clientId"]!.GetValue<string>().ShouldBe(ClientId);
        signed["isWeb"]!.GetValue<bool>().ShouldBeFalse();

        var web = Client();
        web.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
        var site = await Ok(await web.GetAsync("/api/v1/marketplace-probe", Ct));
        site["distribution"]!.GetValue<string>().ShouldBe("web");
        site["isWeb"]!.GetValue<bool>().ShouldBeTrue();

        var anonymous = await Client().GetAsync("/api/v1/marketplace-probe", Ct);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
