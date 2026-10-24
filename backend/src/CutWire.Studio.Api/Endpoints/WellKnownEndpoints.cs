using CutWire.Studio.Domain.Accounts;
using CutWire.Studio.Infrastructure.Identity;

namespace CutWire.Studio.Api.Endpoints;

public static class WellKnownEndpoints
{
    public static void MapWellKnown(this IEndpointRouteBuilder app)
    {
        foreach (var path in new[] { "/.well-known/oauth-protected-resource", "/.well-known/oauth-protected-resource/mcp" })
            app.MapGet(path, (PublicUrl publicUrl) => Results.Json(new
                {
                    resource = publicUrl.Absolute("/mcp"),
                    authorization_servers = new[] { publicUrl.Base + "/" },
                    scopes_supported = AuthScopes.Personal,
                    bearer_methods_supported = new[] { "header" },
                    resource_name = "CutWire Forge MCP",
                }))
                .ExcludeFromDescription();
    }
}
