using System.Text.Json;
using CutWire.Studio.Api.Auth;
using CutWire.Studio.Api.Http;
using CutWire.Studio.Application.Forge;
using CutWire.Studio.Domain.Common;

namespace CutWire.Studio.Api.Endpoints;

public sealed record PutThemeDraftRequest(string? Id, string? Name, JsonElement Doc, long? UpdatedAt);

public static class ThemeDraftEndpoints
{
    public static void MapThemeDrafts(this RouteGroupBuilder api)
    {
        var drafts = api.MapGroup("/themes/drafts").WithTags("Themes").RequireAuthorization(AuthPolicies.Session, AuthPolicies.NotBanned)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        drafts.MapGet("", async (HttpContext http, ThemeDrafts service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(http.User.GetUserId(), ct)))
            .WithName("ListThemeDrafts")
            .Produces<IReadOnlyList<ThemeDraftDto>>();

        drafts.MapGet("/{id}", async (string id, HttpContext http, ThemeDrafts service, CancellationToken ct) =>
                (await service.GetAsync(http.User.GetUserId(), id, ct)).ToHttpResult(Results.Ok))
            .WithName("GetThemeDraft")
            .Produces<ThemeDraftDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        drafts.MapPut("/{id}", async (string id, PutThemeDraftRequest body, HttpContext http, ThemeDrafts service, CancellationToken ct) =>
                (await service.PutAsync(http.User.GetUserId(), id, body.Name, body.Doc, body.UpdatedAt, ct)).ToHttpResult(Results.Ok))
            .WithName("PutThemeDraft")
            .Produces<ThemeDraftDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);

        drafts.MapDelete("/{id}", async (string id, HttpContext http, ThemeDrafts service, CancellationToken ct) =>
                (await service.DeleteAsync(http.User.GetUserId(), id, ct)).ToHttpResult(_ => Results.NoContent()))
            .WithName("DeleteThemeDraft")
            .Produces(StatusCodes.Status204NoContent);
    }
}
