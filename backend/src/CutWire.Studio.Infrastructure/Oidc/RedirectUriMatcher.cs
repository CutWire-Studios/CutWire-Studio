namespace CutWire.Studio.Infrastructure.Oidc;

public static class RedirectUriMatcher
{
    public static bool IsLoopbackHost(string host) =>
        host is "127.0.0.1" or "[::1]" or "localhost";

    public static bool Matches(IEnumerable<string> registered, string candidate)
    {
        var list = registered as IReadOnlyCollection<string> ?? registered.ToList();
        if (list.Contains(candidate, StringComparer.Ordinal)) return true;

        if (!TryLoopback(candidate, out var uri)) return false;
        foreach (var entry in list)
        {
            if (TryLoopback(entry, out var other) &&
                string.Equals(uri.Host, other.Host, StringComparison.OrdinalIgnoreCase) &&
                uri.AbsolutePath == other.AbsolutePath &&
                uri.Query == other.Query)
                return true;
        }

        return false;
    }

    private static bool TryLoopback(string value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) &&
            parsed.Scheme == Uri.UriSchemeHttp && string.IsNullOrEmpty(parsed.UserInfo) &&
            IsLoopbackHost(parsed.Host))
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }
}
