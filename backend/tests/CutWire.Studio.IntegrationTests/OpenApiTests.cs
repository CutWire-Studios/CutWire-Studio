using System.Text.Json;
using System.Text.Json.Nodes;
using Shouldly;

namespace CutWire.Studio.IntegrationTests;

public sealed class OpenApiTests : IDisposable
{
    private readonly StudioFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Document_Is31AndWrittenToBackendOpenApiJson()
    {
        var ct = TestContext.Current.CancellationToken;
        var json = await _factory.CreateClient().GetStringAsync("/api/v1/openapi.json", ct);
        var doc = JsonNode.Parse(json)!;

        doc["openapi"]!.GetValue<string>().ShouldStartWith("3.1");
        doc["paths"]!.AsObject().ContainsKey("/api/v1/healthz").ShouldBeTrue();
        doc["paths"]!.AsObject().ContainsKey("/files/{sha256}").ShouldBeTrue();
        doc["paths"]!.AsObject().ContainsKey("/api/v1/account/sign-in").ShouldBeTrue();
        doc["paths"]!.AsObject().ContainsKey("/api/v1/me").ShouldBeTrue();

        foreach (var path in new[] { "/api/v1/items", "/api/v1/items/{kind}/{slug}", "/api/v1/items/{id}/publish", "/api/v1/meta/quality-rules", "/api/v1/users/{handle}", "/connect/verify" })
            doc["paths"]!.AsObject().ContainsKey(path).ShouldBeTrue(path);
        var problem = doc["components"]!["schemas"]!["ProblemDetails"]!["properties"]!.AsObject();
        problem.ContainsKey("code").ShouldBeTrue();
        problem.ContainsKey("errors").ShouldBeTrue();
        problem.ContainsKey("violations").ShouldBeTrue();
        doc["paths"]!["/api/v1/account/passkeys/request-options"]!["post"]!["responses"]!["200"]!["content"]!["application/json"]!["schema"]!["type"]!.ToJsonString().ShouldContain("object");

        var target = Path.Combine(FindBackendRoot(), "openapi.json");
        var pretty = doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
        if (!File.Exists(target) || File.ReadAllText(target) != pretty)
            await File.WriteAllTextAsync(target, pretty, ct);
    }

    private static string FindBackendRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "CutWire.Studio.slnx"))) return dir.FullName;
        throw new InvalidOperationException("backend root not found");
    }
}
