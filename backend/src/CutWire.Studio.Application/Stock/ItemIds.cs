using System.Text;

namespace CutWire.Studio.Application.Stock;

public readonly record struct ItemRef(string Provider, string Type, string Native);

/// <summary>Opaque, self-describing item ids: base64url("provider:type:native"). Anything decoded is validated against the registry before use.</summary>
public static class ItemIds
{
    public static string Encode(string provider, string type, string native)
    {
        if (provider.Contains(':') || type.Contains(':'))
            throw new ArgumentException("provider and type cannot contain ':'");
        return Base64Url(Encoding.UTF8.GetBytes($"{provider}:{type}:{native}"));
    }

    public static string Encode(ItemRef r) => Encode(r.Provider, r.Type, r.Native);

    public static bool TryDecode(string? id, out ItemRef item)
    {
        item = default;
        if (string.IsNullOrEmpty(id) || id.Length > 4096) return false;
        try
        {
            var text = Encoding.UTF8.GetString(FromBase64Url(id));
            var parts = text.Split(':', 3);
            if (parts.Length != 3 || parts.Any(p => p.Length == 0)) return false;
            item = new ItemRef(parts[0], parts[1], parts[2]);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }
}

/// <summary>Cache for provider responses (IMemoryCache in front of a Postgres table).</summary>
public interface IProviderCache
{
    Task<string?> GetAsync(string key, CancellationToken ct);
    Task SetAsync(string key, string body, TimeSpan ttl, CancellationToken ct);
}
