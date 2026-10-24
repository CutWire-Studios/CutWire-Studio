using System.Diagnostics.CodeAnalysis;

namespace CutWire.Studio.Domain.Blobs;

public readonly record struct Sha256Hash
{
    public const int HexLength = 64;

    public string Value { get; }

    public Sha256Hash(string value)
    {
        if (!TryNormalize(value, out var normalized))
            throw new ArgumentException("Not a valid SHA-256 hex digest.", nameof(value));
        Value = normalized;
    }

    public static bool TryParse([NotNullWhen(true)] string? value, out Sha256Hash hash)
    {
        if (TryNormalize(value, out var normalized))
        {
            hash = new Sha256Hash(normalized);
            return true;
        }

        hash = default;
        return false;
    }

    public static Sha256Hash FromBytes(ReadOnlySpan<byte> digest) => new(Convert.ToHexStringLower(digest));

    public override string ToString() => Value;

    private static bool TryNormalize([NotNullWhen(true)] string? value, [NotNullWhen(true)] out string? normalized)
    {
        normalized = null;
        if (value is not { Length: HexLength }) return false;
        foreach (var c in value)
            if (!char.IsAsciiHexDigit(c)) return false;
        normalized = value.ToLowerInvariant();
        return true;
    }
}
