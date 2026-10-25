using System.Collections.Concurrent;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using CutWire.Studio.Api.Auth;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Forge;
using CutWire.Studio.Domain.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Api.Endpoints;

/// <summary>Fixed-window counter per user, in memory (one server process).</summary>
public sealed class FixedWindowLimiter(TimeSpan window)
{
    private readonly ConcurrentDictionary<string, (int Count, DateTime ResetAt)> _hits = new();

    public bool Take(string key, int limit)
    {
        var now = DateTime.UtcNow;
        if (_hits.Count > 10_000)
            foreach (var (k, v) in _hits)
                if (v.ResetAt < now) _hits.TryRemove(k, out _);
        var updated = _hits.AddOrUpdate(key, _ => (1, now + window), (_, v) => v.ResetAt < now ? (1, now + window) : (v.Count + 1, v.ResetAt));
        return updated.Count <= limit;
    }
}

/// <summary>
/// The hosted assistant, as specified in web/apps/forge/docs/ai-endpoint.md. Error bodies here are <c>{ error }</c> (not problem JSON)
/// because that is what the client reads. Optional additions: <c>cloudId</c> (or header <c>X-Forge-Doc</c>) makes the run persist its result
/// into that cloud document and broadcast it to open editors.
/// </summary>
public static class ForgeAiEndpoints
{
    public const int MaxBody = 256 * 1024;
    public const int MaxPrompt = 1500;
    public const int MaxHistory = 8;
    public const int MaxNodes = 150;
    public const string DocHeader = "X-Forge-Doc";
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static void MapForgeAi(this RouteGroupBuilder api)
    {
        api.MapPost("/forge/ai", Handle)
            .WithName("ForgeAi")
            .WithTags("Forge")
            .Accepts<ForgeAiRequest>("application/json")
            .Produces(StatusCodes.Status200OK, contentType: "application/x-ndjson")
            .Produces<ForgeAiError>(StatusCodes.Status400BadRequest)
            .Produces<ForgeAiError>(StatusCodes.Status401Unauthorized)
            .Produces<ForgeAiError>(StatusCodes.Status413PayloadTooLarge)
            .Produces<ForgeAiError>(StatusCodes.Status429TooManyRequests);
    }

    private static IResult Fail(int status, string message, object? quota = null) =>
        Results.Json(quota is null ? new ForgeAiError(message, null) : new ForgeAiError(message, quota), statusCode: status);

    private static async Task<IResult> Handle(HttpContext http, HostedAgent agent, IForgeRuntime runtime, FixedWindowLimiter limiter,
        IOptions<AiOptions> options, IAppDbContext db, ForgeDocuments docs, ForgeEditor editor, ILogger<HostedAgent> logger)
    {
        var ct = http.RequestAborted;
        var auth = await http.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!auth.Succeeded || auth.Principal is null) return Fail(401, "Sign in with your CutWire account to use the built-in AI.");
        var userId = auth.Principal.GetUserId();
        var profile = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == userId, ct);
        if (profile is null || profile.IsBanned(DateTimeOffset.UtcNow)) return Fail(403, "Your account can't use the built-in AI.");
        if (!limiter.Take(userId.ToString(), options.Value.RequestsPerMinute))
            return Fail(429, "Slow down a little: too many requests in the last minute.");

        var raw = await Application.Catalog.BoundedRead.ReadAsync(http.Request.Body, MaxBody, ct);
        if (raw.Length > MaxBody) return Fail(413, "This effect is too large for the built-in AI.");
        JsonObject? body;
        try
        {
            body = JsonNode.Parse(raw) as JsonObject;
        }
        catch (JsonException)
        {
            body = null;
        }

        if (body is null) return Fail(400, "Bad request.");
        var prompt = (body["prompt"] is JsonValue p && p.TryGetValue<string>(out var text) ? text.Trim() : "");
        if (prompt.Length > MaxPrompt) prompt = prompt[..MaxPrompt];
        if (prompt.Length == 0) return Fail(400, "Say what you'd like to make.");

        // Only plain text turns survive: no roles, tool calls or instructions beyond user/assistant text.
        var history = (body["history"] as JsonArray ?? [])
            .Select(t => t as JsonObject)
            .Where(t => t?["text"] is JsonValue v && v.TryGetValue<string>(out _) && t["role"] is JsonValue r && r.TryGetValue<string>(out var role) && role is "user" or "assistant")
            .TakeLast(MaxHistory)
            .Select(t => new AiTurn(t!["role"]!.GetValue<string>(), Truncate(t["text"]!.GetValue<string>(), MaxPrompt)))
            .ToList();

        var parsed = runtime.ParseDoc(body["doc"]?.ToJsonString() ?? "null");
        if (parsed.IsFailure) return Fail(400, parsed.Error.Message);
        var docJson = parsed.Value;
        if (JsonNode.Parse(docJson)!["nodes"] is JsonArray nodes && nodes.Count > MaxNodes) return Fail(413, "This graph is too big for the built-in AI.");

        if (!await agent.HasAllowanceAsync(userId, ct))
            return Fail(429, "The built-in AI has used today's free allowance. It resets at midnight UTC, or add your own AI key in settings.",
                await agent.QuotaAsync(userId, ct));

        Guid? cloudId = null;
        var requested = http.Request.Headers[DocHeader].ToString();
        if (string.IsNullOrEmpty(requested) && body["cloudId"] is JsonValue c && c.TryGetValue<string>(out var fromBody)) requested = fromBody;
        if (!string.IsNullOrEmpty(requested))
        {
            if (!Guid.TryParse(requested, out var parsedId) || !await docs.ExistsAsync(userId, parsedId, ct)) return Fail(404, "That cloud document doesn't exist.");
            cloudId = parsedId;
        }

        var clientId = http.ClientIdOf();
        http.Response.ContentType = "application/x-ndjson; charset=utf-8";
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers["X-Accel-Buffering"] = "no";
        await http.Response.StartAsync(ct);

        async Task Send(JsonObject e)
        {
            await http.Response.WriteAsync(e.ToJsonString(Json) + "\n", Encoding.UTF8, ct);
            await http.Response.Body.FlushAsync(ct);
        }

        try
        {
            var run = await agent.RunAsync(new AiRunRequest(userId, prompt, history, docJson), Send, ct);
            var done = new JsonObject
            {
                ["type"] = "done",
                ["doc"] = JsonNode.Parse(run.DocJson),
                ["reply"] = run.Reply,
                ["quota"] = JsonSerializer.SerializeToNode(run.Quota, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            };
            if (run.Stopped is not null) done["stopped"] = run.Stopped;
            if (cloudId is { } id && run.DocJson != docJson && await Persist(id) is { } saved)
                done["cloud"] = new JsonObject { ["revision"] = saved.Revision, ["updatedAt"] = saved.UpdatedAt };
            await Send(done);

            async Task<ForgeSavedDto?> Persist(Guid documentId)
            {
                var saved = await editor.MutateAsync(userId, documentId, ForgeSources.Ai, clientId, loaded =>
                {
                    // The client blanks asset data and the AI can't add assets: keep the stored ones.
                    var result = JsonNode.Parse(run.DocJson)!.AsObject();
                    var stored = JsonNode.Parse(loaded.DocJson)!.AsObject();
                    result["assets"] = stored["assets"]?.DeepClone() ?? new JsonArray();
                    if (stored["preview"] is { } preview) result["preview"] = preview.DeepClone();
                    return Task.FromResult(new Mutation<bool>(result.ToJsonString(), true));
                }, ct);
                if (saved.IsFailure) logger.LogWarning("Could not save AI result to {Document}: {Error}", documentId, saved.Error.Code);
                return saved.IsSuccess ? saved.Value.Saved : null;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            logger.LogError(e, "AI request failed");
            if (!ct.IsCancellationRequested)
                await Send(new JsonObject
                {
                    ["type"] = "error",
                    ["error"] = "The AI couldn't finish that. Try again in a moment.",
                    ["quota"] = JsonSerializer.SerializeToNode(await agent.QuotaAsync(userId, CancellationToken.None), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                });
        }

        return Results.Empty;
    }

    private static string Truncate(string text, int max) => text.Length > max ? text[..max] : text;
}

public sealed record ForgeAiRequest(string Prompt, IReadOnlyList<ForgeAiTurn>? History, JsonElement Doc, string? CloudId);

public sealed record ForgeAiTurn(string Role, string Text);

public sealed record ForgeAiError(string Error, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] object? Quota);
