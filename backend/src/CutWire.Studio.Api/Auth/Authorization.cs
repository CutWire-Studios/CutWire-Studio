using CutWire.Studio.Api.Http;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Api.Auth;

public sealed record ScopeRequirement(string Scope) : IAuthorizationRequirement;

public sealed class NotBannedRequirement : IAuthorizationRequirement;

public sealed class ScopeHandler : AuthorizationHandler<ScopeRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ScopeRequirement requirement)
    {
        if (context.User.IsCookieSession() || context.User.GetGrantedScopes().Contains(requirement.Scope))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

public sealed class NotBannedHandler(IAppDbContext db, IClock clock) : AuthorizationHandler<NotBannedRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, NotBannedRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true) return;

        var userId = context.User.GetUserId();
        var profile = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == userId);
        if (profile is not null && !profile.IsBanned(clock.UtcNow)) context.Succeed(requirement);
    }
}

public sealed class StudioPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(AuthPolicies.ScopePrefix, StringComparison.Ordinal))
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new ScopeRequirement(policyName[AuthPolicies.ScopePrefix.Length..]))
                .Build();

        if (policyName.StartsWith(AuthPolicies.RolePrefix, StringComparison.Ordinal))
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireRole(policyName[AuthPolicies.RolePrefix.Length..].Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Build();

        return await base.GetPolicyAsync(policyName);
    }
}

public sealed class StudioAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden && authorizeResult.AuthorizationFailure is { } failure)
        {
            var failed = failure.FailedRequirements.ToList();
            if (failed.OfType<NotBannedRequirement>().Any())
            {
                await Error.Forbidden("banned", "Your account is banned from this action.").ToHttpResult().ExecuteAsync(context);
                return;
            }

            if (failed.OfType<ScopeRequirement>().FirstOrDefault() is { } scope)
            {
                context.Response.Headers.WWWAuthenticate = $"Bearer error=\"insufficient_scope\", scope=\"{scope.Scope}\"";
                await Error.Forbidden("insufficient_scope", $"The '{scope.Scope}' scope is required.").ToHttpResult().ExecuteAsync(context);
                return;
            }
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
