using CutWire.Studio.Domain.Accounts;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace CutWire.Studio.Infrastructure.Oidc;

public static class OidcClients
{
    public const string DriftDesktop = "drift-desktop";
    public const string Mcp = "mcp";
    public const string DynamicProperty = "dynamic";
    public const string CreatedAtProperty = "created_at";

    public static readonly string[] StandardScopes = [Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.OfflineAccess];

    public static readonly string[] ServerScopes = [.. StandardScopes, .. AuthScopes.Api];

    public static readonly string[] DynamicScopes = [.. StandardScopes, .. AuthScopes.Personal];

    public static OpenIddictApplicationDescriptor Drift(IEnumerable<string> resources)
    {
        var descriptor = Public(DriftDesktop, "Drift", ServerScopes, resources);
        descriptor.RedirectUris.Add(new Uri("http://127.0.0.1/callback"));
        descriptor.RedirectUris.Add(new Uri("cutwire://auth/callback"));
        descriptor.Permissions.Add(Permissions.Endpoints.DeviceAuthorization);
        descriptor.Permissions.Add(Permissions.GrantTypes.DeviceCode);
        return descriptor;
    }

    public static OpenIddictApplicationDescriptor McpPlaceholder(IEnumerable<string> resources)
    {
        var descriptor = Public(Mcp, "MCP client", DynamicScopes, resources);
        descriptor.RedirectUris.Add(new Uri("http://127.0.0.1/callback"));
        descriptor.RedirectUris.Add(new Uri("http://localhost/callback"));
        return descriptor;
    }

    public static OpenIddictApplicationDescriptor Public(string clientId, string displayName, IEnumerable<string> scopes,
        IEnumerable<string> resources)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Explicit,
            DisplayName = displayName,
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.Revocation,
                Permissions.Endpoints.EndSession,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };
        foreach (var scope in scopes) descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);
        descriptor.AddResourcePermissions(resources.ToArray());
        return descriptor;
    }
}
