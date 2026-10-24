using System.Text.RegularExpressions;

namespace CutWire.Studio.Infrastructure.Catalog;

internal static partial class Semver
{
    public static bool IsValid(string? value) => value is not null && Pattern().IsMatch(value);

    [GeneratedRegex(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$")]
    private static partial Regex Pattern();
}
