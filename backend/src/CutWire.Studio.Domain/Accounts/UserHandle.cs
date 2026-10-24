using System.Text.RegularExpressions;
using CutWire.Studio.Domain.Common;

namespace CutWire.Studio.Domain.Accounts;

public static partial class UserHandle
{
    public const int MinLength = 3;
    public const int MaxLength = 24;

    public static Result<string> Normalize(string? input)
    {
        var handle = input?.Trim().ToLowerInvariant() ?? "";
        if (!Pattern().IsMatch(handle))
            return Error.Validation("profile.invalid_handle", "Handle is invalid.",
                new Dictionary<string, string[]>
                {
                    ["handle"] = [$"Use {MinLength}-{MaxLength} characters: a-z, 0-9, '_' or '-'."],
                });
        return handle;
    }

    [GeneratedRegex("^[a-z0-9_-]{3,24}$")]
    private static partial Regex Pattern();
}
