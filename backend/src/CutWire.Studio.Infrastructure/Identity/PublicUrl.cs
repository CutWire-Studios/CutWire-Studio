using Microsoft.Extensions.Configuration;

namespace CutWire.Studio.Infrastructure.Identity;

public sealed class PublicUrl(IConfiguration configuration)
{
    public string Base => (configuration["PublicUrl"] ?? "http://localhost:8087").TrimEnd('/');

    public string Origin => new Uri(Base).GetLeftPart(UriPartial.Authority);

    public string[] StudioResources => [Base + "/", Absolute("/mcp")];

    /// <summary>The CutWire Synth MCP resource, or null when <c>Synth:PublicUrl</c> is unset.</summary>
    public string? SynthMcp => configuration["Synth:PublicUrl"] is { Length: > 0 } synth && synth.Trim().TrimEnd('/') is { Length: > 0 } trimmed
        ? trimmed + "/mcp"
        : null;

    public string? SynthBase => SynthMcp is null ? null : SynthMcp[..^"/mcp".Length];

    public string[] Resources => SynthMcp is { } synth ? [.. StudioResources, synth] : StudioResources;

    public string Absolute(string path) => Base + path;
}
