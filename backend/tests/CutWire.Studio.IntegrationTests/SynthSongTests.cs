using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using CutWire.Studio.Infrastructure.Oidc;
using CutWire.Studio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.EntityFrameworkCore.Models;
using Shouldly;

namespace CutWire.Studio.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class SynthSongTests(PostgresFixture postgres) : ForgeTestBase(postgres, ConfigureSynth)
{
    private const string SynthMcp = "https://synth.example/mcp";
    private const string Callback = "http://127.0.0.1:44444/callback";

    private static void ConfigureSynth(IWebHostBuilder builder)
    {
        builder.UseSetting("Synth:PublicUrl", "https://synth.example/");
        builder.UseSetting("Auth:Clients:Synth:Secret", "synth-test-secret");
    }

    private static JsonNode Song(string? name = "My Song") => new JsonObject { ["name"] = name, ["tracks"] = new JsonArray(1, 2, 3) };

    private Task<HttpResponseMessage> CreateSong(HttpClient client, JsonNode? doc = null, string? localId = null) =>
        client.PostJson("/api/v1/synth/songs", new { localId = localId ?? Guid.NewGuid().ToString(), doc = doc ?? Song() }, Ct);

    private async Task<JsonNode> CreatedSong(HttpClient client)
    {
        var response = await CreateSong(client);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await response.Json(Ct);
    }

    private Task<HttpResponseMessage> PutSong(HttpClient client, string id, JsonNode doc, int? baseRevision, string? clientId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/synth/songs/{id}") { Content = JsonContent.Create(new { doc, baseRevision }) };
        if (clientId is not null) request.Headers.Add("X-Synth-Client", clientId);
        return client.SendAsync(request, Ct);
    }

    private async Task<HttpClient> OAuth(HttpClient browser, string scope, string? resource)
    {
        var anonymous = F.Factory.Browser(cookies: false);
        var register = await anonymous.PostJson("/connect/register", new
        {
            client_name = "Test MCP",
            redirect_uris = new[] { Callback },
            grant_types = new[] { "authorization_code" },
            response_types = new[] { "code" },
            token_endpoint_auth_method = "none",
        }, Ct);
        register.StatusCode.ShouldBe(HttpStatusCode.Created);
        var clientId = (await register.Json(Ct))["client_id"]!.GetValue<string>();

        var (verifier, challenge) = AuthTestSupport.Pkce();
        var url = "/connect/authorize?" + AuthTestSupport.Q(
        [
            new("client_id", clientId), new("response_type", "code"), new("redirect_uri", Callback), new("scope", scope),
            new("code_challenge", challenge), new("code_challenge_method", "S256"), new("state", "s"),
        ]);
        (await browser.PostJson("/api/v1/account/consent", new { @return = url, approve = true }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var authorize = url + (resource is null ? "" : "&resource=" + Uri.EscapeDataString(resource));
        var code = Uri.UnescapeDataString((await browser.GetAsync(authorize, Ct)).Headers.Location!.Query.TrimStart('?').Split('&')
            .First(p => p.StartsWith("code=", StringComparison.Ordinal))[5..]);

        var fields = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "authorization_code"), new("code", code), new("redirect_uri", Callback),
            new("code_verifier", verifier), new("client_id", clientId),
        };
        if (resource is not null) fields.Add(new("resource", resource));
        var token = await (await anonymous.PostAsync("/connect/token", new FormUrlEncodedContent(fields), Ct)).Content.ReadJson(Ct);
        var client = F.Factory.Browser(cookies: false);
        client.DefaultRequestHeaders.Remove("X-Requested-With");
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token["access_token"]!.GetValue<string>());
        return client;
    }

    [Fact]
    public async Task Crud_And_RevisionConflict()
    {
        var (session, _, _) = await F.User();
        var created = await CreatedSong(session);
        var id = created["id"]!.GetValue<string>();
        created["revision"]!.GetValue<int>().ShouldBe(1);
        created["name"]!.GetValue<string>().ShouldBe("My Song");
        created["doc"]!["tracks"]!.AsArray().Count.ShouldBe(3);

        var list = await (await session.GetAsync("/api/v1/synth/songs", Ct)).Json(Ct);
        list["songs"]!.AsArray().Count.ShouldBe(1);
        list["songs"]![0]!["doc"].ShouldBeNull();
        list["songs"]![0]!["localId"].ShouldNotBeNull();

        var saved = await PutSong(session, id, Song("Renamed"), 1);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await saved.Json(Ct);
        dto["revision"]!.GetValue<int>().ShouldBe(2);
        dto["name"]!.GetValue<string>().ShouldBe("Renamed");

        var conflict = await PutSong(session, id, Song("Stale"), 1);
        conflict.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await conflict.Json(Ct);
        problem["code"]!.GetValue<string>().ShouldBe("revision_conflict");
        problem["current"]!["revision"]!.GetValue<int>().ShouldBe(2);
        problem["current"]!["doc"]!["name"]!.GetValue<string>().ShouldBe("Renamed");

        var unconditional = await PutSong(session, id, Song("Forced"), null);
        (await unconditional.Json(Ct))["revision"]!.GetValue<int>().ShouldBe(3);

        (await session.DeleteAsync($"/api/v1/synth/songs/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await session.GetAsync($"/api/v1/synth/songs/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await session.DeleteAsync($"/api/v1/synth/songs/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_IsIdempotentPerLocalId_AndOwnerScoped()
    {
        var (session, _, _) = await F.User();
        var (other, _, _) = await F.User();
        var first = await CreateSong(session, localId: "local-1");
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await first.Json(Ct))["id"]!.GetValue<string>();

        var again = await CreateSong(session, localId: "local-1");
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.Json(Ct))["id"]!.GetValue<string>().ShouldBe(id);

        (await CreateSong(other, localId: "local-1")).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await other.GetAsync($"/api/v1/synth/songs/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PutSong(other, id, Song(), 1)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.DeleteAsync($"/api/v1/synth/songs/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Limits_And_Naming()
    {
        var (session, _, userId) = await F.User();
        (await session.PostJson("/api/v1/synth/songs", new { localId = "x", doc = new[] { 1 } }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CreateSong(session, localId: new string('a', 200))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var big = Song();
        big["blob"] = new string('x', 512 * 1024 + 10);
        (await CreateSong(session, big)).StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);

        (await (await CreateSong(session, Song(new string('n', 100)))).Json(Ct))["name"]!.GetValue<string>().Length.ShouldBe(60);
        (await (await CreateSong(session, new JsonObject { ["x"] = 1 })).Json(Ct))["name"]!.GetValue<string>().ShouldBe("Untitled song");
        (await (await CreateSong(session, Song("  padded  "))).Json(Ct))["name"]!.GetValue<string>().ShouldBe("padded");

        await F.Sql($$"""
            insert into synth_songs (id, owner_id, local_id, name, doc, revision, created_at, updated_at)
            select gen_random_uuid(), '{{userId}}', 'bulk-' || g, 'x', jsonb_build_object(), 1, now(), now() from generate_series(1, 197) g
            """);
        var limit = await CreateSong(session);
        limit.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await limit.Json(Ct))["code"]!.GetValue<string>().ShouldBe("song_limit");
    }

    [Fact]
    public async Task Scopes_AreEnforced()
    {
        var (session, _, _) = await F.User();
        var id = (await CreatedSong(session))["id"]!.GetValue<string>();

        var reader = await Pat(session, "synth:read");
        (await reader.GetAsync("/api/v1/synth/songs", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync($"/api/v1/synth/songs/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CreateSong(reader)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await PutSong(reader, id, Song(), 1)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.DeleteAsync($"/api/v1/synth/songs/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var writer = await Pat(session, "synth:write");
        (await CreateSong(writer)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await writer.GetAsync("/api/v1/synth/songs", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var forgeOnly = await Pat(session, "forge:read", "forge:write");
        (await forgeOnly.GetAsync("/api/v1/synth/songs", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await F.Factory.Browser(cookies: false).GetAsync("/api/v1/synth/songs", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Audience_SynthMcpTokenWorksOnSynthOnly()
    {
        var (session, _, _) = await F.User();
        const string scope = "openid synth:read synth:write forge:read";

        var synthToken = await OAuth(session, scope, SynthMcp);
        (await CreateSong(synthToken)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await synthToken.GetAsync("/api/v1/synth/songs", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var forge = await synthToken.GetAsync("/api/v1/forge/documents", Ct);
        forge.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await forge.Json(Ct))["code"]!.GetValue<string>().ShouldBe("invalid_audience");

        var studioToken = await OAuth(session, scope, "http://localhost:8087/");
        (await studioToken.GetAsync("/api/v1/synth/songs", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await studioToken.GetAsync("/api/v1/forge/documents", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SynthClient_IsSeeded()
    {
        await using var scope = F.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StudioDbContext>();
        var app = await db.Set<OpenIddictEntityFrameworkCoreApplication<Guid>>().SingleAsync(a => a.ClientId == OidcClients.SynthClient, Ct);
        app.ClientType.ShouldBe("confidential");
        app.ConsentType.ShouldBe("implicit");
        app.RedirectUris!.ShouldContain("https://synth.example/auth/callback");
        app.Permissions!.ShouldContain("scp:synth:write");
        app.Permissions!.ShouldContain("rsrc:" + SynthMcp);
        app.Permissions!.ShouldContain("rsrc:http://localhost:8087/");
    }

    [Fact]
    public async Task Events_StreamUpdatesWithDocAndDeletes()
    {
        var (session, _, _) = await F.User();
        var id = (await CreatedSong(session))["id"]!.GetValue<string>();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/synth/songs/{id}/events");
        using var response = await session.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/event-stream");
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(Ct));

        (await PutSong(session, id, Song("Live"), 1, "client-1")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await ForgeDocumentTests.ReadEvent(reader);
        updated.Name.ShouldBe("updated");
        updated.Data["revision"]!.GetValue<int>().ShouldBe(2);
        updated.Data["source"]!.GetValue<string>().ShouldBe("synth");
        updated.Data["clientId"]!.GetValue<string>().ShouldBe("client-1");
        updated.Data["doc"]!["name"]!.GetValue<string>().ShouldBe("Live");

        (await session.DeleteAsync($"/api/v1/synth/songs/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ForgeDocumentTests.ReadEvent(reader)).Name.ShouldBe("deleted");
    }
}
