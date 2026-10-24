using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace CutWire.Studio.Domain.Catalog;

/// <summary>Semantic version with SemVer 2.0 precedence (build metadata is ignored).</summary>
public sealed partial class SemVersion : IComparable<SemVersion>
{
    private readonly string[] _pre;

    private SemVersion(long major, long minor, long patch, string[] pre)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        _pre = pre;
    }

    public long Major { get; }
    public long Minor { get; }
    public long Patch { get; }

    public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out SemVersion? version)
    {
        version = null;
        if (value is null) return false;
        var m = Pattern().Match(value);
        if (!m.Success) return false;
        if (!long.TryParse(m.Groups[1].Value, out var major) || !long.TryParse(m.Groups[2].Value, out var minor)
            || !long.TryParse(m.Groups[3].Value, out var patch)) return false;
        var pre = m.Groups[4].Success ? m.Groups[4].Value.Split('.') : [];
        version = new SemVersion(major, minor, patch, pre);
        return true;
    }

    public int CompareTo(SemVersion? other)
    {
        if (other is null) return 1;
        var c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;
        if (_pre.Length == 0) return other._pre.Length == 0 ? 0 : 1;
        if (other._pre.Length == 0) return -1;
        for (var i = 0; i < Math.Min(_pre.Length, other._pre.Length); i++)
        {
            var a = _pre[i];
            var b = other._pre[i];
            var aNumeric = a.All(char.IsAsciiDigit);
            var bNumeric = b.All(char.IsAsciiDigit);
            c = aNumeric && bNumeric ? decimal.Parse(a).CompareTo(decimal.Parse(b))
                : aNumeric ? -1
                : bNumeric ? 1
                : string.CompareOrdinal(a, b);
            if (c != 0) return Math.Sign(c);
        }

        return _pre.Length.CompareTo(other._pre.Length);
    }

    [GeneratedRegex(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$")]
    private static partial Regex Pattern();
}
