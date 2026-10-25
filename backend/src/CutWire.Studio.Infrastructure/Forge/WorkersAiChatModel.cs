using System.Text;
using System.Text.Json.Nodes;
using CutWire.Studio.Application.Forge;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Infrastructure.Forge;

/// <summary>Cloudflare Workers AI (through AI Gateway when configured), OpenAI-format chat completions, streamed.</summary>
public sealed class WorkersAiChatModel(HttpClient http, IOptions<AiOptions> options) : IChatModel
{
    public const int MaxOutputTokens = 2048;

    public async Task<JsonObject> CompleteAsync(JsonArray messages, JsonArray tools, Func<string, string, Task> onDelta, CancellationToken ct)
    {
        var o = options.Value;
        var baseUrl = !string.IsNullOrEmpty(o.BaseUrl) ? o.BaseUrl.TrimEnd('/')
            : !string.IsNullOrEmpty(o.Gateway) ? $"https://gateway.ai.cloudflare.com/v1/{o.CloudflareAccountId}/{o.Gateway}/workers-ai"
            : $"https://api.cloudflare.com/client/v4/accounts/{o.CloudflareAccountId}/ai/run";

        var body = new JsonObject
        {
            ["messages"] = messages.DeepClone(),
            ["tools"] = tools.DeepClone(),
            ["tool_choice"] = "auto",
            ["max_completion_tokens"] = MaxOutputTokens,
            ["temperature"] = 0.2,
            ["stream"] = true,
            ["stream_options"] = new JsonObject { ["include_usage"] = true },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/{o.Model}")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", o.CloudflareToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(o.CallTimeoutSeconds));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(timeout.Token);
            throw new HttpRequestException($"Workers AI answered {(int)response.StatusCode}: {(text.Length > 300 ? text[..300] : text)}");
        }

        if (response.Content.Headers.ContentType?.MediaType?.Contains("event-stream") != true)
        {
            var whole = JsonNode.Parse(await response.Content.ReadAsStringAsync(timeout.Token))!.AsObject();
            return whole["result"] as JsonObject ?? whole;
        }

        var acc = new Accumulator(onDelta);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(timeout.Token));
        while (await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            line = line.Trim();
            if (line.StartsWith("data:", StringComparison.Ordinal) && line != "data: [DONE]") await acc.AddAsync(line[5..].Trim());
        }

        return acc.Result();
    }

    /// <summary>Builds a non-streamed completion out of streamed deltas, forwarding text as it comes.</summary>
    private sealed class Accumulator(Func<string, string, Task> onDelta)
    {
        private readonly StringBuilder _content = new();
        private readonly StringBuilder _reasoning = new();
        private readonly List<(string? Id, string? Name, StringBuilder Args)> _calls = [];
        private JsonObject? _usage;

        public async Task AddAsync(string data)
        {
            JsonObject? chunk;
            try
            {
                chunk = JsonNode.Parse(data) as JsonObject;
            }
            catch (System.Text.Json.JsonException)
            {
                return;
            }

            if (chunk is null) return;
            // Some servers send running increments as well as the total; the total is the largest.
            if (chunk["usage"] is JsonObject usage && Total(usage) >= Total(_usage)) _usage = usage.DeepClone().AsObject();
            if ((chunk["choices"] as JsonArray)?.FirstOrDefault()?["delta"] is not JsonObject delta) return;

            // Workers AI sends the same reasoning under both names.
            var thinking = delta["reasoning_content"]?.GetValue<string>() ?? delta["reasoning"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(thinking))
            {
                _reasoning.Append(thinking);
                await onDelta("thinking", thinking);
            }

            if (delta["content"] is JsonValue c && c.TryGetValue<string>(out var content) && content.Length > 0)
            {
                _content.Append(content);
                await onDelta("text", content);
            }

            foreach (var call in (delta["tool_calls"] as JsonArray) ?? [])
            {
                var index = call?["index"]?.GetValue<int>() ?? _calls.Count;
                while (_calls.Count <= index) _calls.Add((null, null, new StringBuilder()));
                var current = _calls[index];
                if (call?["id"]?.GetValue<string>() is { Length: > 0 } id) current.Id = id;
                var function = call?["function"];
                if (function?["name"]?.GetValue<string>() is { Length: > 0 } name && current.Name is null) current.Name = name;
                var args = function?["arguments"];
                if (args is JsonObject obj) current.Args.Clear().Append(obj.ToJsonString());
                else if (args is JsonValue v && v.TryGetValue<string>(out var s)) current.Args.Append(s);
                _calls[index] = current;
            }
        }

        private static double Total(JsonObject? usage) => usage?["total_tokens"]?.GetValue<double>() ?? 0;

        public JsonObject Result()
        {
            var message = new JsonObject { ["content"] = _content.ToString(), ["reasoning"] = _reasoning.ToString() };
            var calls = new JsonArray();
            foreach (var (id, name, args) in _calls)
                calls.Add(new JsonObject { ["id"] = id, ["function"] = new JsonObject { ["name"] = name, ["arguments"] = args.ToString() } });
            message["tool_calls"] = calls;
            var result = new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["message"] = message }) };
            if (_usage is not null) result["usage"] = _usage;
            return result;
        }
    }
}
