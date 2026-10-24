using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using CutWire.Studio.Api.Auth;
using CutWire.Studio.Api.Endpoints;

namespace CutWire.Studio.IntegrationTests;

public sealed class TestEndpoints : IApiContributor
{
    public void Map(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/_test");
        group.MapGet("/forge-read", () => Results.Ok("read")).RequireAuthorization(AuthPolicies.Scope("forge:read"));
        group.MapGet("/forge-write", () => Results.Ok("write")).RequireAuthorization(AuthPolicies.Scope("forge:write"));
        group.MapPost("/forge-write", () => Results.Ok("write")).RequireAuthorization(AuthPolicies.Scope("forge:write"));
        group.MapGet("/not-banned", () => Results.Ok("fine")).RequireAuthorization(AuthPolicies.NotBanned);
        group.MapGet("/moderator", () => Results.Ok("mod")).RequireAuthorization(AuthPolicies.Role("moderator"));
    }
}
