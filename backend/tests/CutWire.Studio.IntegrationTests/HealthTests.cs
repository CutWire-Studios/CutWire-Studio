using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace CutWire.Studio.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class HealthTests(PostgresFixture postgres) : IDisposable
{
    private readonly StudioFactory _factory = new(postgres.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Healthz_ReturnsOkWhenDatabaseIsReachable()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/healthz", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(TestContext.Current.CancellationToken))!["status"].ShouldBe("ok");
    }

    [Fact]
    public async Task Healthz_Returns503ProblemWhenDatabaseIsDown()
    {
        using var broken = new StudioFactory("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2", migrate: false);

        var response = await broken.CreateClient().GetAsync("/api/v1/healthz", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("\"code\":\"db.unavailable\"");
    }

    [Fact]
    public async Task UnknownApiRoute_ReturnsProblemWithCode()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/nope", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("\"code\":\"not_found\"");
    }
}
