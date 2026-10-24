using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CutWire.Studio.Application.Catalog;
using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Common;
using ZstdSharp;

namespace CutWire.Studio.Infrastructure.Catalog;

internal sealed record DriftfxContents(
    JsonElement Manifest,
    string Root,
    IReadOnlyList<PackageFileEntry> Entries,
    IReadOnlyDictionary<string, byte[]> Files);

internal static class DriftfxReader
{
    private static readonly byte[] Magic = "DRIFTFX\0"u8.ToArray();
    private const int MaxMetadataBytes = 1024 * 1024;
    private const int MaxFiles = 256;

    public static Result<DriftfxContents> Read(byte[] bytes, long maxUnpacked)
    {
        const int fixedHeader = 16;
        const int sizes = 16;
        const int digest = 32;
        if (bytes.Length < fixedHeader + sizes + digest) return Fail("driftfx_too_short", "File is too short to be a .driftfx package.");
        if (!bytes.AsSpan(0, 8).SequenceEqual(Magic)) return Fail("driftfx_bad_magic", "Not a .driftfx package (bad magic).");
        var version = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8));
        if (version != 1) return Fail("driftfx_unsupported_version", $"Unsupported .driftfx format version {version}.");
        var metaLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12));
        if (metaLength == 0 || metaLength > MaxMetadataBytes || fixedHeader + (long)metaLength + sizes + digest > bytes.Length)
            return Fail("driftfx_header_damaged", "Package header is damaged.");

        var p = fixedHeader + (int)metaLength;
        var compressed = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(p));
        var raw = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(p + 8));
        p += sizes;
        if (compressed > (ulong)bytes.Length || (ulong)p + compressed + digest != (ulong)bytes.Length)
            return Fail("driftfx_size_mismatch", "Package size does not match its header.");
        if (raw > (ulong)maxUnpacked)
            return Fail("unpacked_too_large", $"Package unpacks to more than {maxUnpacked} bytes.");

        var end = p + (int)compressed;
        if (!SHA256.HashData(bytes.AsSpan(0, end)).AsSpan().SequenceEqual(bytes.AsSpan(end)))
            return Fail("driftfx_digest_mismatch", "Package is corrupted (checksum mismatch).");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes.AsMemory(fixedHeader, (int)metaLength), new JsonDocumentOptions { MaxDepth = 32 });
        }
        catch (JsonException)
        {
            return Fail("manifest_invalid", "Package manifest is not valid JSON.");
        }

        var manifest = document.RootElement.Clone();
        document.Dispose();
        if (manifest.ValueKind != JsonValueKind.Object) return Fail("manifest_invalid", "Package manifest must be an object.");
        if (!bytes.AsSpan(fixedHeader, (int)metaLength).SequenceEqual(Encoding.UTF8.GetBytes(CanonicalJson.Write(manifest) ?? "")))
            return Fail("manifest_not_canonical", "Package manifest is not canonical JSON (sorted keys, no whitespace).");

        var table = ParseTable(manifest, raw);
        if (table.IsFailure) return table.Error;

        var payload = Decompress(bytes.AsMemory(p, (int)compressed), (long)raw);
        if (payload is null) return Fail("payload_invalid", "Package payload is corrupted or does not match its declared size.");

        var entries = new List<PackageFileEntry>();
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (path, offset, size, sha) in table.Value)
        {
            var data = payload.AsSpan((int)offset, (int)size).ToArray();
            if (!SHA256.HashData(data).AsSpan().SequenceEqual(Convert.FromHexString(sha)))
                return Fail("file_digest_mismatch", $"File {path} is corrupted.");
            entries.Add(new PackageFileEntry(path, size, new Sha256Hash(sha)));
            files[path] = data;
        }

        var provides = manifest.GetProperty("provides")[0];
        return new DriftfxContents(manifest, provides.GetProperty("root").GetString()!, entries, files);
    }

    private static Result<List<(string Path, long Offset, long Size, string Sha)>> ParseTable(JsonElement manifest, ulong raw)
    {
        if (!manifest.TryGetProperty("schema", out var schema) || !schema.TryGetInt32(out var schemaNumber) || schemaNumber != 1)
            return Fail("manifest_invalid", "Unsupported manifest schema.");
        if (!manifest.TryGetProperty("provides", out var provides) || provides.ValueKind != JsonValueKind.Array || provides.GetArrayLength() != 1
            || provides[0].ValueKind != JsonValueKind.Object
            || !Str(provides[0], "kind", out _) || !Str(provides[0], "root", out var root) || !PathRules.IsSafe(root))
            return Fail("manifest_invalid", "Manifest must provide exactly one item with a safe root.");
        if (!manifest.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array || files.GetArrayLength() == 0 || files.GetArrayLength() > MaxFiles)
            return Fail("manifest_invalid", $"Manifest must list between 1 and {MaxFiles} files.");

        var table = new List<(string, long, long, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        long expected = 0;
        foreach (var file in files.EnumerateArray())
        {
            if (file.ValueKind != JsonValueKind.Object || !Str(file, "path", out var path) || !Str(file, "sha256", out var sha)
                || !file.TryGetProperty("offset", out var o) || !o.TryGetInt64(out var offset)
                || !file.TryGetProperty("size", out var s) || !s.TryGetInt64(out var size) || size < 0)
                return Fail("manifest_invalid", "Manifest has a malformed file entry.");
            if (!PathRules.IsSafe(path)) return Fail("unsafe_path", $"Unsafe file path: {path}");
            if (sha.Length != 64 || !sha.All(char.IsAsciiHexDigit)) return Fail("manifest_invalid", $"Missing file hash for {path}.");
            if (!seen.Add(path)) return Fail("manifest_invalid", $"File {path} is listed twice.");
            if (offset != expected) return Fail("file_table_invalid", $"Non-contiguous file table at {path}.");
            expected += size;
            if (expected > (long)raw) return Fail("file_table_invalid", $"File {path} is out of bounds.");
            table.Add((path, offset, size, sha.ToLowerInvariant()));
        }

        if (expected != (long)raw) return Fail("file_table_invalid", "File table does not cover the payload.");
        return table;
    }

    private static byte[]? Decompress(ReadOnlyMemory<byte> compressed, long expectedLength)
    {
        try
        {
            using var input = new MemoryStream(compressed.ToArray(), writable: false);
            using var zstd = new DecompressionStream(input);
            var output = new byte[expectedLength];
            var total = 0;
            while (total < output.Length)
            {
                var read = zstd.Read(output, total, output.Length - total);
                if (read == 0) return null;
                total += read;
            }

            return zstd.ReadByte() == -1 ? output : null;
        }
        catch (Exception e) when (e is ZstdException or InvalidDataException or IOException or ArgumentException)
        {
            return null;
        }
    }

    private static bool Str(JsonElement element, string name, out string value)
    {
        value = element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : "";
        return value.Length > 0;
    }

    private static Error Fail(string code, string message) =>
        Error.Validation(code, message, new Dictionary<string, string[]> { ["package"] = [message] });
}

internal static class PathRules
{
    public static bool IsSafe(string path)
    {
        if (path.Length is 0 or > 512 || path.Contains('\\') || path.Contains(':') || path.StartsWith('/')) return false;
        return path.Split('/').All(part => part.Length > 0 && part is not "." and not "..");
    }
}

internal static class CanonicalJson
{
    public static string? Write(JsonElement element)
    {
        var sb = new StringBuilder();
        return Append(sb, element) ? sb.ToString() : null;
    }

    private static bool Append(StringBuilder sb, JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                var props = e.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
                for (var i = 1; i < props.Count; i++)
                    if (props[i].Name == props[i - 1].Name) return false;
                sb.Append('{');
                for (var i = 0; i < props.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    AppendString(sb, props[i].Name);
                    sb.Append(':');
                    if (!Append(sb, props[i].Value)) return false;
                }

                sb.Append('}');
                return true;
            case JsonValueKind.Array:
                sb.Append('[');
                var first = true;
                foreach (var item in e.EnumerateArray())
                {
                    if (!first) sb.Append(',');
                    first = false;
                    if (!Append(sb, item)) return false;
                }

                sb.Append(']');
                return true;
            case JsonValueKind.String:
                AppendString(sb, e.GetString()!);
                return true;
            default:
                sb.Append(e.GetRawText());
                return true;
        }
    }

    private static void AppendString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (var c in s)
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case < ' ': sb.Append("\\u").Append(((int)c).ToString("x4")); break;
                default: sb.Append(c); break;
            }

        sb.Append('"');
    }
}
