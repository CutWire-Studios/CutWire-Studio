using System.Text.Json.Nodes;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Blobs;
using CutWire.Studio.Application.Catalog;
using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Domain.Forge;

namespace CutWire.Studio.Application.Forge;

/// <summary>Pictures and recordings a document uses, as content-addressed blobs.</summary>
public sealed class ForgeAssets(ICommandHandler<StoreBlobCommand, StoreBlobResult> storeBlob, IBlobStore blobs)
{
    public const long MaxAssetBytes = 8 * 1024 * 1024;

    /// <summary>Served back from /files with this type, so only passive media: no SVG, HTML or scripts.</summary>
    public static bool IsAllowedMime(string? mime) =>
        mime is not null && mime.Length < 100
        && (mime.StartsWith("image/", StringComparison.Ordinal) || mime.StartsWith("audio/", StringComparison.Ordinal))
        && !mime.Contains("svg", StringComparison.Ordinal) && !mime.Contains("xml", StringComparison.Ordinal)
        && mime.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '.' or '+');

    public async Task<Result<StoreBlobResult>> UploadAsync(Stream content, string? contentType, CancellationToken ct)
    {
        var mime = contentType?.Split(';')[0].Trim().ToLowerInvariant();
        if (!IsAllowedMime(mime))
            return Error.Validation("asset_mime_invalid", "Send the file with an image/* or audio/* Content-Type.");
        var data = await BoundedRead.ReadAsync(content, MaxAssetBytes, ct);
        if (data.Length > MaxAssetBytes) return Error.TooLarge("asset_too_large", "An asset can be at most 8 MB.");
        if (data.Length == 0) return Error.Validation("asset_empty", "The asset is empty.");
        return await storeBlob.Handle(new StoreBlobCommand(new MemoryStream(data), mime!, MaxAssetBytes), ct);
    }

    /// <summary>Moves every inline <c>data</c> over the inline limit into the blob store (for documents the server builds itself).</summary>
    public async Task<Result<string>> ExternalizeAsync(string docJson, CancellationToken ct)
    {
        var doc = JsonNode.Parse(docJson)!;
        if (doc["assets"] is not JsonArray assets) return docJson;
        foreach (var asset in assets.OfType<JsonObject>())
        {
            if (asset["data"]?.GetValue<string>() is not { Length: > ForgeDocument.MaxInlineAssetBytes } data) continue;
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(data);
            }
            catch (FormatException)
            {
                return Error.Validation("asset_invalid", $"Asset {asset["id"]} isn't valid base64.");
            }

            var stored = await UploadAsync(new MemoryStream(bytes), asset["mime"]?.GetValue<string>(), ct);
            if (stored.IsFailure) return stored.Error;
            asset["blob"] = stored.Value.Hash.Value;
            asset.Remove("data");
        }

        return doc.ToJsonString();
    }

    /// <summary>The reverse, for export: every <c>blob</c> becomes base64 <c>data</c> like the browser holds it.</summary>
    public async Task<Result<string>> InflateAsync(string docJson, CancellationToken ct)
    {
        var doc = JsonNode.Parse(docJson)!;
        if (doc["assets"] is not JsonArray assets) return docJson;
        foreach (var asset in assets.OfType<JsonObject>())
        {
            if (asset["blob"]?.GetValue<string>() is not { } blob || !Sha256Hash.TryParse(blob, out var hash)) continue;
            await using var stream = await blobs.OpenAsync(hash, ct: ct);
            if (stream is null) return Error.Unavailable("asset_blob_missing", $"Asset {asset["id"]} is missing from storage.");
            var bytes = await BoundedRead.ReadAsync(stream, MaxAssetBytes * 2, ct);
            asset["data"] = Convert.ToBase64String(bytes);
            asset.Remove("blob");
        }

        return doc.ToJsonString();
    }

    public async Task<byte[]?> ReadAsync(string hex, CancellationToken ct)
    {
        if (!Sha256Hash.TryParse(hex, out var hash)) return null;
        await using var stream = await blobs.OpenAsync(hash, ct: ct);
        return stream is null ? null : await BoundedRead.ReadAsync(stream, MaxAssetBytes * 2, ct);
    }
}

public sealed record Mutation<T>(string? NewDocJson, T Value);

/// <summary>Read-modify-write on one document for the MCP server and the AI: serialised per document, retried on a revision clash.</summary>
public sealed class ForgeEditor(ForgeDocuments docs, KeyedLocks<Guid> locks)
{
    private const int Attempts = 4;

    public async Task<Result<(T Value, ForgeSavedDto? Saved)>> MutateAsync<T>(Guid userId, Guid docId, string source, string? clientId,
        Func<ForgeLoaded, Task<Mutation<T>>> apply, CancellationToken ct)
    {
        using var _ = await locks.AcquireAsync(docId, ct);
        Error? last = null;
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var loaded = await docs.LoadAsync(userId, docId, ct);
            if (loaded.IsFailure) return loaded.Error;
            var mutation = await apply(loaded.Value);
            if (mutation.NewDocJson is null) return (mutation.Value, null);

            var saved = await docs.UpdateAsync(userId, docId, mutation.NewDocJson, loaded.Value.Revision, source, clientId, ct);
            if (saved.IsSuccess) return (mutation.Value, saved.Value);
            if (saved.Error.Code != "revision_conflict") return saved.Error;
            last = saved.Error;
        }

        return last!;
    }
}
