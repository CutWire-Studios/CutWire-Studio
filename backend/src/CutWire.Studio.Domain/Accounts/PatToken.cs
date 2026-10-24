using System.Security.Cryptography;
using System.Text;

namespace CutWire.Studio.Domain.Accounts;

public static class PatToken
{
    public const string Prefix = "cwpat_";
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

    public static string Generate() => Prefix + Base32(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static bool HasPrefix(string? token) =>
        token is not null && token.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Base32(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }
}
