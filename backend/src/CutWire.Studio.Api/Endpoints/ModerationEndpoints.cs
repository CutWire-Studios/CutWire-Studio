using CutWire.Studio.Api.Auth;
using CutWire.Studio.Api.Http;
using CutWire.Studio.Application.Moderation;
using Microsoft.AspNetCore.Mvc;

namespace CutWire.Studio.Api.Endpoints;

public sealed record RestoreRequest(bool? DismissReports, string? Reason);

public sealed record RemoveRequest(string? Reason);

public sealed record SchedulePurgeRequest(int? Days, string? Reason);

public sealed record NoteRequest(string? Note, string? Reason)
{
    public string? Text => Note ?? Reason;
}

public sealed record BulkRequest(Guid[]? Ids, string? Action, string? Reason, int? Days, bool? DismissReports);

public sealed record BanRequest(DateTimeOffset? Until, bool? Permanent, string? Reason);

public sealed record UnbanRequest(string? Reason);

public sealed record TrustRequest(string? Level, string? Reason);

public sealed record RolesRequest(bool? Moderator, string[]? Roles)
{
    public bool? IsModerator => Moderator ?? Roles?.Contains("moderator", StringComparer.OrdinalIgnoreCase);
}

public static class ModerationEndpoints
{
    public static void MapModeration(this RouteGroupBuilder api)
    {
        var mod = api.MapGroup("/mod")
            .WithTags("Moderation")
            .RequireAuthorization(AuthPolicies.Moderation, AuthPolicies.NotBanned)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        mod.MapGet("/queue", async (string? type, string? cursor, int? limit, ModerationQueries queries, CancellationToken ct) =>
                (await queries.QueueAsync(type, cursor, limit, ct)).ToHttpResult(Results.Ok))
            .WithName("ModQueue")
            .Produces<ModQueuePageDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        mod.MapGet("/audit", async (string? actor, Guid? target, string? cursor, int? limit, ModerationQueries queries, CancellationToken ct) =>
                (await queries.AuditAsync(actor, target, cursor, limit, ct)).ToHttpResult(Results.Ok))
            .WithName("ModAudit")
            .Produces<AuditPageDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        var items = mod.MapGroup("/items");

        items.MapPost("/bulk", async (BulkRequest body, HttpContext http, ModerationActions actions, CancellationToken ct) =>
                (await actions.BulkAsync(http.User.GetUserId(), body.Ids, body.Action, body.Reason, body.Days, body.DismissReports ?? true, ct))
                .ToHttpResult(Results.Ok))
            .WithName("ModBulkItems")
            .Produces<BulkResultDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        items.MapPost("/{id:guid}/restore", async (Guid id, [FromBody] RestoreRequest? body, HttpContext http, ModerationActions actions, CancellationToken ct) =>
                (await actions.RestoreAsync(http.User.GetUserId(), id, body?.DismissReports ?? true, body?.Reason, ct)).ToHttpResult(Results.Ok))
            .WithName("ModRestoreItem")
            .Produces<ModItemResultDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        items.MapPost("/{id:guid}/remove", async (Guid id, RemoveRequest body, HttpContext http, ModerationActions actions, CancellationToken ct) =>
                (await actions.RemoveAsync(http.User.GetUserId(), id, body.Reason, ct)).ToHttpResult(Results.Ok))
            .WithName("ModRemoveItem")
            .Produces<ModItemResultDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        items.MapPost("/{id:guid}/schedule-purge", async (Guid id, SchedulePurgeRequest body, HttpContext http, ModerationActions actions, CancellationToken ct) =>
                (await actions.SchedulePurgeAsync(http.User.GetUserId(), id, body.Days ?? 0, body.Reason, ct)).ToHttpResult(Results.Ok))
            .WithName("ModSchedulePurge")
            .Produces<ModItemResultDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        mod.MapPost("/reports/{id:guid}/dismiss", async (Guid id, [FromBody] NoteRequest? body, HttpContext http, ModerationActions actions, CancellationToken ct) =>
                (await actions.ResolveReportAsync(http.User.GetUserId(), id, uphold: false, body?.Text, ct)).ToHttpResult(Results.Ok))
            .WithName("ModDismissReport")
            .Produces<ModItemResultDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        mod.MapPost("/reports/{id:guid}/uphold", async (Guid id, [FromBody] NoteRequest? body, HttpContext http, ModerationActions actions, CancellationToken ct) =>
                (await actions.ResolveReportAsync(http.User.GetUserId(), id, uphold: true, body?.Text, ct)).ToHttpResult(Results.Ok))
            .WithName("ModUpholdReport")
            .Produces<ModItemResultDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        mod.MapPost("/flags/{id:guid}/resolve", async (Guid id, [FromBody] NoteRequest? body, HttpContext http, ModerationActions actions, CancellationToken ct) =>
                (await actions.ResolveFlagAsync(http.User.GetUserId(), id, body?.Text, ct)).ToHttpResult(Results.Ok))
            .WithName("ModResolveFlag")
            .Produces<ModItemResultDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var users = mod.MapGroup("/users");

        users.MapGet("", async (string? q, string? cursor, int? limit, ModerationQueries queries, CancellationToken ct) =>
                (await queries.UsersAsync(q, cursor, limit, ct)).ToHttpResult(Results.Ok))
            .WithName("ModUsers")
            .Produces<ModUserPageDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        users.MapPost("/{id:guid}/ban", async (Guid id, BanRequest body, HttpContext http, ModerationActions actions, CancellationToken ct) =>
                (await actions.BanAsync(http.User.GetUserId(), http.User.IsInRole("admin"), id, body.Until, body.Permanent ?? false, body.Reason, ct))
                .ToHttpResult(Results.Ok))
            .WithName("ModBanUser")
            .Produces<ModUserActionDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        users.MapPost("/{id:guid}/unban", async (Guid id, [FromBody] UnbanRequest? body, HttpContext http, ModerationActions actions, CancellationToken ct) =>
                (await actions.UnbanAsync(http.User.GetUserId(), id, body?.Reason, ct)).ToHttpResult(Results.Ok))
            .WithName("ModUnbanUser")
            .Produces<ModUserActionDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        users.MapPost("/{id:guid}/trust", async (Guid id, TrustRequest body, HttpContext http, ModerationActions actions, CancellationToken ct) =>
                (await actions.SetTrustAsync(http.User.GetUserId(), id, body.Level, body.Reason, ct)).ToHttpResult(Results.Ok))
            .WithName("ModSetTrust")
            .Produces<ModUserActionDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        users.MapPost("/{id:guid}/roles", async (Guid id, RolesRequest body, HttpContext http, ModerationActions actions, CancellationToken ct) =>
                body.IsModerator is null
                    ? Required("moderator")
                    : (await actions.SetModeratorAsync(http.User.GetUserId(), id, body.IsModerator.Value, ct)).ToHttpResult(Results.Ok))
            .WithName("ModSetRoles")
            .RequireAuthorization(AuthPolicies.Role("admin"))
            .Produces<ModUserActionDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static IResult Required(string field) =>
        CutWire.Studio.Domain.Common.Error.Validation("bad_request", $"{field} is required.",
            [new CutWire.Studio.Domain.Common.FieldViolation(field, "required", $"{field} is required.")]).ToHttpResult();
}
