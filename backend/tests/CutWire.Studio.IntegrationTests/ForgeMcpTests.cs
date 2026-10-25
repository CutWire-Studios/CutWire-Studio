using System.Net;
using System.Text.Json.Nodes;
using CutWire.Studio.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Shouldly;

namespace CutWire.Studio.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class ForgeMcpTests(PostgresFixture postgres) : ForgeTestBase(postgres)
{
    private async Task<McpClient> Connect(HttpClient bearer) => await McpClient.CreateAsync(
        new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri("http://localhost:8087/mcp") }, bearer, ownsHttpClient: false),
        cancellationToken: Ct);

    private async Task<string> Call(McpClient client, string tool, Dictionary<string, object?>? args = null, bool expectError = false)
    {
        var result = await client.CallToolAsync(tool, args ?? [], cancellationToken: Ct);
        var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(b => b.Text));
        (result.IsError ?? false).ShouldBe(expectError, text);
        return text;
    }

    [Fact]
    public async Task Unauthenticated_GetsChallengeWithResourceMetadata()
    {
        var anonymous = F.Factory.Browser(cookies: false);
        var response = await anonymous.PostAsync("/mcp", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var challenge = response.Headers.WwwAuthenticate.ToString();
        challenge.ShouldStartWith("Bearer");
        challenge.ShouldContain("resource_metadata=\"http://localhost:8087/.well-known/oauth-protected-resource\"");

        var (session, _, _) = await F.User();
        var cookieOnly = await session.PostAsync("/mcp", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"), Ct);
        cookieOnly.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var metadata = await (await anonymous.GetAsync("/.well-known/oauth-protected-resource", Ct)).Json(Ct);
        metadata["resource"]!.GetValue<string>().ShouldBe("http://localhost:8087/mcp");
    }

    [Fact]
    public async Task EndToEnd_Pat()
    {
        var (session, handle, userId) = await F.User();
        var bearer = await Pat(session, "forge:read", "forge:write", "community:publish");
        await using var client = await Connect(bearer);
        client.ServerInstructions!.ShouldContain("live");

        var tools = (await client.ListToolsAsync(cancellationToken: Ct)).Select(t => t.Name).ToHashSet();
        foreach (var name in new[]
                 {
                     "list_documents", "open_document", "create_document", "get_shader_code", "check", "get_share_link", "open_link", "import_package",
                     "export_driftfx", "publish_draft", "list_blocks", "describe_block", "view_graph", "add_block", "connect", "disconnect", "set_setting",
                     "remove_block", "expose_setting", "set_details", "list_pedals", "describe_pedal", "view_rack", "add_pedal", "add_split", "move_pedal",
                     "remove_pedal", "set_knob", "add_modulator", "route_modulation", "set_ir", "expose_knob",
                 })
            tools.ShouldContain(name);
        tools.ShouldNotContain("delete_document");

        (await Call(client, "list_documents")).ShouldContain("No documents");
        var created = await Call(client, "create_document", new() { ["kind"] = "effect", ["name"] = "Mcp Glow" });
        var docId = created.Split('\n')[0].Replace("doc_id: ", "").Trim();
        Guid.TryParse(docId, out _).ShouldBeTrue(created);
        (await Call(client, "list_documents")).ShouldContain("Mcp Glow");

        var events = await session.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/api/v1/forge/documents/{docId}/events"), HttpCompletionOption.ResponseHeadersRead, Ct);
        using var reader = new StreamReader(await events.Content.ReadAsStreamAsync(Ct));

        var added = await Call(client, "add_block", new() { ["doc_id"] = docId, ["type"] = "blur" });
        added.ShouldStartWith("added ");
        var live = await ForgeDocumentTests.ReadEvent(reader);
        live.Name.ShouldBe("updated");
        live.Data["source"]!.GetValue<string>().ShouldBe("mcp");
        live.Data["revision"]!.GetValue<int>().ShouldBe(2);
        live.Data["clientId"].ShouldBeNull();

        var stored = await (await session.GetAsync($"/api/v1/forge/documents/{docId}", Ct)).Json(Ct);
        stored["revision"]!.GetValue<int>().ShouldBe(2);
        stored["doc"]!["nodes"]!.AsArray().Count.ShouldBe(3);

        (await Call(client, "add_block", new() { ["doc_id"] = docId, ["type"] = "nope" }, expectError: true)).ShouldContain("No block type");
        (await Call(client, "open_document", new() { ["doc_id"] = docId })).ShouldContain("blur");
        (await Call(client, "check", new() { ["doc_id"] = docId })).ShouldNotBeNullOrWhiteSpace();
        (await Call(client, "get_shader_code", new() { ["doc_id"] = docId })).ShouldContain("main.frag");
        (await Call(client, "list_blocks", new() { ["doc_id"] = docId, ["category"] = "blur" })).ShouldContain("blur");
        (await Call(client, "view_graph", new() { ["doc_id"] = docId })).ShouldContain("effect");
        (await Call(client, "view_graph", new() { ["doc_id"] = Guid.NewGuid().ToString() }, expectError: true)).ShouldContain("Unknown doc_id");

        var link = await Call(client, "get_share_link", new() { ["doc_id"] = docId });
        link.ShouldStartWith("http://localhost:8087/forge/#v1.");
        var opened = await Call(client, "open_link", new() { ["url"] = link });
        opened.ShouldStartWith("doc_id: ");

        var exported = JsonNode.Parse(await Call(client, "export_driftfx", new() { ["doc_id"] = docId }))!;
        var bytes = Convert.FromBase64String(exported["base64"]!.GetValue<string>());
        exported["filename"]!.GetValue<string>().ShouldEndWith(".driftfx");
        var validated = await new CutWire.Studio.Infrastructure.Catalog.EffectKind().ValidateAsync(new MemoryStream(bytes), Ct);
        validated.IsSuccess.ShouldBeTrue(validated.IsFailure ? validated.Error.Message : "");

        var imported = await Call(client, "import_package", new() { ["base64"] = exported["base64"]!.GetValue<string>(), ["filename"] = "x.driftfx" });
        imported.ShouldStartWith("doc_id: ");

        var published = await Call(client, "publish_draft", new() { ["doc_id"] = docId, ["title"] = "Mcp Glow Blur" });
        published.ShouldContain("http://localhost:8087/community/upload?item=");
        var item = Guid.Parse(published.Split("item=")[1].Split(['\n', ' '])[0]);
        var row = await F.Db(db => db.Items.AsNoTracking().SingleAsync(i => i.Id == item, Ct));
        row.OwnerId.ShouldBe(userId);
        row.Status.ShouldBe(ItemStatus.Draft);
    }

    [Fact]
    public async Task ReadOnlyToken_CannotWrite()
    {
        var (session, _, _) = await F.User();
        var docId = (await Created(session))["id"]!.GetValue<string>();
        var reader = await Pat(session, "forge:read");
        await using var client = await Connect(reader);

        (await Call(client, "open_document", new() { ["doc_id"] = docId })).ShouldContain("doc_id");
        (await Call(client, "add_block", new() { ["doc_id"] = docId, ["type"] = "blur" }, expectError: true)).ShouldContain("forge:write");
        (await Call(client, "create_document", new() { ["kind"] = "effect" }, expectError: true)).ShouldContain("forge:write");
        (await Call(client, "view_graph", new() { ["doc_id"] = docId })).ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ConcurrentEdits_AreSerialised()
    {
        var (session, _, _) = await F.User();
        var bearer = await Pat(session, "forge:read", "forge:write");
        await using var client = await Connect(bearer);
        var docId = (await Call(client, "create_document", new() { ["kind"] = "effect" })).Split('\n')[0].Replace("doc_id: ", "").Trim();
        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Call(client, "add_block", new() { ["doc_id"] = docId, ["type"] = "blur" })));
        var stored = await (await session.GetAsync($"/api/v1/forge/documents/{docId}", Ct)).Json(Ct);
        stored["revision"]!.GetValue<int>().ShouldBe(7);
        stored["doc"]!["nodes"]!.AsArray().Count.ShouldBe(8);
    }
}
