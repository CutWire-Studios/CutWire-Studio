using System.Security.Claims;
using CutWire.Studio.Application.Accounts;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Infrastructure.Identity;
using CutWire.Studio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace CutWire.Studio.Infrastructure.Oidc;

public sealed class OidcPrincipalFactory(StudioDbContext db, IClock clock, PublicUrl publicUrl)
{
    public async Task<ClaimsPrincipal> CreateAsync(ApplicationUser user, IEnumerable<string> scopes,
        IEnumerable<string> resources, string? authorizationId, CancellationToken ct)
    {
        var profile = await db.UserProfiles.AsNoTracking().FirstAsync(p => p.Id == user.Id, ct);
        var dto = ProfileDto.From(profile, clock.UtcNow);

        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, user.Id.ToString())
            .SetClaim(Claims.Name, dto.DisplayName)
            .SetClaim(Claims.PreferredUsername, dto.Handle)
            .SetClaim(Claims.Email, user.Email)
            .SetClaim(Claims.EmailVerified, user.EmailConfirmed);
        if (dto.AvatarUrl is not null) identity.SetClaim(Claims.Picture, publicUrl.Absolute(dto.AvatarUrl));

        identity.SetScopes(scopes);
        identity.SetResources(resources);
        if (authorizationId is not null) identity.SetAuthorizationId(authorizationId);

        var principal = new ClaimsPrincipal(identity);
        identity.SetDestinations(claim => claim.Type switch
        {
            Claims.Subject => [Destinations.AccessToken, Destinations.IdentityToken],
            Claims.Name or Claims.PreferredUsername or Claims.Picture when principal.HasScope(Scopes.Profile) =>
                [Destinations.IdentityToken],
            Claims.Email or Claims.EmailVerified when principal.HasScope(Scopes.Email) => [Destinations.IdentityToken],
            _ => [],
        });
        return principal;
    }
}
