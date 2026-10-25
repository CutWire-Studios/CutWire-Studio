using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using CutWire.Studio.Application.Forge;
using CutWire.Studio.Domain.Common;
using Jint;
using Jint.Native;
using Jint.Native.Function;
using Jint.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Infrastructure.Forge;

public sealed class ForgeRuntimeOptions
{
    public const string Section = "Forge:Runtime";

    /// <summary>Wall-clock limit for one call.</summary>
    public int TimeoutMs { get; set; } = 8000;

    /// <summary>Allocation limit for one call.</summary>
    public int MemoryLimitMb { get; set; } = 256;

    public int MaxRecursion { get; set; } = 400;

    /// <summary>Engines kept warm; more concurrent calls create short-lived extra ones.</summary>
    public int PoolSize { get; set; } = 4;
}

/// <summary>Thrown when forge-core itself throws (bad input) or a limit stops a call.</summary>
public sealed class ForgeRuntimeException(string message, bool limit = false) : Exception(message)
{
    public bool HitLimit { get; } = limit;
}

/// <summary>
/// forge-core's server bundle in Jint. The script is parsed once; each engine only has to run it. Engines are not thread-safe,
/// so a call takes one from the pool and gives it back (limit hits discard it).
/// </summary>
public sealed class ForgeRuntime : IForgeRuntime
{
    private static readonly string[] Functions =
    [
        "tools", "starters", "newDocument", "parseDoc", "view", "applyTool", "agentCall", "agentSetup", "place", "parseCompletion",
        "shader", "linkText", "exportPlan", "linkInfo",
    ];

    private readonly Prepared<Acornima.Ast.Script> _script;
    private readonly ForgeRuntimeOptions _options;
    private readonly ILogger<ForgeRuntime> _logger;
    private readonly ConcurrentBag<Instance> _pool = [];
    private readonly Lazy<IReadOnlyList<ForgeToolInfo>> _tools;
    private readonly Lazy<IReadOnlyList<ForgeStarter>> _starters;
    private readonly Lazy<(int Limit, string Prefix)> _link;

    public ForgeRuntime(IOptions<ForgeRuntimeOptions> options, ILogger<ForgeRuntime> logger)
    {
        _options = options.Value;
        _logger = logger;
        _script = Engine.PrepareScript(ReadBundle(), "forge-core.js");
        _tools = new Lazy<IReadOnlyList<ForgeToolInfo>>(() => JsonNode.Parse(Call("tools"))!.AsArray().Select(t => new ForgeToolInfo(
            t!["name"]!.GetValue<string>(), t["description"]!.GetValue<string>(), t["parameters"]!.DeepClone(),
            t["kinds"]!.AsArray().Select(k => k!.GetValue<string>()).ToList(), t["shared"]!.GetValue<bool>())).ToList());
        _starters = new Lazy<IReadOnlyList<ForgeStarter>>(() => JsonNode.Parse(Call("starters"))!.AsArray()
            .Select(s => new ForgeStarter(s!["name"]!.GetValue<string>(), s["kind"]!.GetValue<string>(), s["description"]!.GetValue<string>())).ToList());
        _link = new Lazy<(int, string)>(() =>
        {
            var info = JsonNode.Parse(Call("linkInfo"))!;
            return (info["limit"]!.GetValue<int>(), info["prefix"]!.GetValue<string>());
        });
    }

    public IReadOnlyList<ForgeToolInfo> Tools => _tools.Value;
    public IReadOnlyList<ForgeStarter> Starters => _starters.Value;
    public int LinkLimit => _link.Value.Limit;
    public string LinkPrefix => _link.Value.Prefix;

    /// <summary>Builds an engine (and so runs the whole bundle) ahead of the first request.</summary>
    public TimeSpan WarmUp()
    {
        var watch = Stopwatch.StartNew();
        _ = Tools;
        return watch.Elapsed;
    }

    public string NewDocument(string kind, string? name, string? starter) => Call("newDocument", kind, name, starter);

    public Result<string> ParseDoc(string text)
    {
        try
        {
            return Call("parseDoc", text);
        }
        catch (ForgeRuntimeException e) when (!e.HitLimit)
        {
            return Error.Validation("document_invalid", e.Message);
        }
    }

    public string View(string docJson) => Call("view", docJson);

    public ForgeToolOutcome Apply(string docJson, string tool, string argsJson) => Outcome(Call("applyTool", docJson, tool, argsJson));

    public ForgeToolOutcome AgentCall(string docJson, string tool, string argsJson) => Outcome(Call("agentCall", docJson, tool, argsJson));

    public ForgeAgentSetup AgentSetup(string docJson, string prompt)
    {
        var node = JsonNode.Parse(Call("agentSetup", docJson, prompt))!;
        return new ForgeAgentSetup(node["system"]!.GetValue<string>(), node["tools"]!.AsArray().DeepClone().AsArray(), node["first"]!.GetValue<string>());
    }

    public string Place(string docJson, IReadOnlyCollection<string> created) =>
        Call("place", docJson, new JsonArray(created.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray()).ToJsonString());

    public ForgeModelReply ParseCompletion(string completionJson)
    {
        var node = JsonNode.Parse(Call("parseCompletion", completionJson))!;
        var calls = (node["calls"]?.AsArray() ?? []).Select(c => new ForgeModelCall(
            c!["id"]!.GetValue<string>(), c["name"]!.GetValue<string>(), c["args"] as JsonObject ?? new JsonObject())).ToList();
        var usage = node["usage"];
        return new ForgeModelReply(node["text"]?.GetValue<string>() ?? "", node["thinking"]?.GetValue<string>(), calls,
            usage?["neurons"]?.GetValue<double>(), usage?["input"]?.GetValue<int>(), usage?["output"]?.GetValue<int>());
    }

    public Result<string> Shader(string docJson)
    {
        var node = JsonNode.Parse(Call("shader", docJson))!;
        var text = node["text"]!.GetValue<string>();
        return node["ok"]!.GetValue<bool>() ? (text.Length > 40_000 ? text[..40_000] : text) : Error.Unprocessable("compile_failed", text,
            [new FieldViolation("document", "compile_failed", text)]);
    }

    public string LinkText(string docJson) => Call("linkText", docJson);

    public Result<ForgeExportPlan> ExportPlan(string docJson)
    {
        try
        {
            var node = JsonNode.Parse(Call("exportPlan", docJson))!;
            var files = node["files"]!.AsArray().Select(f => new ForgeExportFile(f!["path"]!.GetValue<string>(), f["text"]?.GetValue<string>(),
                f["asset"]?.GetValue<string>(), f["builtinIr"]?.GetValue<string>(), f["forge"]?.GetValue<bool>() ?? false)).ToList();
            return new ForgeExportPlan(node["base"]!.AsObject().DeepClone().AsObject(), files);
        }
        catch (ForgeRuntimeException e) when (!e.HitLimit)
        {
            return Error.Unprocessable("export_failed", e.Message, [new FieldViolation("document", "export_failed", e.Message)]);
        }
    }

    private static ForgeToolOutcome Outcome(string json)
    {
        var node = JsonNode.Parse(json)!;
        return new ForgeToolOutcome(node["doc"]?.GetValue<string>(), node["result"]?.GetValue<string>() ?? "", node["error"]?.GetValue<bool>() ?? false,
            (node["created"]?.AsArray() ?? []).Select(c => c!.GetValue<string>()).ToList(), node["pending"]?.DeepClone());
    }

    private string Call(string function, params string?[] args)
    {
        Instance? instance = null;
        var healthy = false;
        try
        {
            instance = Rent();
            var result = instance.Invoke(function, args);
            healthy = true;
            return result;
        }
        catch (JavaScriptException e)
        {
            healthy = true;
            throw new ForgeRuntimeException(e.Message);
        }
        catch (Exception e) when (e is TimeoutException or MemoryLimitExceededException or RecursionDepthOverflowException or StatementsCountOverflowException)
        {
            _logger.LogWarning("forge-core call {Function} stopped: {Message}", function, e.Message);
            throw new ForgeRuntimeException("The operation took too much time or memory.", limit: true);
        }
        finally
        {
            if (healthy && instance is not null && _pool.Count < _options.PoolSize) _pool.Add(instance);
        }
    }

    private Instance Rent() => _pool.TryTake(out var instance) ? instance : new Instance(_script, _options);

    private static string ReadBundle()
    {
        var assembly = typeof(ForgeRuntime).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("forge-core.js", StringComparison.Ordinal));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        return reader.ReadToEnd();
    }

    private sealed class Instance
    {
        private readonly Engine _engine;
        private readonly Dictionary<string, JsValue> _functions = [];
        private readonly JsValue _api;

        public Instance(Prepared<Acornima.Ast.Script> script, ForgeRuntimeOptions options)
        {
            _engine = new Engine(o => o
                .LimitMemory(options.MemoryLimitMb * 1024L * 1024L)
                .TimeoutInterval(TimeSpan.FromMilliseconds(options.TimeoutMs))
                .LimitRecursion(options.MaxRecursion)
                .Strict());
            _engine.Execute(script);
            _api = _engine.Evaluate("ForgeServer");
            foreach (var name in Functions) _functions[name] = _api.AsObject().Get(name);
        }

        public string Invoke(string function, string?[] args)
        {
            var values = args.Select(a => a is null ? JsValue.Null : new JsString(a)).ToArray<JsValue>();
            return _engine.Invoke(_functions[function], _api, values).AsString();
        }
    }
}
