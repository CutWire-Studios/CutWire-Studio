using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using CutWire.Studio.Application.Forge;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace CutWire.Studio.IntegrationTests;

public sealed class FakeWorkersAi : HttpMessageHandler
{
    public List<JsonNode> Requests { get; } = [];
    public List<string> Urls { get; } = [];
    public Queue<Func<HttpResponseMessage>> Replies { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Urls.Add(request.RequestUri!.ToString());
        Requests.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!);
        request.Headers.Authorization!.Parameter.ShouldBe("test-token");
        return Replies.Count > 0 ? Replies.Dequeue()() : Stream("Done.");
    }

    public static HttpResponseMessage Stream(string text, string? toolName = null, string? toolArgs = null, int total = 1200)
    {
        var sb = new StringBuilder();
        if (toolName is not null)
            sb.Append("data: ").Append(new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["delta"] = new JsonObject { ["tool_calls"] = new JsonArray(new JsonObject
            {
                ["index"] = 0, ["id"] = "call_1", ["function"] = new JsonObject { ["name"] = toolName, ["arguments"] = toolArgs },
            }) } }) }.ToJsonString()).Append("\n\n");
        else
            sb.Append("data: ").Append(new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["delta"] = new JsonObject { ["content"] = text } }) }.ToJsonString()).Append("\n\n");
        sb.Append("data: ").Append(new JsonObject { ["choices"] = new JsonArray(), ["usage"] = new JsonObject { ["prompt_tokens"] = total - 100, ["completion_tokens"] = 100, ["total_tokens"] = total } }.ToJsonString()).Append("\n\n");
        sb.Append("data: [DONE]\n\n");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sb.ToString(), Encoding.UTF8, "text/event-stream") };
    }
}

[Collection(PostgresCollection.Name)]
public sealed class ForgeAiTests : ForgeTestBase
{
    private readonly FakeWorkersAi _upstream;

    public ForgeAiTests(PostgresFixture postgres) : this(postgres, new FakeWorkersAi())
    {
    }

    private ForgeAiTests(PostgresFixture postgres, FakeWorkersAi upstream) : base(postgres, b =>
    {
        b.UseSetting("Ai:CloudflareAccountId", "acct");
        b.UseSetting("Ai:CloudflareToken", "test-token");
        b.UseSetting("Ai:BaseUrl", "https://workers.test/ai");
        b.UseSetting("Ai:UserDailyNeurons", "300");
        b.UseSetting("Ai:DailyNeurons", "1000");
        b.ConfigureTestServices(s => s.AddHttpClient<IChatModel, CutWire.Studio.Infrastructure.Forge.WorkersAiChatModel>().ConfigurePrimaryHttpMessageHandler(() => upstream));
    }) => _upstream = upstream;

    private async Task<(HttpResponseMessage Response, List<JsonNode> Events)> Ask(HttpClient client, string prompt, string doc, string? cloudId = null)
    {
        var body = new JsonObject { ["prompt"] = prompt, ["history"] = new JsonArray(), ["doc"] = JsonNode.Parse(doc) };
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/forge/ai") { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        if (cloudId is not null) request.Headers.Add("X-Forge-Doc", cloudId);
        var response = await client.SendAsync(request, Ct);
        var events = new List<JsonNode>();
        if (response.Content.Headers.ContentType?.MediaType == "application/x-ndjson")
            foreach (var line in (await response.Content.ReadAsStringAsync(Ct)).Split('\n', StringSplitOptions.RemoveEmptyEntries)) events.Add(JsonNode.Parse(line)!);
        return (response, events);
    }

    [Fact]
    public async Task Run_StreamsEditsTheDoc_AndTracksQuota()
    {
        var (session, _, _) = await F.User();
        _upstream.Replies.Enqueue(() => FakeWorkersAi.Stream("", "add_block", """{"type":"blur"}"""));
        _upstream.Replies.Enqueue(() => FakeWorkersAi.Stream("Added a blur."));

        var (response, events) = await Ask(session, "add a blur", NewDoc());
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        events.Select(e => e["type"]!.GetValue<string>()).ShouldContain("tool");
        events.Select(e => e["type"]!.GetValue<string>()).ShouldContain("progress");
        var done = events[^1];
        done["type"]!.GetValue<string>().ShouldBe("done");
        done["reply"]!.GetValue<string>().ShouldBe("Added a blur.");
        done["doc"]!["nodes"]!.AsArray().Count.ShouldBe(3);
        done["quota"]!["userLimit"]!.GetValue<double>().ShouldBe(300);
        done["quota"]!["userLeft"]!.GetValue<int>().ShouldBeLessThan(300);
        _upstream.Urls[0].ShouldBe("https://workers.test/ai/@cf/zai-org/glm-4.7-flash");
        _upstream.Requests[0]["tools"]!.AsArray().Count.ShouldBeGreaterThan(5);
        _upstream.Requests[0]["stream"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task CloudDocument_IsPersistedAndBroadcast()
    {
        var (session, _, _) = await F.User();
        var created = await Created(session);
        var id = created["id"]!.GetValue<string>();
        using var stream = await session.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/api/v1/forge/documents/{id}/events"), HttpCompletionOption.ResponseHeadersRead, Ct);
        using var reader = new StreamReader(await stream.Content.ReadAsStreamAsync(Ct));

        _upstream.Replies.Enqueue(() => FakeWorkersAi.Stream("", "add_block", """{"type":"blur"}"""));
        _upstream.Replies.Enqueue(() => FakeWorkersAi.Stream("Added a blur."));
        var (_, events) = await Ask(session, "add a blur", created["doc"]!.ToJsonString(), id);
        events[^1]["cloud"]!["revision"]!.GetValue<int>().ShouldBe(2);

        var live = await ForgeDocumentTests.ReadEvent(reader);
        live.Data["source"]!.GetValue<string>().ShouldBe("ai");
        var stored = await (await session.GetAsync($"/api/v1/forge/documents/{id}", Ct)).Json(Ct);
        stored["doc"]!["nodes"]!.AsArray().Count.ShouldBe(3);
    }

    [Fact]
    public async Task Gates()
    {
        var anonymous = F.Factory.Browser();
        var (denied, _) = await Ask(anonymous, "hi", NewDoc());
        denied.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await denied.Json(Ct))["error"]!.GetValue<string>().ShouldContain("Sign in");

        var (session, _, _) = await F.User();
        var (empty, _) = await Ask(session, "   ", NewDoc());
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await empty.Json(Ct))["error"]!.GetValue<string>().ShouldBe("Say what you'd like to make.");

        var (badDoc, _) = await Ask(session, "hi", """{"forge":3}""");
        badDoc.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var notJson = await session.PostAsync("/api/v1/forge/ai", new StringContent("nope", Encoding.UTF8, "application/json"), Ct);
        notJson.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var big = await session.PostAsync("/api/v1/forge/ai", new StringContent(new string('x', 300 * 1024), Encoding.UTF8, "application/json"), Ct);
        big.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);

        _upstream.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task RateLimit_EightPerMinute()
    {
        var (session, _, _) = await F.User();
        for (var i = 0; i < 8; i++) (await Ask(session, "hi", "{}")).Response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var (limited, _) = await Ask(session, "hi", "{}");
        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await limited.Json(Ct))["error"]!.GetValue<string>().ShouldContain("Slow down");
    }

    [Fact]
    public async Task Budget_Exhaustion()
    {
        var (session, _, userId) = await F.User();
        // The reservation for one call is about 100 neurons, so the 300 a day allow a couple; the upstream reports big usage.
        _upstream.Replies.Enqueue(() => FakeWorkersAi.Stream("", "add_block", """{"type":"blur"}""", total: 250_000));
        _upstream.Replies.Enqueue(() => FakeWorkersAi.Stream("never"));
        var (response, events) = await Ask(session, "go", NewDoc());
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var done = events[^1];
        done["type"]!.GetValue<string>().ShouldBe("done");
        done["stopped"]!.GetValue<string>().ShouldContain("ran out of today's free AI allowance");
        done["doc"]!["nodes"]!.AsArray().Count.ShouldBe(3);
        _upstream.Requests.Count.ShouldBe(1);

        var (blocked, _) = await Ask(session, "again", NewDoc());
        blocked.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        var body = await blocked.Json(Ct);
        body["error"]!.GetValue<string>().ShouldContain("allowance");
        body["quota"]!["userLeft"]!.GetValue<int>().ShouldBe(0);
        _upstream.Requests.Count.ShouldBe(1);
        userId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task UpstreamFailure_ReportsGenericError()
    {
        var (session, _, _) = await F.User();
        _upstream.Replies.Enqueue(() => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("secret upstream detail") });
        var (response, events) = await Ask(session, "go", NewDoc());
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        events[^1]["type"]!.GetValue<string>().ShouldBe("error");
        events[^1]["error"]!.GetValue<string>().ShouldBe("The AI couldn't finish that. Try again in a moment.");
        events[^1].ToJsonString().ShouldNotContain("secret");
    }
}

[Collection(PostgresCollection.Name)]
public sealed class ThemeDraftTests(PostgresFixture postgres) : ForgeTestBase(postgres)
{
    [Fact]
    public async Task Crud_Limits_AndOwnership()
    {
        var (session, _, _) = await F.User();
        var (other, _, _) = await F.User();
        var anonymous = F.Factory.Browser();
        (await anonymous.GetAsync("/api/v1/themes/drafts", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        async Task<HttpResponseMessage> Put(HttpClient c, string id, object body) => await c.PutAsJsonAsync($"/api/v1/themes/drafts/{id}", body, Ct);

        var saved = await Put(session, "d1", new { id = "d1", name = "Sunset", doc = new { v = 1 }, updatedAt = 1000L });
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await saved.Json(Ct))["name"]!.GetValue<string>().ShouldBe("Sunset");

        (await (await Put(session, "d1", new { id = "d1", name = "Newer", doc = new { v = 2 }, updatedAt = 2000L })).Json(Ct))["doc"]!["v"]!.GetValue<int>().ShouldBe(2);
        (await (await Put(session, "d1", new { id = "d1", name = "Stale", doc = new { v = 0 }, updatedAt = 500L })).Json(Ct))["name"]!.GetValue<string>().ShouldBe("Newer");

        var list = await (await session.GetAsync("/api/v1/themes/drafts", Ct)).Json(Ct);
        list.AsArray().Count.ShouldBe(1);
        list[0]!["id"]!.GetValue<string>().ShouldBe("d1");
        list[0]!["updatedAt"]!.GetValue<long>().ShouldBe(2000);
        (await (await other.GetAsync("/api/v1/themes/drafts", Ct)).Json(Ct)).AsArray().Count.ShouldBe(0);
        (await other.GetAsync("/api/v1/themes/drafts/d1", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await session.GetAsync("/api/v1/themes/drafts/d1", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await Put(session, "bad id!", new { name = "x", doc = new { } })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Put(session, "d2", new { name = "x", doc = "str" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Put(session, "d3", new { name = "x", doc = new { blob = new string('x', 513 * 1024) } })).StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);

        for (var i = 0; i < 99; i++) (await Put(session, $"bulk{i}", new { name = "b", doc = new { } })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var over = await Put(session, "one-too-many", new { name = "b", doc = new { } });
        over.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Put(session, "d1", new { name = "still ok", doc = new { }, updatedAt = 3000L })).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await session.DeleteAsync("/api/v1/themes/drafts/d1", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await session.DeleteAsync("/api/v1/themes/drafts/d1", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await session.GetAsync("/api/v1/themes/drafts/d1", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
