using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace CutWire.Studio.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(Claims.Subject), out var id)
            ? id
            : throw new InvalidOperationException("Principal has no subject.");

    public static bool IsCookieSession(this ClaimsPrincipal principal) =>
        principal.Identities.Any(i => i.AuthenticationType == IdentityConstants.ApplicationScheme);

    public static HashSet<string> GetGrantedScopes(this ClaimsPrincipal principal)
    {
        var scopes = new HashSet<string>(principal.GetScopes(), StringComparer.Ordinal);
        foreach (var claim in principal.FindAll(Claims.Scope))
            foreach (var scope in claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)) scopes.Add(scope);
        return scopes;
    }
}
