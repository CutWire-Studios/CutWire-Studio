using System.Diagnostics;
using System.Text.Json.Nodes;
using CutWire.Studio.Application.Forge;
using CutWire.Studio.Infrastructure.Catalog;
using CutWire.Studio.Infrastructure.Forge;
using Jint;
using CutWire.Studio.Application.Catalog;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace CutWire.Studio.UnitTests;

public class ForgeRuntimeTests
{
    private const string SeedPrelude =
        "var __a = 12345; Math.random = function () { __a = (__a + 0x6d2b79f5) | 0; var t = Math.imul(__a ^ (__a >>> 15), 1 | __a);" +
        " t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };";

    private static ForgeRuntime NewRuntime(ForgeRuntimeOptions? options = null) =>
        new(Microsoft.Extensions.Options.Options.Create(options ?? new ForgeRuntimeOptions()), NullLogger<ForgeRuntime>.Instance);

    private static string Script()
    {
        var assembly = typeof(ForgeRuntime).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("forge-core.js", StringComparison.Ordinal));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        return reader.ReadToEnd();
    }

    private static JsonNode Fixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "forge-parity.json");
        return JsonNode.Parse(File.ReadAllText(path))!;
    }

    [Fact]
    public void ToolCalls_MatchNodeOutput()
    {
        var script = Engine.PrepareScript(Script(), "forge-core.js");
        var fixture = Fixture();
        foreach (var scenario in new[] { "effect", "wired", "audio" })
        {
            var node = fixture[scenario]!;
            var engine = new Engine().Execute(script);
            engine.Execute(SeedPrelude);
            var api = engine.Evaluate("ForgeServer");
            string Call(string fn, params string[] args) =>
                engine.Invoke(api.AsObject().Get(fn), api, args.Select(a => (Jint.Native.JsValue)a).ToArray()).AsString();

            var doc = node["input"]!.GetValue<string>();
            foreach (var step in node["steps"]!.AsArray())
            {
                var output = Call("applyTool", doc, step!["tool"]!.GetValue<string>(), step["args"]!.ToJsonString());
                JsonNode.DeepEquals(JsonNode.Parse(output), step["output"]).ShouldBeTrue($"{scenario}/{step["tool"]}: {output}");
                if (JsonNode.Parse(output)!["doc"]?.GetValue<string>() is { } next) doc = next;
            }

            Call("view", doc).ShouldBe(node["view"]!.GetValue<string>());
            if (node["shader"] is { } shader) Call("shader", doc).ShouldBe(shader.GetValue<string>());
            if (node["exportPlan"] is { } plan) Call("exportPlan", doc).ShouldBe(plan.GetValue<string>());
        }
    }

    [Fact]
    public void Runtime_ExposesToolsAndAppliesThem()
    {
        var runtime = NewRuntime();
        runtime.Tools.Select(t => t.Name).ShouldContain("add_block");
        runtime.Tools.Select(t => t.Name).ShouldContain("add_pedal");
        runtime.Starters.ShouldNotBeEmpty();

        var doc = runtime.NewDocument("effect", "Test fx", null);
        var outcome = runtime.Apply(doc, "add_block", """{"type":"blur"}""");
        outcome.IsError.ShouldBeFalse();
        outcome.DocJson.ShouldNotBeNull();
        outcome.Created.Count.ShouldBe(1);
        runtime.Apply(outcome.DocJson!, "add_block", """{"type":"nope"}""").IsError.ShouldBeTrue();
        runtime.Shader(outcome.DocJson!).IsSuccess.ShouldBeTrue();
        runtime.ParseDoc("{}").IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Runtime_StopsRunawayCalls()
    {
        var runtime = NewRuntime(new ForgeRuntimeOptions { MaxRecursion = 5 });
        Should.Throw<ForgeRuntimeException>(() => runtime.NewDocument("effect", "Test fx", null)).HitLimit.ShouldBeTrue();
    }

    [Fact]
    public void Runtime_Timings()
    {
        var watch = Stopwatch.StartNew();
        var runtime = NewRuntime();
        var parse = watch.Elapsed;
        var warm = runtime.WarmUp();
        var doc = runtime.NewDocument("effect", "Timing", null);
        watch.Restart();
        for (var i = 0; i < 50; i++) runtime.Apply(doc, "add_block", """{"type":"blur"}""");
        var perCall = watch.Elapsed / 50;
        Console.WriteLine($"forge-core: prepare {parse.TotalMilliseconds:0} ms, first engine {warm.TotalMilliseconds:0} ms, tool call {perCall.TotalMilliseconds:0.0} ms");
        perCall.ShouldBeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ExportedDriftfx_IsAcceptedByTheCatalog()
    {
        var runtime = NewRuntime();
        var packager = new ForgePackager(runtime, new ForgeAssets(null!, null!));
        foreach (var (kind, assetKind) in new (string, IAssetKind)[] { ("effect", new EffectKind()), ("transition", new TransitionKind()), ("audio", new AudioEffectKind()) })
        {
            var doc = runtime.NewDocument(kind, "Export test", null);
            var package = await packager.ExportDriftfxAsync(doc, TestContext.Current.CancellationToken);
            package.IsSuccess.ShouldBeTrue(package.IsFailure ? package.Error.Message : "");
            var info = await Kinds.Validate(assetKind, package.Value.Bytes);
            info.IsSuccess.ShouldBeTrue($"{kind}: " + (info.IsFailure ? info.Error.Message : ""));
        }
    }

    [Fact]
    public async Task ShareFragment_RoundTrips()
    {
        var runtime = NewRuntime();
        var packager = new ForgePackager(runtime, new ForgeAssets(null!, null!));
        var doc = runtime.NewDocument("effect", "Link test", null);
        var fragment = await packager.ShareFragmentAsync(doc, TestContext.Current.CancellationToken);
        fragment.IsSuccess.ShouldBeTrue();
        fragment.Value.ShouldStartWith("v1.");
        var back = packager.DocFromFragment(fragment.Value);
        back.IsSuccess.ShouldBeTrue();
        JsonNode.Parse(back.Value)!["meta"]!["displayName"]!.GetValue<string>().ShouldBe("Link test");
    }
}
