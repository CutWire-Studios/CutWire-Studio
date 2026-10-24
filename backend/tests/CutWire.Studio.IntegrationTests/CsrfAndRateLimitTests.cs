using System.Net;
using Shouldly;

namespace CutWire.Studio.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class CsrfAndRateLimitTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NonGet_WithoutRequestedWithHeader_IsRejected()
    {
        using var factory = new StudioFactory(postgres.ConnectionString);
        var client = factory.Browser();
        client.DefaultRequestHeaders.Remove("X-Requested-With");

        var response = await client.PostJson("/api/v1/account/sign-in", new { email = "a@b.co", password = "x" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Json(Ct))["code"]!.GetValue<string>().ShouldBe("csrf_header_required");
        (await client.GetAsync("/api/v1/healthz", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task NonGet_WithForeignOrigin_IsRejected()
    {
        using var factory = new StudioFactory(postgres.ConnectionString);
        var client = factory.Browser();
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://evil.example");

        var response = await client.PostJson("/api/v1/account/sign-in", new { email = "a@b.co", password = "x" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Json(Ct))["code"]!.GetValue<string>().ShouldBe("csrf_origin");
    }

    [Fact]
    public async Task StrictRateLimit_Returns429AfterPermitLimit()
    {
        using var factory = new StudioFactory(postgres.ConnectionString,
            configure: b => b.UseSetting("RateLimiting:StrictPermitLimit", "3"));
        var client = factory.Browser();

        for (var i = 0; i < 3; i++)
            (await client.PostJson("/api/v1/account/forgot-password", new { email = "a@b.co" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var limited = await client.PostJson("/api/v1/account/forgot-password", new { email = "a@b.co" }, Ct);
        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await limited.Json(Ct))["code"]!.GetValue<string>().ShouldBe("rate_limited");
    }
}
