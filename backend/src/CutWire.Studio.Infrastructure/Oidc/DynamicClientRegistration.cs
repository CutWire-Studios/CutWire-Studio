using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Infrastructure.Identity;
using Microsoft.AspNetCore.WebUtilities;
using OpenIddict.Abstractions;

namespace CutWire.Studio.Infrastructure.Oidc;

public sealed record ClientRegistrationRequest
{
    [JsonPropertyName("client_name")] public string? ClientName { get; init; }
    [JsonPropertyName("redirect_uris")] public string[]? RedirectUris { get; init; }
    [JsonPropertyName("grant_types")] public string[]? GrantTypes { get; init; }
    [JsonPropertyName("response_types")] public string[]? ResponseTypes { get; init; }
    [JsonPropertyName("token_endpoint_auth_method")] public string? TokenEndpointAuthMethod { get; init; }
    [JsonPropertyName("scope")] public string? Scope { get; init; }
}

public sealed record ClientRegistrationResponse(
    [property: JsonPropertyName("client_id")] string ClientId,
    [property: JsonPropertyName("client_id_issued_at")] long ClientIdIssuedAt,
    [property: JsonPropertyName("client_name")] string ClientName,
    [property: JsonPropertyName("redirect_uris")] string[] RedirectUris,
    [property: JsonPropertyName("grant_types")] string[] GrantTypes,
    [property: JsonPropertyName("response_types")] string[] ResponseTypes,
    [property: JsonPropertyName("token_endpoint_auth_method")] string TokenEndpointAuthMethod,
    [property: JsonPropertyName("scope")] string Scope);

public sealed class DynamicClientRegistration(IOpenIddictApplicationManager applications, IClock clock, PublicUrl publicUrl)
{
    public const int MaxRedirectUris = 5;
    public const int MaxUriLength = 2048;
    public const int MaxNameLength = 100;

    private static readonly string[] SupportedGrants = ["authorization_code", "refresh_token"];

    public async Task<Result<ClientRegistrationResponse>> RegisterAsync(ClientRegistrationRequest request, CancellationToken ct)
    {
        var uris = request.RedirectUris ?? [];
        if (uris.Length is 0 or > MaxRedirectUris || uris.Any(u => !IsAllowedRedirect(u)))
            return Error.Validation("invalid_redirect_uri",
                $"Provide 1-{MaxRedirectUris} redirect URIs that are https or loopback http URLs without fragments.");

        if (request.TokenEndpointAuthMethod is not (null or "none"))
            return Error.Validation("invalid_client_metadata", "Only public clients (token_endpoint_auth_method 'none') are supported.");

        var grants = request.GrantTypes is { Length: > 0 } ? request.GrantTypes : SupportedGrants;
        if (grants.Any(g => !SupportedGrants.Contains(g)))
            return Error.Validation("invalid_client_metadata", "Supported grant_types: authorization_code, refresh_token.");

        if (request.ResponseTypes is { Length: > 0 } && request.ResponseTypes.Any(r => r != "code"))
            return Error.Validation("invalid_client_metadata", "Only response_type 'code' is supported.");

        var requested = request.Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (requested is { Length: > 0 } && requested.Any(s => !OidcClients.DynamicScopes.Contains(s)))
            return Error.Validation("invalid_client_metadata",
                $"Supported scopes: {string.Join(' ', OidcClients.DynamicScopes)}.");

        var name = request.ClientName?.Trim();
        name = string.IsNullOrEmpty(name) ? "MCP client" : name.Length > MaxNameLength ? name[..MaxNameLength] : name;

        var now = clock.UtcNow;
        var clientId = "dyn_" + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(18));
        var descriptor = OidcClients.Public(clientId, name, OidcClients.DynamicScopes, publicUrl.Resources);
        foreach (var uri in uris) descriptor.RedirectUris.Add(new Uri(uri));
        descriptor.Properties[OidcClients.DynamicProperty] = JsonSerializer.SerializeToElement(true);
        descriptor.Properties[OidcClients.CreatedAtProperty] = JsonSerializer.SerializeToElement(now);
        await applications.CreateAsync(descriptor, ct);

        return new ClientRegistrationResponse(
            clientId,
            now.ToUnixTimeSeconds(),
            name,
            uris,
            SupportedGrants,
            ["code"],
            "none",
            string.Join(' ', requested is { Length: > 0 } ? requested : OidcClients.DynamicScopes));
    }

    private static bool IsAllowedRedirect(string value)
    {
        if (value.Length > MaxUriLength || !Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        if (!string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        return uri.Scheme == Uri.UriSchemeHttps ||
               (uri.Scheme == Uri.UriSchemeHttp && RedirectUriMatcher.IsLoopbackHost(uri.Host));
    }
}
