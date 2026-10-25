using System.Text.Json;
using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Domain.Forge;

namespace CutWire.Studio.Application.Forge;

public sealed record ForgeDocInfo(string Kind, string Name, IReadOnlySet<Sha256Hash> Blobs);

/// <summary>The checks the server makes on a stored ForgeDoc: shape, size and the asset rule (blobs, not inline data).</summary>
public static class ForgeDocJson
{
    public static readonly string[] Kinds = ["effect", "transition", "audio"];

    public static Result<ForgeDocInfo> Inspect(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > ForgeDocument.MaxDocBytes)
            return Error.TooLarge("document_too_large", $"A document can be at most {ForgeDocument.MaxDocBytes / 1024 / 1024} MB.");

        JsonDocument parsed;
        try
        {
            parsed = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        }
        catch (JsonException)
        {
            return Invalid("document_invalid", "The document is not valid JSON.");
        }

        using (parsed)
        {
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("forge", out var schema) || schema.ValueKind != JsonValueKind.Number)
                return Invalid("document_invalid", "This isn't a Drift Forge document.");
            if (!root.TryGetProperty("kind", out var kindElement) || kindElement.ValueKind != JsonValueKind.String
                || !Kinds.Contains(kindElement.GetString()))
                return Invalid("document_invalid", "Unknown document kind.");
            foreach (var array in new[] { "nodes", "edges", "params" })
                if (!root.TryGetProperty(array, out var value) || value.ValueKind != JsonValueKind.Array)
                    return Invalid("document_invalid", "The document is incomplete.");
            if (!root.TryGetProperty("meta", out var meta) || meta.ValueKind != JsonValueKind.Object)
                return Invalid("document_invalid", "The document is incomplete.");

            var name = meta.TryGetProperty("displayName", out var display) && display.ValueKind == JsonValueKind.String
                ? display.GetString()!.Trim()
                : "";
            if (name.Length == 0) name = "Untitled";
            if (name.Length > ForgeDocument.MaxNameLength) name = name[..ForgeDocument.MaxNameLength];

            var blobs = new HashSet<Sha256Hash>();
            if (root.TryGetProperty("assets", out var assets))
            {
                if (assets.ValueKind != JsonValueKind.Array) return Invalid("document_invalid", "The document's assets must be a list.");
                var index = 0;
                foreach (var asset in assets.EnumerateArray())
                {
                    var field = $"assets[{index++}]";
                    if (asset.ValueKind != JsonValueKind.Object || !asset.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String)
                        return Invalid("document_invalid", $"{field} is malformed.");
                    var hasBlob = false;
                    if (asset.TryGetProperty("blob", out var blob))
                    {
                        if (blob.ValueKind != JsonValueKind.String || !Sha256Hash.TryParse(blob.GetString(), out var hash))
                            return Invalid("asset_blob_invalid", $"{field}.blob must be a sha256 hex digest.");
                        blobs.Add(hash);
                        hasBlob = true;
                    }

                    if (asset.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.String)
                    {
                        if (data.GetString()!.Length > ForgeDocument.MaxInlineAssetBytes)
                            return Invalid("inline_asset_too_large", $"{field} has inline data over {ForgeDocument.MaxInlineAssetBytes} bytes: upload it to /api/v1/forge/assets and reference it by blob.");
                    }
                    else if (!hasBlob)
                        return Invalid("asset_blob_invalid", $"{field} needs a blob or inline data.");
                }
            }

            return new ForgeDocInfo(kindElement.GetString()!, name, blobs);
        }
    }

    private static Error Invalid(string code, string message) => Error.Validation(code, message);
}
