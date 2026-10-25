using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using CutWire.Studio.Application.Forge;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Infrastructure.Catalog;
using ZstdSharp;

namespace CutWire.Studio.Infrastructure.Forge;

/// <summary>
/// The parts of export and import forge-core can't do inside Jint: sha256, zstd, the .driftfx container framing and blob access.
/// Output is byte-compatible with forge-core's <c>writeDriftfx</c> (same canonical manifest, same layout).
/// </summary>
public sealed class ForgePackager(IForgeRuntime runtime, ForgeAssets assets) : IForgePackager
{
    private const int ZstdLevel = 19;
    private const long MaxLinkJson = 8 * 1024 * 1024;
    private const long MaxImportUnpacked = 256L * 1024 * 1024;
    private static readonly byte[] Magic = "DRIFTFX\0"u8.ToArray();
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task<Result<ExportedPackage>> ExportDriftfxAsync(string docJson, CancellationToken ct)
    {
        var plan = runtime.ExportPlan(docJson);
        if (plan.IsFailure) return plan.Error;

        var inflated = await assets.InflateAsync(docJson, ct);
        if (inflated.IsFailure) return inflated.Error;
        var doc = JsonNode.Parse(inflated.Value)!;
        var assetData = (doc["assets"]?.AsArray() ?? []).OfType<JsonObject>()
            .Where(a => a["id"] is not null && a["data"] is not null)
            .ToDictionary(a => a["id"]!.GetValue<string>(), a => Convert.FromBase64String(a["data"]!.GetValue<string>()));

        var files = new List<(string Path, byte[] Data)>();
        foreach (var file in plan.Value.Files)
        {
            byte[] data;
            if (file.IsForgeJson) data = Encoding.UTF8.GetBytes(doc.ToJsonString(Pretty));
            else if (file.Text is not null) data = Encoding.UTF8.GetBytes(file.Text);
            else if (file.BuiltinIr is not null)
            {
                var ir = BuiltinImpulseResponse(file.BuiltinIr);
                if (ir is null) return Error.Unprocessable("export_failed", $"Unknown built-in impulse response '{file.BuiltinIr}'.");
                data = ir;
            }
            else if (file.Asset is not null && assetData.TryGetValue(file.Asset, out var bytes)) data = bytes;
            else return Error.Unprocessable("export_failed", $"The file {file.Path} needs an asset that isn't in the document.");
            files.Add((file.Path, data));
        }

        var packed = Pack(plan.Value.Manifest, files);
        var id = doc["meta"]?["id"]?.GetValue<string>() ?? "effect";
        return new ExportedPackage(packed, id + ".driftfx");
    }

    internal static byte[] Pack(JsonObject baseManifest, List<(string Path, byte[] Data)> files)
    {
        files.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        var table = new JsonArray();
        using var raw = new MemoryStream();
        foreach (var (path, data) in files)
        {
            table.Add(new JsonObject
            {
                ["path"] = path,
                ["offset"] = raw.Length,
                ["size"] = data.Length,
                ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(data)),
            });
            raw.Write(data);
        }

        var manifest = (JsonObject)baseManifest.DeepClone();
        manifest["schema"] = 1;
        manifest["platform"] = "";
        manifest["installedSize"] = raw.Length;
        manifest["files"] = table;

        using var element = JsonDocument.Parse(manifest.ToJsonString());
        var meta = Encoding.UTF8.GetBytes(CanonicalJson.Write(element.RootElement)!);
        var payload = Compress(raw.ToArray());

        var bodyLength = 8 + 4 + 4 + meta.Length + 8 + 8 + payload.Length;
        var output = new byte[bodyLength + 32];
        Magic.CopyTo(output, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(12), (uint)meta.Length);
        meta.CopyTo(output, 16);
        var p = 16 + meta.Length;
        BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(p), (ulong)payload.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(p + 8), (ulong)raw.Length);
        payload.CopyTo(output, p + 16);
        SHA256.HashData(output.AsSpan(0, bodyLength)).CopyTo(output, bodyLength);
        return output;
    }

    private static byte[] Compress(byte[] data)
    {
        using var compressor = new Compressor(ZstdLevel);
        return compressor.Wrap(data).ToArray();
    }

    private static byte[]? BuiltinImpulseResponse(string name)
    {
        if (name.Length == 0 || !name.All(char.IsAsciiLetterLower)) return null;
        var assembly = typeof(ForgePackager).Assembly;
        var resource = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith($".ir.{name}.wav", StringComparison.Ordinal));
        if (resource is null) return null;
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public async Task<Result<string>> ShareFragmentAsync(string docJson, CancellationToken ct)
    {
        var inflated = await assets.InflateAsync(docJson, ct);
        if (inflated.IsFailure) return inflated.Error;
        var json = Encoding.UTF8.GetBytes(runtime.LinkText(inflated.Value));
        return runtime.LinkPrefix + Base64Url(Compress(json));
    }

    public Result<string> DocFromFragment(string fragment)
    {
        if (!fragment.StartsWith(runtime.LinkPrefix, StringComparison.Ordinal))
            return Error.Validation("link_invalid", "This link isn't a Drift Forge share link.");
        byte[]? json;
        try
        {
            json = Decompress(FromBase64Url(fragment[runtime.LinkPrefix.Length..]), MaxLinkJson);
        }
        catch (FormatException)
        {
            json = null;
        }

        return json is null
            ? Error.Validation("link_invalid", "This link is damaged or incomplete. It may have been cut off when it was shared.")
            : runtime.ParseDoc(Encoding.UTF8.GetString(json));
    }

    public async Task<Result<string>> DocFromFileAsync(string fileName, byte[] bytes, CancellationToken ct)
    {
        await Task.CompletedTask;
        var name = fileName.ToLowerInvariant();
        if (name.EndsWith(".driftfx", StringComparison.Ordinal))
        {
            var read = DriftfxReader.Read(bytes, MaxImportUnpacked);
            if (read.IsFailure) return read.Error;
            var forge = read.Value.Files.FirstOrDefault(f => f.Key.EndsWith("/forge.json", StringComparison.Ordinal));
            return forge.Value is null
                ? Error.Unprocessable("not_forge_package", "This package wasn't made with Drift Forge, so it can't be edited here.")
                : runtime.ParseDoc(Encoding.UTF8.GetString(forge.Value));
        }

        if (name.EndsWith(".zip", StringComparison.Ordinal))
        {
            try
            {
                using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
                var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("forge.json", StringComparison.Ordinal));
                if (entry is null) return Error.Unprocessable("not_forge_package", "This zip has no forge.json, so it wasn't made with Drift Forge.");
                if (entry.Length > MaxImportUnpacked) return Error.TooLarge("package_too_large", "forge.json is too large.");
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                return runtime.ParseDoc(await reader.ReadToEndAsync(ct));
            }
            catch (InvalidDataException)
            {
                return Error.Validation("zip_invalid", "That isn't a valid zip file.");
            }
        }

        if (name.EndsWith(".json", StringComparison.Ordinal)) return runtime.ParseDoc(Encoding.UTF8.GetString(bytes));
        return Error.Validation("file_unsupported", "Open a .driftfx, a .zip exported from Drift Forge, or a forge.json file.");
    }

    private static byte[]? Decompress(byte[] data, long max)
    {
        try
        {
            using var input = new MemoryStream(data);
            using var zstd = new DecompressionStream(input);
            using var output = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = zstd.Read(chunk, 0, chunk.Length)) > 0)
            {
                output.Write(chunk, 0, read);
                if (output.Length > max) return null;
            }

            return output.ToArray();
        }
        catch (Exception e) when (e is ZstdException or InvalidDataException or IOException or ArgumentException)
        {
            return null;
        }
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static byte[] FromBase64Url(string text)
    {
        var b64 = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(b64 + new string('=', (4 - b64.Length % 4) % 4));
    }
}
