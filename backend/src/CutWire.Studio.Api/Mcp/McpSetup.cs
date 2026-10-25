using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using CutWire.Studio.Api.Auth;
using CutWire.Studio.Api.Http;
using CutWire.Studio.Domain.Accounts;
using CutWire.Studio.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using OpenIddict.Abstractions;

namespace CutWire.Studio.Api.Mcp;

public static class McpSetup
{
    public const string RateLimitPolicy = "mcp";
    public const string AuthPolicy = "mcp";

    public static IServiceCollection AddStudioMcp(this IServiceCollection services)
    {
        services.AddSingleton<ForgeMcpTools>();
        services.AddMcpServer(o =>
            {
                o.ServerInfo = new Implementation { Name = "drift-forge", Title = "Drift Forge", Version = "1.0.0" };
                o.ServerInstructions = ForgeMcpTools.Instructions;
            })
            .WithHttpTransport(o => o.Stateless = true)
            .WithListToolsHandler((ctx, _) => ValueTask.FromResult(new ListToolsResult
            {
                Tools = ctx.Services!.GetRequiredService<ForgeMcpTools>().ListTools().ToList(),
            }))
            .WithCallToolHandler(async (ctx, ct) =>
            {
                var principal = ctx.User ?? throw new McpException("Not authenticated.");
                var args = new JsonObject();
                foreach (var (key, value) in ctx.Params!.Arguments ?? new Dictionary<string, JsonElement>())
                    args[key] = JsonNode.Parse(value.GetRawText());
                await using var scope = ctx.Services!.CreateAsyncScope();
                return await ctx.Services!.GetRequiredService<ForgeMcpTools>()
                    .CallAsync(ctx.Params!.Name, args, principal.GetUserId(), principal.GetGrantedScopes(), scope.ServiceProvider, ct);
            });

        services.AddAuthorizationBuilder().AddPolicy(AuthPolicy, p => p
            .RequireAuthenticatedUser()
            .AddRequirements(new ScopeRequirement(AuthScopes.ForgeRead), new McpResourceRequirement()));
        services.AddSingleton<IAuthorizationHandler, McpAudienceHandler>();
        services.Configure<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>(o => o.AddPolicy(RateLimitPolicy, http =>
            RateLimitPartition.GetFixedWindowLimiter(
                http.User.Identity?.IsAuthenticated == true && http.User.FindFirst("sub")?.Value is { } sub ? "u:" + sub : "ip:" + http.Connection.RemoteIpAddress,
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
        return services;
    }

    public static void MapStudioMcp(this WebApplication app)
    {
        app.MapMcp("/mcp").RequireAuthorization(AuthPolicy).RequireRateLimiting(RateLimitPolicy);
    }

    /// <summary>
    /// Per the MCP authorization spec an unauthenticated request gets a 401 that points at the protected-resource metadata. Browsers'
    /// cookies don't count: /mcp only takes bearer tokens.
    /// </summary>
    public static IApplicationBuilder UseMcpChallenge(this IApplicationBuilder app) => app.UseWhen(
        http => http.Request.Path.StartsWithSegments("/mcp"),
        branch => branch.Use(async (http, next) =>
        {
            var publicUrl = http.RequestServices.GetRequiredService<PublicUrl>();
            var metadata = $"resource_metadata=\"{publicUrl.Absolute("/.well-known/oauth-protected-resource")}\"";
            http.Response.OnStarting(() =>
            {
                if (http.Response.StatusCode == StatusCodes.Status401Unauthorized)
                {
                    var existing = http.Response.Headers.WWWAuthenticate.ToString();
                    http.Response.Headers.WWWAuthenticate = existing.StartsWith("Bearer ", StringComparison.Ordinal) && !existing.Contains("resource_metadata")
                        ? $"{existing}, {metadata}"
                        : $"Bearer {metadata}";
                }

                return Task.CompletedTask;
            });
            if (!http.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next(http);
        }));
}

public sealed class McpResourceRequirement : IAuthorizationRequirement;

/// <summary>An OAuth access token only works here when it was issued for this resource; personal access tokens are for the user's own account.</summary>
public sealed class McpAudienceHandler(PublicUrl publicUrl) : AuthorizationHandler<McpResourceRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, McpResourceRequirement requirement)
    {
        var principal = context.User;
        var isPat = principal.Identities.Any(i => i.AuthenticationType == AuthSchemes.Pat);
        if (isPat || principal.GetAudiences().Contains(publicUrl.Absolute("/mcp"))) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
