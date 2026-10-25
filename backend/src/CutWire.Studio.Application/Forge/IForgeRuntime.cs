using System.Text.Json.Nodes;
using CutWire.Studio.Domain.Common;

namespace CutWire.Studio.Application.Forge;

public sealed record ForgeToolInfo(string Name, string Description, JsonNode Parameters, IReadOnlyList<string> Kinds, bool Shared);

public sealed record ForgeStarter(string Name, string Kind, string Description);

/// <summary>The result of one tool call. <see cref="DocJson"/> is set only when the call changed the document.</summary>
public sealed record ForgeToolOutcome(string? DocJson, string Result, bool IsError, IReadOnlyList<string> Created, JsonNode? Pending);

public sealed record ForgeAgentSetup(string System, JsonArray Tools, string First);

public sealed record ForgeModelCall(string Id, string Name, JsonObject Args);

public sealed record ForgeModelReply(string Text, string? Thinking, IReadOnlyList<ForgeModelCall> Calls, double? Neurons, int? InputTokens, int? OutputTokens);

public sealed record ForgeExportFile(string Path, string? Text, string? Asset, string? BuiltinIr, bool IsForgeJson);

public sealed record ForgeExportPlan(JsonObject Manifest, IReadOnlyList<ForgeExportFile> Files);

/// <summary>forge-core, the same code the browser runs, hosted in-process. Calls are synchronous, CPU-bound and bounded.</summary>
public interface IForgeRuntime
{
    IReadOnlyList<ForgeToolInfo> Tools { get; }
    IReadOnlyList<ForgeStarter> Starters { get; }
    int LinkLimit { get; }
    string LinkPrefix { get; }

    string NewDocument(string kind, string? name, string? starter);
    Result<string> ParseDoc(string text);
    string View(string docJson);
    ForgeToolOutcome Apply(string docJson, string tool, string argsJson);
    ForgeToolOutcome AgentCall(string docJson, string tool, string argsJson);
    ForgeAgentSetup AgentSetup(string docJson, string prompt);
    string Place(string docJson, IReadOnlyCollection<string> created);
    ForgeModelReply ParseCompletion(string completionJson);
    Result<string> Shader(string docJson);
    string LinkText(string docJson);
    Result<ForgeExportPlan> ExportPlan(string docJson);
}

public sealed record ExportedPackage(byte[] Bytes, string FileName);

/// <summary>Everything that needs compression, hashing or blobs around forge-core's output.</summary>
public interface IForgePackager
{
    Task<Result<ExportedPackage>> ExportDriftfxAsync(string docJson, CancellationToken ct);
    Task<Result<string>> ShareFragmentAsync(string docJson, CancellationToken ct);
    Result<string> DocFromFragment(string fragment);
    Task<Result<string>> DocFromFileAsync(string fileName, byte[] bytes, CancellationToken ct);
}
