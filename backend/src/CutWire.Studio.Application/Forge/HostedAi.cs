using System.Text.Json;
using System.Text.Json.Nodes;

namespace CutWire.Studio.Application.Forge;

public sealed class AiOptions
{
    public const string Section = "Ai";

    public string CloudflareAccountId { get; set; } = "";
    public string CloudflareToken { get; set; } = "";

    /// <summary>AI Gateway id; empty calls Workers AI directly.</summary>
    public string Gateway { get; set; } = "";

    public string Model { get; set; } = "@cf/zai-org/glm-4.7-flash";

    /// <summary>Replaces the Cloudflare base URL (tests, proxies).</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>Workers AI's free tier is 10,000 neurons per UTC day; stay below it.</summary>
    public double DailyNeurons { get; set; } = 9000;

    public double UserDailyNeurons { get; set; } = 600;
    public double NeuronsPerKInput { get; set; } = 5.5;
    public double NeuronsPerKOutput { get; set; } = 36.364;
    public int RequestsPerMinute { get; set; } = 8;
    public int CallTimeoutSeconds { get; set; } = 90;
}

public sealed record AiQuota(int UserLeft, double UserLimit, int GlobalLeft);

public interface IAiBudget
{
    Task<(double UserUsed, double GlobalUsed)> UsedAsync(Guid userId, CancellationToken ct);

    /// <summary>Books <paramref name="neurons"/> against the user and global budgets, or books nothing and returns false.</summary>
    Task<bool> ReserveAsync(Guid userId, double neurons, CancellationToken ct);

    /// <summary>Corrects a reservation once real usage is known (the delta may be negative).</summary>
    Task SettleAsync(Guid userId, double delta, CancellationToken ct);
}

/// <summary>One chat-completions call. Returns the answer in the non-streamed shape; reasoning and text reach <paramref name="onDelta"/> as they arrive.</summary>
public interface IChatModel
{
    Task<JsonObject> CompleteAsync(JsonArray messages, JsonArray tools, Func<string, string, Task> onDelta, CancellationToken ct);
}

public sealed record AiTurn(string Role, string Text);

public sealed record AiRunRequest(Guid UserId, string Prompt, IReadOnlyList<AiTurn> History, string DocJson);

public sealed record AiRunResult(string DocJson, string Reply, string? Stopped, AiQuota Quota);

/// <summary>The hosted assistant: a port of runAgent. Tools run in forge-core; the model, budget and streaming are around it.</summary>
public sealed class HostedAgent(IForgeRuntime runtime, IChatModel model, IAiBudget budget, Microsoft.Extensions.Options.IOptions<AiOptions> options)
{
    public const int MaxSteps = 20;
    public const int MaxOutputTokens = 2048;
    public const int MaxReply = 600;
    public const int MaxThinking = 2000;
    private const string OutOfBudget = "I ran out of today's free AI allowance, so I stopped here. Everything I did so far is kept.";

    private AiOptions Options => options.Value;

    public async Task<AiQuota> QuotaAsync(Guid userId, CancellationToken ct)
    {
        var (user, global) = await budget.UsedAsync(userId, ct);
        return new AiQuota((int)Math.Max(0, Math.Floor(Options.UserDailyNeurons - user)), Options.UserDailyNeurons,
            (int)Math.Max(0, Math.Floor(Options.DailyNeurons - global)));
    }

    public async Task<bool> HasAllowanceAsync(Guid userId, CancellationToken ct)
    {
        var (user, global) = await budget.UsedAsync(userId, ct);
        return user < Options.UserDailyNeurons && global < Options.DailyNeurons;
    }

    private double Neurons(double input, double output) => input / 1000 * Options.NeuronsPerKInput + output / 1000 * Options.NeuronsPerKOutput;

    public async Task<AiRunResult> RunAsync(AiRunRequest request, Func<JsonObject, Task> send, CancellationToken ct)
    {
        var docJson = request.DocJson;
        var created = new HashSet<string>();
        var messages = new JsonArray();
        foreach (var turn in request.History)
            messages.Add(new JsonObject { ["role"] = turn.Role, ["content"] = turn.Role == "assistant" && turn.Text.Length == 0 ? null : turn.Text });
        messages.Add(new JsonObject { ["role"] = "user", ["content"] = runtime.AgentSetup(docJson, request.Prompt).First });

        string? reply = null;
        string? stopped = null;
        for (var step = 0; step < MaxSteps && reply is null; step++)
        {
            var estimate = Neurons(Math.Ceiling((messages.ToJsonString().Length + 14_000) / 3.0), MaxOutputTokens);
            if (!await budget.ReserveAsync(request.UserId, estimate, ct))
            {
                stopped = OutOfBudget;
                break;
            }

            var reserved = estimate;
            var setup = runtime.AgentSetup(docJson, request.Prompt);
            var shown = new Dictionary<string, int> { ["thinking"] = 0, ["text"] = 0 };
            var limits = new Dictionary<string, int> { ["thinking"] = MaxThinking, ["text"] = MaxReply };
            async Task OnDelta(string channel, string text)
            {
                var room = limits[channel] - shown[channel];
                if (room <= 0 || text.Length == 0) return;
                var part = text.Length > room ? text[..room] : text;
                shown[channel] += part.Length;
                await send(new JsonObject { ["type"] = "delta", ["channel"] = channel, ["text"] = part });
            }

            var all = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = setup.System } };
            foreach (var m in messages) all.Add(m!.DeepClone());

            // A failed call keeps its reservation booked: the model may have been billed for it.
            var completion = await model.CompleteAsync(all, setup.Tools, OnDelta, ct);

            var parsed = runtime.ParseCompletion(completion.ToJsonString());
            var actual = parsed.Neurons ?? (parsed.InputTokens is not null || parsed.OutputTokens is not null
                ? Neurons(parsed.InputTokens ?? 0, parsed.OutputTokens ?? 0)
                : reserved);
            await budget.SettleAsync(request.UserId, actual - reserved, ct);

            if (!string.IsNullOrWhiteSpace(parsed.Thinking) && shown["thinking"] == 0)
                await send(new JsonObject { ["type"] = "thinking", ["text"] = Cap(parsed.Thinking.Trim(), MaxThinking) });
            if (!string.IsNullOrWhiteSpace(parsed.Text) && parsed.Calls.Count > 0 && shown["text"] == 0)
                await send(new JsonObject { ["type"] = "text", ["text"] = Cap(parsed.Text.Trim(), MaxReply) });

            messages.Add(AssistantMessage(parsed));
            if (parsed.Calls.Count == 0)
            {
                reply = parsed.Text;
                break;
            }

            JsonNode? pending = null;
            foreach (var call in parsed.Calls)
            {
                var outcome = runtime.AgentCall(docJson, call.Name, call.Args.ToJsonString());
                if (outcome.Pending is not null) pending = outcome.Pending;
                else if (call.Name is not ("ask_user" or "propose_plan"))
                {
                    if (outcome.DocJson is not null) docJson = outcome.DocJson;
                    foreach (var id in outcome.Created) created.Add(id);
                    var tool = new JsonObject { ["type"] = "tool", ["name"] = call.Name, ["result"] = Cap(outcome.Result, 300) };
                    if (outcome.IsError) tool["error"] = true;
                    await send(tool);
                }

                messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = call.Id, ["content"] = outcome.Result });
            }

            await send(new JsonObject { ["type"] = "progress", ["doc"] = JsonNode.Parse(created.Count > 0 ? runtime.Place(docJson, created) : docJson) });
            if (pending is not null)
            {
                await send(new JsonObject { ["type"] = "pending", ["pending"] = pending });
                reply = parsed.Text;
            }
        }

        if (reply is null && stopped is null) stopped = $"I stopped after {MaxSteps} steps. Ask me to continue if it isn't finished.";
        var text = !string.IsNullOrEmpty(reply) ? reply : stopped ?? "";
        var finalDoc = created.Count > 0 ? runtime.Place(docJson, created) : docJson;
        return new AiRunResult(finalDoc, Cap(text.Trim(), MaxReply), stopped, await QuotaAsync(request.UserId, ct));
    }

    private static JsonObject AssistantMessage(ForgeModelReply reply)
    {
        var message = new JsonObject { ["role"] = "assistant", ["content"] = reply.Text.Length == 0 ? null : reply.Text };
        if (reply.Calls.Count > 0)
            message["tool_calls"] = new JsonArray(reply.Calls.Select(c => (JsonNode)new JsonObject
            {
                ["id"] = c.Id,
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.Args.ToJsonString() },
            }).ToArray());
        return message;
    }

    private static string Cap(string text, int max) => text.Length > max ? text[..max] : text;
}
