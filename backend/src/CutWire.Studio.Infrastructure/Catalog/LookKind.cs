using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CutWire.Studio.Application.Catalog;
using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Domain.Common;

namespace CutWire.Studio.Infrastructure.Catalog;

public sealed partial class LookKind() : AssetKindBase(AssetKindId.Look)
{
    public const int MinSize = 2;
    public const int MaxSize = 65;

    protected override Result<PackageInfo> Validate(byte[] bytes, Sha256Hash sha256)
    {
        if (Array.IndexOf(bytes, (byte)0) >= 0) return Invalid("lut_not_text", "A .cube file is plain text.");

        string? title = null;
        var size = 0;
        double[] min = [0, 0, 0], max = [1, 1, 1];
        var rows = 0;
        var text = Encoding.UTF8.GetString(bytes);
        var lineNumber = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            if (parts[0] == "TITLE")
            {
                title = line[5..].Trim().Trim('"');
            }
            else if (parts[0] == "LUT_3D_SIZE")
            {
                if (parts.Length != 2 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out size) || size is < MinSize or > MaxSize)
                    return Invalid("lut_size_invalid", $"LUT_3D_SIZE must be an integer between {MinSize} and {MaxSize}.");
            }
            else if (parts[0] == "LUT_1D_SIZE")
            {
                return Invalid("lut_1d_unsupported", "1D LUTs are not supported; use a 3D LUT.");
            }
            else if (parts[0] is "DOMAIN_MIN" or "DOMAIN_MAX")
            {
                if (!TryFloats(parts, 1, 3, out var values)) return Invalid("lut_domain_invalid", $"{parts[0]} needs three finite numbers (line {lineNumber}).");
                if (parts[0] == "DOMAIN_MIN") min = values; else max = values;
            }
            else if (parts[0] == "LUT_3D_INPUT_RANGE")
            {
                if (!TryFloats(parts, 1, 2, out var values)) return Invalid("lut_domain_invalid", $"LUT_3D_INPUT_RANGE needs two finite numbers (line {lineNumber}).");
                min = [values[0], values[0], values[0]];
                max = [values[1], values[1], values[1]];
            }
            else if (char.IsAsciiLetter(parts[0][0]))
            {
                return Invalid("lut_unknown_keyword", $"Unknown keyword '{parts[0]}' on line {lineNumber}.");
            }
            else
            {
                if (size == 0) return Invalid("lut_size_missing", "LUT_3D_SIZE must come before the data.");
                if (!TryFloats(parts, 0, 3, out _))
                    return Invalid("lut_row_invalid", $"Line {lineNumber} is not three finite numbers.");
                rows++;
                if (rows > size * size * size) return Invalid("lut_row_count", $"A size-{size} LUT has {size * size * size} rows; the file has more.");
            }
        }

        if (size == 0) return Invalid("lut_size_missing", "LUT_3D_SIZE is required.");
        if (rows != size * size * size) return Invalid("lut_row_count", $"A size-{size} LUT needs {size * size * size} rows; the file has {rows}.");
        for (var i = 0; i < 3; i++)
            if (min[i] >= max[i]) return Invalid("lut_domain_invalid", "DOMAIN_MIN must be below DOMAIN_MAX on every channel.");

        var manifest = JsonSerializer.SerializeToElement(new { title, size, domainMin = min, domainMax = max });
        var id = Slug(title) is { Length: > 0 } slug ? slug : $"look-{sha256.Value[..12]}";
        var file = new PackageFileEntry("look.cube", bytes.Length, sha256);
        return new PackageInfo(PackageFormat.Cube, manifest, id, "1.0.0", "0.1.0", "looks", [file], title ?? "", sha256);
    }

    private static bool TryFloats(string[] parts, int skip, int count, out double[] values)
    {
        values = new double[count];
        if (parts.Length != skip + count) return false;
        for (var i = 0; i < count; i++)
            if (!double.TryParse(parts[skip + i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || !double.IsFinite(values[i]))
                return false;
        return true;
    }

    private static string Slug(string? title) =>
        title is null ? "" : NonSlug().Replace(title.ToLowerInvariant(), "-").Trim('-');

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlug();
}
