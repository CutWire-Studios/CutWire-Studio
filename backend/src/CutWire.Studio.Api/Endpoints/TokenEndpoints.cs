using CutWire.Studio.Api.Auth;
using CutWire.Studio.Api.Http;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Accounts;
using CutWire.Studio.Infrastructure.Identity;

namespace CutWire.Studio.Api.Endpoints;

public sealed record CreateTokenRequest(string? Name, string[]? Scopes, DateTimeOffset? ExpiresAt);

public static class TokenEndpoints
{
    public static void MapTokens(this RouteGroupBuilder account)
    {
        var tokens = account.MapGroup("/tokens").WithTags("Personal access tokens").RequireAuthorization(AuthPolicies.Session)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        tokens.MapGet("", async (HttpContext http, IQueryHandler<ListPatsQuery, IReadOnlyList<PatDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new ListPatsQuery(http.User.GetUserId()), ct)).ToHttpResult(Results.Ok))
            .WithName("ListTokens")
            .Produces<IReadOnlyList<PatDto>>();

        tokens.MapPost("", async (CreateTokenRequest body, HttpContext http,
                    ICommandHandler<CreatePatCommand, CreatedPatDto> handler, CancellationToken ct) =>
                (await handler.Handle(new CreatePatCommand(http.User.GetUserId(), body.Name, body.Scopes, body.ExpiresAt), ct))
                .ToHttpResult(created => Results.Created($"/api/v1/account/tokens/{created.Token.Id}", created)))
            .WithName("CreateToken")
            .Produces<CreatedPatDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        tokens.MapDelete("/{id:guid}", async (Guid id, HttpContext http,
                    ICommandHandler<RevokePatCommand, bool> handler, CancellationToken ct) =>
                (await handler.Handle(new RevokePatCommand(http.User.GetUserId(), id), ct)).ToHttpResult(_ => Results.NoContent()))
            .WithName("RevokeToken")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    public static void MapConnectedApps(this RouteGroupBuilder account)
    {
        var apps = account.MapGroup("/connected-apps").WithTags("Connected apps").RequireAuthorization(AuthPolicies.Session)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        apps.MapGet("", async (HttpContext http, ConnectedAppService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(http.User.GetUserId(), ct)))
            .WithName("ListConnectedApps")
            .Produces<IReadOnlyList<ConnectedAppDto>>();

        apps.MapDelete("/{id}", async (string id, HttpContext http, ConnectedAppService service, CancellationToken ct) =>
                (await service.RevokeAsync(http.User.GetUserId(), id, ct)).ToHttpResult(() => Results.NoContent()))
            .WithName("RevokeConnectedApp")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
