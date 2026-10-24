using CutWire.Studio.Api.Auth;
using CutWire.Studio.Api.Http;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Catalog;
using CutWire.Studio.Application.Moderation;

namespace CutWire.Studio.Api.Endpoints;

public sealed record CreateItemRequest(string? Kind, string? Title, string? Summary, string? Description, string? Category, string[]? Tags, string? License);

public sealed record VoteRequest(int? Value);

public sealed record ReportRequest(string? Reason, string? Note);

public sealed record UpdateItemRequest(string? Title, string? Summary, string? Description, string? Category, string[]? Tags, string? License);

public static class ItemEndpoints
{
    public static void MapItems(this RouteGroupBuilder api)
    {
        var items = api.MapGroup("/items").WithTags("Items");

        items.MapGet("", async (HttpContext http, IQueryHandler<ListItemsQuery, ItemPageDto> handler, CancellationToken ct,
                string? kind, string? q, string? tags, string? category, string? sort, string? owner, string? status, string? cursor, int? limit, string? period) =>
            {
                var list = (tags ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var result = await handler.Handle(new ListItemsQuery(http.ViewerOf(), kind, q, list, category, sort, owner, status, cursor, limit, period), ct);
                return result.ToHttpResult(http.JsonWithEtag);
            })
            .WithName("ListItems")
            .Produces<ItemPageDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        items.MapGet("{id:guid}", async (Guid id, HttpContext http, IQueryHandler<GetItemQuery, ItemDetailDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetItemQuery(http.ViewerOf(), null, null, id), ct)).ToHttpResult(http.JsonWithEtag))
            .WithName("GetItemById")
            .Produces<ItemDetailDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        items.MapGet("{kind}/{slug}", async (string kind, string slug, HttpContext http, IQueryHandler<GetItemQuery, ItemDetailDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetItemQuery(http.ViewerOf(), kind, slug, null), ct)).ToHttpResult(http.JsonWithEtag))
            .WithName("GetItem")
            .Produces<ItemDetailDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        items.MapGet("{id:guid}/download", async (Guid id, string? version, HttpContext http, IConfiguration configuration,
                ICommandHandler<RecordDownloadCommand, DownloadTarget> handler, CancellationToken ct) =>
            {
                var viewer = http.ViewerOf();
                var result = await handler.Handle(new RecordDownloadCommand(id, version, viewer.UserId, http.InstallIdOf(), http.IpHashOf(configuration)), ct);
                http.Response.Headers.CacheControl = "no-store";
                return result.ToHttpResult(t => Results.Redirect($"/files/{t.Sha256}?name={Uri.EscapeDataString(t.FileName)}"));
            })
            .WithName("DownloadItem")
            .Produces(StatusCodes.Status302Found)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var write = items.MapGroup("")
            .RequireAuthorization(AuthPolicies.Scope("community:publish"), AuthPolicies.NotBanned)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        write.MapPost("", async (CreateItemRequest body, HttpContext http, ICommandHandler<CreateItemCommand, ItemDetailDto> handler, CancellationToken ct) =>
                (await handler.Handle(new CreateItemCommand(http.User.GetUserId(), body.Kind, body.Title, body.Summary, body.Description,
                    body.Category, body.Tags, body.License), ct))
                .ToHttpResult(item => Results.Created($"/api/v1/items/{item.Id}", item)))
            .WithName("CreateItem")
            .Produces<ItemDetailDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        write.MapPatch("{id:guid}", async (Guid id, UpdateItemRequest body, HttpContext http, ICommandHandler<UpdateItemCommand, ItemDetailDto> handler, CancellationToken ct) =>
                (await handler.Handle(new UpdateItemCommand(id, http.User.GetUserId(), body.Title, body.Summary, body.Description,
                    body.Category, body.Tags, body.License), ct)).ToHttpResult(Results.Ok))
            .WithName("UpdateItem")
            .Produces<ItemDetailDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        write.MapPut("{id:guid}/package", async (Guid id, HttpContext http, ICommandHandler<UploadPackageCommand, ItemDetailDto> handler, CancellationToken ct) =>
            {
                var upload = await http.ReadUploadAsync(ct);
                if (upload.IsFailure) return upload.Error.ToHttpResult();
                return (await handler.Handle(new UploadPackageCommand(id, http.User.GetUserId(), upload.Value.Content), ct)).ToHttpResult(Results.Ok);
            })
            .WithName("UploadItemPackage")
            .Accepts<byte[]>("application/octet-stream", "multipart/form-data")
            .Produces<ItemDetailDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);

        write.MapPut("{id:guid}/media/{role}", async (Guid id, string role, HttpContext http, ICommandHandler<UploadMediaCommand, ItemDetailDto> handler, CancellationToken ct) =>
            {
                var upload = await http.ReadUploadAsync(ct);
                if (upload.IsFailure) return upload.Error.ToHttpResult();
                return (await handler.Handle(new UploadMediaCommand(id, http.User.GetUserId(), role, upload.Value.Content), ct)).ToHttpResult(Results.Ok);
            })
            .WithName("UploadItemMedia")
            .Accepts<byte[]>("image/webp", "multipart/form-data")
            .Produces<ItemDetailDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        write.MapPost("{id:guid}/publish", async (Guid id, HttpContext http, ICommandHandler<PublishItemCommand, ItemDetailDto> handler, CancellationToken ct) =>
                (await handler.Handle(new PublishItemCommand(id, http.User.GetUserId()), ct)).ToHttpResult(Results.Ok))
            .WithName("PublishItem")
            .Produces<ItemDetailDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        write.MapPost("{id:guid}/versions", async (Guid id, string? changelog, HttpContext http, ICommandHandler<UploadVersionCommand, ItemDetailDto> handler, CancellationToken ct) =>
            {
                var upload = await http.ReadUploadAsync(ct);
                if (upload.IsFailure) return upload.Error.ToHttpResult();
                var notes = upload.Value.Form?["changelog"].ToString() is { Length: > 0 } field ? field : changelog;
                return (await handler.Handle(new UploadVersionCommand(id, http.User.GetUserId(), upload.Value.Content, notes), ct)).ToHttpResult(Results.Ok);
            })
            .WithName("UploadItemVersion")
            .Accepts<byte[]>("application/octet-stream", "multipart/form-data")
            .Produces<ItemDetailDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        write.MapPut("{id:guid}/vote", async (Guid id, VoteRequest body, HttpContext http, ICommandHandler<CastVoteCommand, VoteResultDto> handler, CancellationToken ct) =>
                (await handler.Handle(new CastVoteCommand(id, http.User.GetUserId(), body.Value ?? 0), ct)).ToHttpResult(Results.Ok))
            .WithName("VoteItem")
            .RequireRateLimiting(RateLimitingSettings.VotePolicy)
            .Produces<VoteResultDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        write.MapDelete("{id:guid}/vote", async (Guid id, HttpContext http, ICommandHandler<CastVoteCommand, VoteResultDto> handler, CancellationToken ct) =>
                (await handler.Handle(new CastVoteCommand(id, http.User.GetUserId(), null), ct)).ToHttpResult(Results.Ok))
            .WithName("ClearItemVote")
            .RequireRateLimiting(RateLimitingSettings.VotePolicy)
            .Produces<VoteResultDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        write.MapPost("{id:guid}/reports", async (Guid id, ReportRequest body, HttpContext http, ICommandHandler<CreateReportCommand, ReportDto> handler, CancellationToken ct) =>
                (await handler.Handle(new CreateReportCommand(id, http.User.GetUserId(), body.Reason, body.Note), ct))
                .ToHttpResult(r => Results.Created($"/api/v1/items/{id}", r)))
            .WithName("ReportItem")
            .RequireRateLimiting(RateLimitingSettings.ReportPolicy)
            .Produces<ReportDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        write.MapDelete("{id:guid}", async (Guid id, HttpContext http, ICommandHandler<DeleteItemCommand, Guid> handler, CancellationToken ct) =>
                (await handler.Handle(new DeleteItemCommand(id, http.User.GetUserId()), ct)).ToHttpResult(_ => Results.NoContent()))
            .WithName("DeleteItem")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    public static void MapMyItems(this RouteGroupBuilder api) =>
        api.MapGet("/me/items", async (string? status, string? cursor, int? limit, HttpContext http,
                IQueryHandler<ListMyItemsQuery, MyItemPageDto> handler, CancellationToken ct) =>
                (await handler.Handle(new ListMyItemsQuery(http.User.GetUserId(), status, cursor, limit), ct)).ToHttpResult(Results.Ok))
            .WithName("ListMyItems")
            .WithTags("Items")
            .RequireAuthorization(AuthPolicies.Scope("community:publish"))
            .Produces<MyItemPageDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

    public static void MapUsers(this RouteGroupBuilder api) =>
        api.MapGet("/users/{handle}", async (string handle, string? sort, string? cursor, int? limit, HttpContext http,
                IQueryHandler<GetUserProfileQuery, UserProfileDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetUserProfileQuery(http.ViewerOf(), handle, sort, cursor, limit), ct)).ToHttpResult(http.JsonWithEtag))
            .WithName("GetUser")
            .WithTags("Users")
            .Produces<UserProfileDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);
}
