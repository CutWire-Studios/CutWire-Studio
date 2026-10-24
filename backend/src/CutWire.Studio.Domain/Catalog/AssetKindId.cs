using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace CutWire.Studio.Domain.Catalog;

public readonly partial record struct AssetKindId
{
    public static readonly AssetKindId Effect = new("effect");
    public static readonly AssetKindId Transition = new("transition");
    public static readonly AssetKindId AudioEffect = new("audio-effect");
    public static readonly AssetKindId Theme = new("theme");
    public static readonly AssetKindId Look = new("look");
    public static readonly AssetKindId EffectTemplate = new("effect-template");
    public static readonly AssetKindId LanguagePack = new("language-pack");

    public string Value { get; }

    public AssetKindId(string value)
    {
        if (!TryNormalize(value, out var normalized))
            throw new ArgumentException("Not a valid asset kind id (lowercase letters, digits and dashes).", nameof(value));
        Value = normalized;
    }

    public static bool TryParse([NotNullWhen(true)] string? value, out AssetKindId id)
    {
        if (TryNormalize(value, out var normalized))
        {
            id = new AssetKindId(normalized);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value;

    private static bool TryNormalize([NotNullWhen(true)] string? value, [NotNullWhen(true)] out string? normalized)
    {
        normalized = value?.Trim().ToLowerInvariant();
        return normalized is not null && Pattern().IsMatch(normalized);
    }

    [GeneratedRegex("^[a-z][a-z0-9]*(-[a-z0-9]+)*$")]
    private static partial Regex Pattern();
}
