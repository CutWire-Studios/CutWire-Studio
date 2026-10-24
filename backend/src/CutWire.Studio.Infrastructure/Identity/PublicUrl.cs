using Microsoft.Extensions.Configuration;

namespace CutWire.Studio.Infrastructure.Identity;

public sealed class PublicUrl(IConfiguration configuration)
{
    public string Base => (configuration["PublicUrl"] ?? "http://localhost:8087").TrimEnd('/');

    public string Origin => new Uri(Base).GetLeftPart(UriPartial.Authority);

    public string[] Resources => [Base + "/", Absolute("/mcp")];

    public string Absolute(string path) => Base + path;
}
