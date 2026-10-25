using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using CutWire.Studio.Application.Forge;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace CutWire.Studio.IntegrationTests;

public abstract class ForgeTestBase : IDisposable
{
    protected CatalogFixture F { get; }

    protected ForgeTestBase(PostgresFixture postgres, Action<IWebHostBuilder>? configure = null) => F = new CatalogFixture(postgres, configure);

    protected CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => F.Dispose();

    protected string NewDoc(string kind = "effect", string name = "Test Fx") =>
        F.Factory.Services.GetRequiredService<IForgeRuntime>().NewDocument(kind, name, null);

    protected async Task<HttpClient> Pat(HttpClient session, params string[] scopes)
    {
        var response = await session.PostJson("/api/v1/account/tokens", new { name = "t", scopes }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var secret = (await response.Json(Ct))["secret"]!.GetValue<string>();
        var client = F.Factory.Browser(cookies: false);
        client.DefaultRequestHeaders.Remove("X-Requested-With");
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return client;
    }

    protected Task<HttpResponseMessage> Create(HttpClient client, string doc, string? localId = null) =>
        client.PostJson("/api/v1/forge/documents", new { localId = localId ?? Guid.NewGuid().ToString(), doc = JsonNode.Parse(doc) }, Ct);

    protected async Task<JsonNode> Created(HttpClient client, string? doc = null)
    {
        var response = await Create(client, doc ?? NewDoc());
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await response.Json(Ct);
    }

    protected Task<HttpResponseMessage> Put(HttpClient client, string id, string doc, int baseRevision, string? clientId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/forge/documents/{id}") { Content = JsonContent.Create(new { doc = JsonNode.Parse(doc), baseRevision }) };
        if (clientId is not null) request.Headers.Add("X-Forge-Client", clientId);
        return client.SendAsync(request, Ct);
    }

    protected static ByteArrayContent Raw(byte[] data, string mime)
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue(mime);
        return content;
    }
}
