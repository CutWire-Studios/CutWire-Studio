using System.Text.Json;
using System.Text.Json.Nodes;
using CutWire.Studio.Api.Auth;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Catalog;
using CutWire.Studio.Application.Forge;
using CutWire.Studio.Application.Quality;
using CutWire.Studio.Domain.Accounts;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Infrastructure.Forge;
using CutWire.Studio.Infrastructure.Identity;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CutWire.Studio.Api.Mcp;

/// <summary>
/// The Forge MCP server. Document tools work on the signed-in user's cloud library, so every edit lands in a real document
/// (revision bump, live update to the open editor). The build tools are generated from forge-core's own tool definitions.
/// </summary>
public sealed class ForgeMcpTools(IForgeRuntime runtime)
{
    public const string Instructions =
        "Drift Forge builds video effects, transitions and audio effects for the Drift video editor. Every document here lives in the user's " +
        "CutWire cloud library: call list_documents to see them, open_document to read one, or create_document to start a new one (optionally " +
        "from a starter). Tools that change a document save it immediately, and if the user has that document open in Forge in their browser " +
        "the changes appear there live, as they happen, so say what you are building and keep edits purposeful. Always run check before you " +
        "finish. get_share_link gives a link that opens the result in Forge, export_driftfx returns the installable package, and " +
        "publish_draft prepares a Community upload that the user finishes themselves (media and metadata). Nothing here deletes documents.";

    private static readonly HashSet<string> Native =
    [
        "list_documents", "open_document", "create_document", "get_shader_code", "get_share_link", "open_link", "import_package", "export_driftfx",
        "publish_draft",
    ];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<Tool> ListTools()
    {
        var tools = new List<Tool>
        {
            Define("list_documents", "List the documents in the user's cloud library: id, kind, name and last change.",
                Schema(new JsonObject { ["kind"] = Prop("string", "Only this kind", ["effect", "transition", "audio"]) })),
            Define("open_document", "Open a document: its summary and the current graph (blocks and wires, or the pedalboard).", WithDocId(Schema())),
            Define("create_document",
                "Start a new Drift video effect, transition or audio effect in the user's library, optionally from a starter. Returns its doc_id. Effects start from the Video block; transitions from From/To/Progress; audio effects are a pedalboard built with the audio tools.",
                Schema(new JsonObject
                {
                    ["kind"] = Prop("string", "The kind of document", ["effect", "transition", "audio"]),
                    ["name"] = Prop("string", "Display name"),
                    ["starter"] = Prop("string", $"One of: {string.Join(", ", runtime.Starters.Select(s => s.Name))}"),
                }, "kind")),
            Define("get_shader_code",
                "Show what Drift will run for this document: effect.json/transition.json and its GLSL, or an audio effect's audio-effect.json.", WithDocId(Schema())),
            Define("get_share_link", "Get a link that opens this document in Drift Forge, where it can be previewed, tweaked and exported for Drift.", WithDocId(Schema())),
            Define("open_link", "Import a Drift Forge share link (https://…/forge/#v1.…) into the library as a new document. Returns its doc_id.",
                Schema(new JsonObject { ["url"] = Prop("string", "The share link") }, "url")),
            Define("import_package",
                "Import a .driftfx, a Forge-exported .zip or a forge.json into the library as a new document. Returns its doc_id.",
                Schema(new JsonObject
                {
                    ["base64"] = Prop("string", "The file, base64 encoded"),
                    ["filename"] = Prop("string", "File name with extension (.driftfx, .zip or .json)"),
                }, "base64", "filename")),
            Define("export_driftfx", "Build the installable .driftfx package for this document. Returns its file name and the bytes as base64.", WithDocId(Schema())),
            Define("publish_draft",
                "Create a Community draft from this document with its .driftfx package attached. The user opens the returned link to add media and finish the details; nothing is published.",
                WithDocId(Schema(new JsonObject
                {
                    ["title"] = Prop("string", "Community title: ASCII, at least two words"),
                    ["summary"] = Prop("string", "One-line summary, 20-140 characters"),
                    ["description"] = Prop("string", "Longer description, at least 80 characters"),
                }))),
        };

        foreach (var tool in runtime.Tools)
        {
            var prefix = tool.Shared ? "" : tool.Kinds[0] == "audio" ? "(Audio effects.) " : "(Video effects and transitions.) ";
            var parameters = tool.Parameters.DeepClone().AsObject();
            tools.Add(Define(tool.Name, prefix + tool.Description, WithDocId(parameters)));
        }

        return tools;
    }

    public async Task<CallToolResult> CallAsync(string name, JsonObject args, Guid userId, HashSet<string> scopes, IServiceProvider services, CancellationToken ct)
    {
        try
        {
            return name switch
            {
                "list_documents" => await ListDocuments(args, userId, services, ct),
                "open_document" => await OpenDocument(args, userId, services, ct),
                "create_document" => await CreateDocument(args, userId, scopes, services, ct),
                "get_shader_code" => await WithDoc(args, userId, services, ct, (doc, _) =>
                {
                    var shader = runtime.Shader(doc.DocJson);
                    return Task.FromResult(shader.IsSuccess ? Text(shader.Value) : Fail(shader.Error.Message));
                }),
                "get_share_link" => await ShareLink(args, userId, services, ct),
                "open_link" => await OpenLink(args, userId, scopes, services, ct),
                "import_package" => await ImportPackage(args, userId, scopes, services, ct),
                "export_driftfx" => await Export(args, userId, services, ct),
                "publish_draft" => await Publish(args, userId, scopes, services, ct),
                _ when runtime.Tools.Any(t => t.Name == name) => await Build(name, args, userId, scopes, services, ct),
                _ => Fail($"Unknown tool \"{name}\"."),
            };
        }
        catch (ForgeRuntimeException e)
        {
            return Fail(e.Message);
        }
    }

    private async Task<CallToolResult> ListDocuments(JsonObject args, Guid userId, IServiceProvider sp, CancellationToken ct)
    {
        var kind = args["kind"]?.GetValue<string>();
        var docs = (await sp.GetRequiredService<ForgeDocuments>().ListAsync(userId, ct)).Where(d => kind is null || d.Kind == kind).ToList();
        if (docs.Count == 0) return Text("No documents yet. Start with create_document.");
        return Text(string.Join("\n", docs.Select(d => $"{d.Id} | {d.Kind} | {d.Name} | rev {d.Revision} | {d.UpdatedAt:u}")));
    }

    private async Task<CallToolResult> OpenDocument(JsonObject args, Guid userId, IServiceProvider sp, CancellationToken ct) =>
        await WithDoc(args, userId, sp, ct, (doc, _) => Task.FromResult(Text($"doc_id: {doc.Id}\nrevision: {doc.Revision}\n{runtime.View(doc.DocJson)}")));

    private async Task<CallToolResult> CreateDocument(JsonObject args, Guid userId, HashSet<string> scopes, IServiceProvider sp, CancellationToken ct)
    {
        if (!scopes.Contains(AuthScopes.ForgeWrite)) return Scope(AuthScopes.ForgeWrite);
        var kind = args["kind"]?.GetValue<string>();
        if (kind is null || !ForgeDocJson.Kinds.Contains(kind)) return Fail("kind must be effect, transition or audio.");
        var json = runtime.NewDocument(kind, args["name"]?.GetValue<string>(), args["starter"]?.GetValue<string>());
        return await Store(json, userId, sp, ct);
    }

    private async Task<CallToolResult> Store(string docJson, Guid userId, IServiceProvider sp, CancellationToken ct)
    {
        var externalized = await sp.GetRequiredService<ForgeAssets>().ExternalizeAsync(docJson, ct);
        if (externalized.IsFailure) return Fail(externalized.Error.Message);
        var created = await sp.GetRequiredService<ForgeDocuments>().CreateAsync(userId, Guid.NewGuid().ToString(), externalized.Value, ct);
        if (created.IsFailure) return Fail(created.Error.Message);
        return Text($"doc_id: {created.Value.Id}\n{runtime.View(externalized.Value)}");
    }

    private async Task<CallToolResult> ShareLink(JsonObject args, Guid userId, IServiceProvider sp, CancellationToken ct) =>
        await WithDoc(args, userId, sp, ct, async (doc, services) =>
        {
            var fragment = await services.GetRequiredService<IForgePackager>().ShareFragmentAsync(doc.DocJson, ct);
            if (fragment.IsFailure) return Fail(fragment.Error.Message);
            var url = $"{services.GetRequiredService<PublicUrl>().Absolute("/forge/")}#{fragment.Value}";
            return url.Length > runtime.LinkLimit
                ? Fail($"The link would be {url.Length} characters, over the {runtime.LinkLimit} limit. Use export_driftfx instead.")
                : Text(url);
        });

    private async Task<CallToolResult> OpenLink(JsonObject args, Guid userId, HashSet<string> scopes, IServiceProvider sp, CancellationToken ct)
    {
        if (!scopes.Contains(AuthScopes.ForgeWrite)) return Scope(AuthScopes.ForgeWrite);
        var url = args["url"]?.GetValue<string>() ?? "";
        var hash = url.IndexOf('#');
        var parsed = sp.GetRequiredService<IForgePackager>().DocFromFragment(hash >= 0 ? url[(hash + 1)..] : url);
        return parsed.IsFailure ? Fail(parsed.Error.Message) : await Store(parsed.Value, userId, sp, ct);
    }

    private async Task<CallToolResult> ImportPackage(JsonObject args, Guid userId, HashSet<string> scopes, IServiceProvider sp, CancellationToken ct)
    {
        if (!scopes.Contains(AuthScopes.ForgeWrite)) return Scope(AuthScopes.ForgeWrite);
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(args["base64"]?.GetValue<string>() ?? "");
        }
        catch (FormatException)
        {
            return Fail("base64 is not valid base64.");
        }

        var parsed = await sp.GetRequiredService<IForgePackager>().DocFromFileAsync(args["filename"]?.GetValue<string>() ?? "", bytes, ct);
        return parsed.IsFailure ? Fail(parsed.Error.Message) : await Store(parsed.Value, userId, sp, ct);
    }

    private async Task<CallToolResult> Export(JsonObject args, Guid userId, IServiceProvider sp, CancellationToken ct) =>
        await WithDoc(args, userId, sp, ct, async (doc, services) =>
        {
            var package = await services.GetRequiredService<IForgePackager>().ExportDriftfxAsync(doc.DocJson, ct);
            if (package.IsFailure) return Fail(package.Error.Message);
            return Text(JsonSerializer.Serialize(new { filename = package.Value.FileName, size = package.Value.Bytes.Length, base64 = Convert.ToBase64String(package.Value.Bytes) }, Json));
        });

    private async Task<CallToolResult> Publish(JsonObject args, Guid userId, HashSet<string> scopes, IServiceProvider sp, CancellationToken ct)
    {
        if (!scopes.Contains(AuthScopes.CommunityPublish)) return Scope(AuthScopes.CommunityPublish);
        return await WithDoc(args, userId, sp, ct, async (doc, services) =>
        {
            var package = await services.GetRequiredService<IForgePackager>().ExportDriftfxAsync(doc.DocJson, ct);
            if (package.IsFailure) return Fail(package.Error.Message);

            var kindId = doc.Kind switch { "effect" => AssetKindId.Effect, "transition" => AssetKindId.Transition, _ => AssetKindId.AudioEffect };
            if (services.GetRequiredService<IAssetKindRegistry>().Get(kindId) is not { Enabled: true } kind) return Fail("This kind of document can't be published yet.");
            var rules = services.GetRequiredService<ContentRulesOptions>();
            var meta = JsonNode.Parse(doc.DocJson)!["meta"]!;
            var category = meta["category"]?.GetValue<string>() is { } c && kind.Categories.Contains(c) ? c : kind.Categories[0];
            var title = args["title"]?.GetValue<string>() ?? doc.Name;
            var blurb = $"A {kind.DisplayName.ToLowerInvariant()} made with Drift Forge.";
            var summary = args["summary"]?.GetValue<string>() ?? Pad(meta["description"]?.GetValue<string>(), blurb, rules.SummaryMinLength);
            var description = args["description"]?.GetValue<string>()
                ?? Pad(meta["description"]?.GetValue<string>(), $"{blurb} Open it in Drift to try it on your own clips, and tweak its sliders to get the look you want.", rules.DescriptionMinLength);
            var tags = new[] { PickTag(kind, title + " " + summary) };

            var create = services.GetRequiredService<ICommandHandler<CreateItemCommand, ItemDetailDto>>();
            var created = await create.Handle(new CreateItemCommand(userId, kindId.Value, title, summary, description, category, tags, rules.Licenses[0]), ct);
            if (created.IsFailure) return Fail(Describe("Could not create the draft", created.Error));

            var upload = await services.GetRequiredService<ICommandHandler<UploadPackageCommand, ItemDetailDto>>()
                .Handle(new UploadPackageCommand(created.Value.Id, userId, new MemoryStream(package.Value.Bytes)), ct);
            if (upload.IsFailure)
            {
                await services.GetRequiredService<ICommandHandler<DeleteItemCommand, Guid>>().Handle(new DeleteItemCommand(created.Value.Id, userId), ct);
                return Fail(Describe("The package was rejected", upload.Error));
            }

            var link = services.GetRequiredService<PublicUrl>().Absolute($"/community/upload?item={created.Value.Id}");
            return Text($"Draft created (not published). The user finishes the thumbnail, details and license here: {link}\n" +
                        $"Defaults used: category {category}, tag {tags[0]}, license {rules.Licenses[0]}.");
        });
    }

    private static string Pad(string? preferred, string fallback, int minLength)
    {
        var text = string.IsNullOrWhiteSpace(preferred) ? fallback : preferred.Trim();
        text = new string(text.Where(ch => ch is >= ' ' and <= '~').ToArray());
        return text.Length >= minLength ? text : (text + " " + fallback).Trim();
    }

    private static string PickTag(IAssetKind kind, string text)
    {
        var lower = text.ToLowerInvariant();
        return kind.TagVocabulary.FirstOrDefault(t => lower.Contains(t, StringComparison.Ordinal)) ?? kind.TagVocabulary[0];
    }

    private static string Describe(string prefix, Error error) =>
        $"{prefix}: " + (error.Violations is { Count: > 0 } v ? string.Join("; ", v.Select(x => $"{x.Field}: {x.Message}")) : error.Message);

    private async Task<CallToolResult> Build(string name, JsonObject args, Guid userId, HashSet<string> scopes, IServiceProvider sp, CancellationToken ct)
    {
        var docId = DocId(args);
        if (docId is null) return Fail("doc_id is required. Use list_documents or create_document.");
        var rest = (JsonObject)args.DeepClone();
        rest.Remove("doc_id");

        var editor = sp.GetRequiredService<ForgeEditor>();
        var result = await editor.MutateAsync(userId, docId.Value, ForgeSources.Mcp, null, loaded =>
        {
            var outcome = runtime.Apply(loaded.DocJson, name, rest.ToJsonString());
            if (outcome.DocJson is not null && !scopes.Contains(AuthScopes.ForgeWrite)) return Task.FromResult(new Mutation<ForgeToolOutcome?>(null, null));
            return Task.FromResult(new Mutation<ForgeToolOutcome?>(outcome.DocJson, outcome));
        }, ct);
        if (result.IsFailure) return Fail(result.Error.Code == "document_not_found" ? "Unknown doc_id. Use list_documents or create_document." : result.Error.Message);
        return result.Value.Value is { } o ? new CallToolResult { Content = [new TextContentBlock { Text = o.Result }], IsError = o.IsError ? true : null } : Scope(AuthScopes.ForgeWrite);
    }

    private async Task<CallToolResult> WithDoc(JsonObject args, Guid userId, IServiceProvider sp, CancellationToken ct,
        Func<ForgeLoaded, IServiceProvider, Task<CallToolResult>> run)
    {
        var docId = DocId(args);
        if (docId is null) return Fail("doc_id is required. Use list_documents or create_document.");
        var loaded = await sp.GetRequiredService<ForgeDocuments>().LoadAsync(userId, docId.Value, ct);
        return loaded.IsFailure ? Fail("Unknown doc_id. Use list_documents or create_document.") : await run(loaded.Value, sp);
    }

    private static Guid? DocId(JsonObject args) => Guid.TryParse(args["doc_id"]?.GetValue<string>(), out var id) ? id : null;

    private static CallToolResult Text(string text) => new() { Content = [new TextContentBlock { Text = text }] };

    private static CallToolResult Fail(string text) => new() { Content = [new TextContentBlock { Text = text }], IsError = true };

    private static CallToolResult Scope(string scope) => Fail($"This token needs the '{scope}' scope for that. Create a token with it in your account settings.");

    private static Tool Define(string name, string description, JsonObject schema) =>
        new() { Name = name, Description = description, InputSchema = JsonSerializer.SerializeToElement(schema) };

    private static JsonObject Prop(string type, string description, string[]? values = null)
    {
        var p = new JsonObject { ["type"] = type, ["description"] = description };
        if (values is not null) p["enum"] = new JsonArray(values.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray());
        return p;
    }

    private static JsonObject Schema(JsonObject? properties = null, params string[] required) => new()
    {
        ["type"] = "object",
        ["properties"] = properties ?? new JsonObject(),
        ["required"] = new JsonArray(required.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray()),
        ["additionalProperties"] = false,
    };

    private static JsonObject WithDocId(JsonObject schema)
    {
        var properties = new JsonObject { ["doc_id"] = Prop("string", "From list_documents or create_document") };
        foreach (var (key, value) in schema["properties"]?.AsObject() ?? [])
            properties[key] = value?.DeepClone();
        var required = new JsonArray { "doc_id" };
        foreach (var r in schema["required"]?.AsArray() ?? []) required.Add(r!.GetValue<string>());
        schema["properties"] = properties;
        schema["required"] = required;
        return schema;
    }
}
