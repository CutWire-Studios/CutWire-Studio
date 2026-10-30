using CutWire.Studio.Domain.Accounts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Validation.AspNetCore;

namespace CutWire.Studio.Api.Auth;

public static class AuthExtensions
{
    public static IServiceCollection AddStudioApiAuth(this IServiceCollection services)
    {
        services.AddAuthentication(o =>
            {
                o.DefaultScheme = AuthSchemes.Studio;
                o.DefaultAuthenticateScheme = AuthSchemes.Studio;
                o.DefaultChallengeScheme = AuthSchemes.Studio;
                o.DefaultForbidScheme = AuthSchemes.Studio;
            })
            .AddPolicyScheme(AuthSchemes.Studio, displayName: null, o => o.ForwardDefaultSelector = ctx =>
            {
                var header = ctx.Request.Headers.Authorization.ToString();
                if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    return IdentityConstants.ApplicationScheme;
                return header["Bearer ".Length..].TrimStart().StartsWith("cwpat_", StringComparison.Ordinal)
                    ? AuthSchemes.Pat
                    : OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            })
            .AddScheme<AuthenticationSchemeOptions, PatAuthenticationHandler>(AuthSchemes.Pat, null);

        services.AddAuthorization(o =>
        {
            o.AddPolicy(AuthPolicies.Session, p => p
                .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme)
                .RequireAuthenticatedUser());
            o.AddPolicy(AuthPolicies.NotBanned, p => p
                .RequireAuthenticatedUser()
                .AddRequirements(new NotBannedRequirement()));
            o.AddPolicy(AuthPolicies.AdminArea, p => p
                .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme)
                .RequireAuthenticatedUser()
                .RequireRole(Roles.Moderator, Roles.Admin)
                .AddRequirements(new NotBannedRequirement()));
            o.AddPolicy(AuthPolicies.AdminOnly, p => p
                .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme)
                .RequireAuthenticatedUser()
                .RequireRole(Roles.Admin)
                .AddRequirements(new NotBannedRequirement()));
        });
        services.AddSingleton<IAuthorizationPolicyProvider, StudioPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, ScopeHandler>();
        services.AddScoped<IAuthorizationHandler, NotBannedHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, StudioAuthorizationResultHandler>();

        return services;
    }

    public static IApplicationBuilder UseStudioCsrf(this IApplicationBuilder app) => app.UseMiddleware<CsrfMiddleware>();
}
