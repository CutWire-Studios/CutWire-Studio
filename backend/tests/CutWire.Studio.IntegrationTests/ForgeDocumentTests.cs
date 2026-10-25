using System.Net;
using System.Text.Json.Nodes;
using CutWire.Studio.Infrastructure.Persistence;
using CutWire.Studio.UnitTests;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace CutWire.Studio.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class ForgeDocumentTests(PostgresFixture postgres) : ForgeTestBase(postgres)
{
    [Fact]
    public async Task Crud_And_RevisionConflict()
    {
        var (session, _, _) = await F.User();
        var created = await Created(session);
        var id = created["id"]!.GetValue<string>();
        created["revision"]!.GetValue<int>().ShouldBe(1);
        created["kind"]!.GetValue<string>().ShouldBe("effect");
        created["name"]!.GetValue<string>().ShouldBe("Test Fx");
        created["doc"]!["forge"].ShouldNotBeNull();

        var list = await (await session.GetAsync("/api/v1/forge/documents", Ct)).Json(Ct);
        list["documents"]!.AsArray().Count.ShouldBe(1);
        list["documents"]![0]!["thumbUrl"].ShouldBeNull();

        var doc = JsonNode.Parse(created["doc"]!.ToJsonString())!;
        doc["meta"]!["displayName"] = "Renamed";
        var saved = await Put(session, id, doc.ToJsonString(), 1);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await saved.Json(Ct))["revision"]!.GetValue<int>().ShouldBe(2);

        var conflict = await Put(session, id, doc.ToJsonString(), 1);
        conflict.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await conflict.Json(Ct);
        problem["code"]!.GetValue<string>().ShouldBe("revision_conflict");
        problem["current"]!["revision"]!.GetValue<int>().ShouldBe(2);
        problem["current"]!["doc"]!["meta"]!["displayName"]!.GetValue<string>().ShouldBe("Renamed");

        var got = await (await session.GetAsync($"/api/v1/forge/documents/{id}", Ct)).Json(Ct);
        got["name"]!.GetValue<string>().ShouldBe("Renamed");

        (await session.DeleteAsync($"/api/v1/forge/documents/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await session.GetAsync($"/api/v1/forge/documents/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await session.DeleteAsync($"/api/v1/forge/documents/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_IsIdempotentPerLocalId_AndOwnerScoped()
    {
        var (session, _, _) = await F.User();
        var (other, _, _) = await F.User();
        var first = await Create(session, NewDoc(), "local-1");
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await first.Json(Ct))["id"]!.GetValue<string>();

        var again = await Create(session, NewDoc(), "local-1");
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await again.Json(Ct);
        problem["code"]!.GetValue<string>().ShouldBe("document_exists");
        problem["id"]!.GetValue<string>().ShouldBe(id);

        (await Create(other, NewDoc(), "local-1")).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await other.GetAsync($"/api/v1/forge/documents/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Put(other, id, NewDoc(), 1)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.DeleteAsync($"/api/v1/forge/documents/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Limits_Validation()
    {
        var (session, _, userId) = await F.User();
        (await session.PostJson("/api/v1/forge/documents", new { localId = "x", doc = new { nope = 1 } }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Create(session, NewDoc("effect"), new string('a', 200))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var big = JsonNode.Parse(NewDoc())!;
        big["meta"]!["description"] = new string('x', 2 * 1024 * 1024 + 10);
        (await Create(session, big.ToJsonString())).StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);

        var created = await Created(session);
        var wrongKind = JsonNode.Parse(NewDoc("transition"))!;
        (await Put(session, created["id"]!.GetValue<string>(), wrongKind.ToJsonString(), 1)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await F.Sql($$"""
            insert into forge_documents (id, owner_id, local_id, kind, name, doc_json, revision, created_at, updated_at)
            select gen_random_uuid(), '{{userId}}', 'bulk-' || g, 'effect', 'x', chr(123) || chr(125), 1, now(), now() from generate_series(1, 499) g
            """);
        var limit = await Create(session, NewDoc());
        limit.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await limit.Json(Ct))["code"]!.GetValue<string>().ShouldBe("document_limit");
    }

    [Fact]
    public async Task Scopes_AreEnforced()
    {
        var (session, _, _) = await F.User();
        var created = await Created(session);
        var id = created["id"]!.GetValue<string>();

        var reader = await Pat(session, "forge:read");
        (await reader.GetAsync("/api/v1/forge/documents", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.GetAsync($"/api/v1/forge/documents/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Create(reader, NewDoc())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Put(reader, id, NewDoc(), 1)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.DeleteAsync($"/api/v1/forge/documents/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.PostAsync("/api/v1/forge/assets", Raw([1, 2, 3], "image/png"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var writer = await Pat(session, "forge:write");
        (await Create(writer, NewDoc())).StatusCode.ShouldBe(HttpStatusCode.Created);

        var anonymous = F.Factory.Browser(cookies: false);
        (await anonymous.GetAsync("/api/v1/forge/documents", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Assets_Upload_InlineRejection_And_RefCounts()
    {
        var (session, _, _) = await F.User();
        var png = Images.Png(16, 16);
        var upload = await session.PostAsync("/api/v1/forge/assets", Raw(png, "image/png"), Ct);
        upload.StatusCode.ShouldBe(HttpStatusCode.OK);
        var asset = await upload.Json(Ct);
        var sha = asset["sha256"]!.GetValue<string>();
        asset["url"]!.GetValue<string>().ShouldBe($"/files/{sha}");
        (await session.GetAsync($"/files/{sha}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await session.PostAsync("/api/v1/forge/assets", Raw(png, "text/html"), Ct)).StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        (await session.PostAsync("/api/v1/forge/assets", Raw(png, "image/svg+xml"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await session.PostAsync("/api/v1/forge/assets", Raw(new byte[8 * 1024 * 1024 + 1], "image/png"), Ct)).StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);

        async Task<int> Refs() => await F.Db(db => db.Blobs.Where(b => b.Id == new CutWire.Studio.Domain.Blobs.Sha256Hash(sha)).Select(b => b.RefCount).SingleAsync(Ct));
        var baseline = await Refs();

        var withBlob = JsonNode.Parse(NewDoc())!;
        withBlob["assets"] = new JsonArray(new JsonObject { ["id"] = "a1", ["name"] = "pic.png", ["mime"] = "image/png", ["blob"] = sha });
        var created = await Created(session, withBlob.ToJsonString());
        var id = created["id"]!.GetValue<string>();
        (await Refs()).ShouldBe(baseline + 1);

        var inline = JsonNode.Parse(withBlob.ToJsonString())!;
        inline["assets"] = new JsonArray(new JsonObject { ["id"] = "a1", ["name"] = "pic.png", ["mime"] = "image/png", ["data"] = new string('A', 2000) });
        var rejected = await Create(session, inline.ToJsonString());
        rejected.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await rejected.Json(Ct))["code"]!.GetValue<string>().ShouldBe("inline_asset_too_large");

        var small = JsonNode.Parse(withBlob.ToJsonString())!;
        small["assets"] = new JsonArray(new JsonObject { ["id"] = "a1", ["name"] = "dot.png", ["mime"] = "image/png", ["data"] = "AAAA" });
        (await Create(session, small.ToJsonString())).StatusCode.ShouldBe(HttpStatusCode.Created);

        var missing = JsonNode.Parse(withBlob.ToJsonString())!;
        missing["assets"] = new JsonArray(new JsonObject { ["id"] = "a1", ["name"] = "x", ["mime"] = "image/png", ["blob"] = new string('0', 64) });
        (await Create(session, missing.ToJsonString())).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var without = JsonNode.Parse(NewDoc())!;
        (await Put(session, id, without.ToJsonString(), 1)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Refs()).ShouldBe(baseline);

        (await Put(session, id, withBlob.ToJsonString(), 2)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Refs()).ShouldBe(baseline + 1);
        (await session.DeleteAsync($"/api/v1/forge/documents/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Refs()).ShouldBe(baseline);
    }

    [Fact]
    public async Task Thumbnail_Upload()
    {
        var (session, _, _) = await F.User();
        var id = (await Created(session))["id"]!.GetValue<string>();
        var thumb = Images.Good(64, 64, 30);
        var png = Images.Png(8, 8);
        (await session.PutAsync($"/api/v1/forge/documents/{id}/thumb", Raw(thumb, "image/webp"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var list = await (await session.GetAsync("/api/v1/forge/documents", Ct)).Json(Ct);
        list["documents"]![0]!["thumbUrl"]!.GetValue<string>().ShouldStartWith("/files/");
        (await session.PutAsync($"/api/v1/forge/documents/{id}/thumb", Raw(thumb, "image/gif"), Ct)).StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        (await session.PutAsync($"/api/v1/forge/documents/{id}/thumb", Raw(png, "image/webp"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await session.PutAsync($"/api/v1/forge/documents/{id}/thumb", Raw(new byte[70 * 1024], "image/webp"), Ct)).StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Events_StreamUpdatesAndDeletes()
    {
        var (session, _, _) = await F.User();
        var id = (await Created(session))["id"]!.GetValue<string>();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/forge/documents/{id}/events");
        using var response = await session.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/event-stream");
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(Ct));

        (await Put(session, id, NewDoc(), 1, "11111111-1111-1111-1111-111111111111")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await ReadEvent(reader);
        updated.Name.ShouldBe("updated");
        updated.Data["revision"]!.GetValue<int>().ShouldBe(2);
        updated.Data["source"]!.GetValue<string>().ShouldBe("web");
        updated.Data["clientId"]!.GetValue<string>().ShouldBe("11111111-1111-1111-1111-111111111111");

        (await session.DeleteAsync($"/api/v1/forge/documents/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ReadEvent(reader)).Name.ShouldBe("deleted");
    }

    internal static async Task<(string Name, JsonNode Data)> ReadEvent(StreamReader reader)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        string? name = null;
        while (await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            if (line.StartsWith("event: ", StringComparison.Ordinal)) name = line[7..];
            else if (line.StartsWith("data: ", StringComparison.Ordinal) && name is not null) return (name, JsonNode.Parse(line[6..])!);
        }

        throw new InvalidOperationException("The event stream ended.");
    }
}
