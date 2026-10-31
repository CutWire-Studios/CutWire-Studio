namespace CutWire.Studio.Api.Spa;

public sealed record AppHost(string Prefix, string Dir, IReadOnlyDictionary<string, string>? Headers = null)
{
    public bool IsRoot => Prefix == "/";
}

public static class AppHostRegistry
{
    private static readonly IReadOnlyDictionary<string, string> CrossOriginIsolation = new Dictionary<string, string>
    {
        ["Cross-Origin-Opener-Policy"] = "same-origin",
        ["Cross-Origin-Embedder-Policy"] = "require-corp",
    };

    public static readonly IReadOnlyList<AppHost> Default =
    [
        new("/forge", "forge"),
        new("/themes", "themes"),
        new("/edit", "edit", CrossOriginIsolation),
        new("/", "shell"),
    ];

    public static readonly string[] ReservedPrefixes = ["/api", "/connect", "/mcp", "/files", "/.well-known", "/admin",
        "/signin-github", "/signin-google", "/signin-discord"];
}

public sealed class SpaOptions
{
    public const string Section = "Spa";

    public string? Root { get; set; }
}
