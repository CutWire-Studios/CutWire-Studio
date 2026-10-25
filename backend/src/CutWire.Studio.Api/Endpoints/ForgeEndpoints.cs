using System.Text.Json;
using CutWire.Studio.Api.Auth;
using CutWire.Studio.Api.Http;
using CutWire.Studio.Application.Forge;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Domain.Forge;

namespace CutWire.Studio.Api.Endpoints;

public sealed record CreateForgeDocumentRequest(string? LocalId, JsonElement Doc);

public sealed record UpdateForgeDocumentRequest(JsonElement Doc, int? BaseRevision);

public sealed record ForgeAssetDto(string Sha256, string Url);

public sealed record ForgeDocumentList(IReadOnlyList<ForgeDocSummaryDto> Documents);

public static class ForgeEndpoints
{
    public const string ClientHeader = "X-Forge-Client";

    public static string? ClientIdOf(this HttpContext http)
    {
        var value = http.Request.Headers[ClientHeader].ToString().Trim();
        return value.Length is > 0 and <= 64 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? value : null;
    }

    public static void MapForge(this RouteGroupBuilder api)
    {
        var forge = api.MapGroup("/forge").WithTags("Forge");
        var read = forge.MapGroup("").RequireAuthorization(AuthPolicies.Scope("forge:read"))
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden);
        var write = forge.MapGroup("").RequireAuthorization(AuthPolicies.Scope("forge:write"), AuthPolicies.NotBanned)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden);

        read.MapGet("/documents", async (HttpContext http, ForgeDocuments docs, CancellationToken ct) =>
                Results.Ok(new ForgeDocumentList(await docs.ListAsync(http.User.GetUserId(), ct))))
            .WithName("ListForgeDocuments")
            .Produces<ForgeDocumentList>();

        read.MapGet("/documents/{id:guid}", async (Guid id, HttpContext http, ForgeDocuments docs, CancellationToken ct) =>
                (await docs.GetAsync(http.User.GetUserId(), id, ct)).ToHttpResult(Results.Ok))
            .WithName("GetForgeDocument")
            .Produces<ForgeDocDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        read.MapGet("/documents/{id:guid}/events", async Task<IResult> (Guid id, HttpContext http, ForgeDocuments docs, IForgeEventHub hub,
                    IConfiguration configuration, CancellationToken ct) =>
                {
                    if (!await docs.ExistsAsync(http.User.GetUserId(), id, ct)) return Error.NotFound("document_not_found", "Document not found.").ToHttpResult();
                    var heartbeat = TimeSpan.FromSeconds(configuration.GetValue("Forge:SseHeartbeatSeconds", 25));
                    return (IResult)new ForgeEventStream(hub.Subscribe(id, ct), heartbeat);
                })
            .WithName("ForgeDocumentEvents")
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
            .ProducesProblem(StatusCodes.Status404NotFound);

        write.MapPost("/documents", async (CreateForgeDocumentRequest body, HttpContext http, ForgeDocuments docs, CancellationToken ct) =>
            {
                if (body.Doc.ValueKind != JsonValueKind.Object) return Error.Validation("document_invalid", "doc must be a ForgeDoc object.").ToHttpResult();
                var result = await docs.CreateAsync(http.User.GetUserId(), body.LocalId, body.Doc.GetRawText(), ct);
                return result.ToHttpResult(d => Results.Created($"/api/v1/forge/documents/{d.Id}", d));
            })
            .WithName("CreateForgeDocument")
            .Produces<ForgeDocDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);

        write.MapPut("/documents/{id:guid}", async (Guid id, UpdateForgeDocumentRequest body, HttpContext http, ForgeDocuments docs, CancellationToken ct) =>
            {
                if (body.Doc.ValueKind != JsonValueKind.Object) return Error.Validation("document_invalid", "doc must be a ForgeDoc object.").ToHttpResult();
                if (body.BaseRevision is not { } baseRevision) return Error.Validation("base_revision_required", "baseRevision is required.").ToHttpResult();
                var result = await docs.UpdateAsync(http.User.GetUserId(), id, body.Doc.GetRawText(), baseRevision, ForgeSources.Web, http.ClientIdOf(), ct);
                return result.ToHttpResult(Results.Ok);
            })
            .WithName("UpdateForgeDocument")
            .Produces<ForgeSavedDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);

        write.MapDelete("/documents/{id:guid}", async (Guid id, HttpContext http, ForgeDocuments docs, CancellationToken ct) =>
                (await docs.DeleteAsync(http.User.GetUserId(), id, ct)).ToHttpResult(_ => Results.NoContent()))
            .WithName("DeleteForgeDocument")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        write.MapPut("/documents/{id:guid}/thumb", async (Guid id, HttpContext http, ForgeDocuments docs, CancellationToken ct) =>
                (await docs.SetThumbAsync(http.User.GetUserId(), id, http.Request.Body, http.Request.ContentType, ct)).ToHttpResult(_ => Results.NoContent()))
            .WithName("SetForgeDocumentThumb")
            .Accepts<byte[]>("image/webp", "image/png")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);

        write.MapPost("/assets", async (HttpContext http, ForgeAssets assets, CancellationToken ct) =>
                (await assets.UploadAsync(http.Request.Body, http.Request.ContentType, ct))
                .ToHttpResult(b => Results.Ok(new ForgeAssetDto(b.Hash.Value, ItemUrl(b.Hash.Value)))))
            .WithName("UploadForgeAsset")
            .Accepts<byte[]>("image/*", "audio/*")
            .Produces<ForgeAssetDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);
    }

    private static string ItemUrl(string sha) => $"/files/{sha}";
}

/// <summary>A server-sent event stream: <c>updated</c> and <c>deleted</c> events, and a comment line as heartbeat.</summary>
internal sealed class ForgeEventStream(IAsyncEnumerable<ForgeDocEvent> events, TimeSpan heartbeat) : IResult
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task ExecuteAsync(HttpContext http)
    {
        var response = http.Response;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-store";
        response.Headers["X-Accel-Buffering"] = "no";
        var ct = http.RequestAborted;
        try
        {
            await response.WriteAsync(": connected\n\n", ct);
            await response.Body.FlushAsync(ct);

            await using var enumerator = events.GetAsyncEnumerator(ct);
            var next = enumerator.MoveNextAsync().AsTask();
            while (!ct.IsCancellationRequested)
            {
                using var tick = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var delay = Task.Delay(heartbeat, tick.Token);
                if (await Task.WhenAny(next, delay) == next)
                {
                    await tick.CancelAsync();
                    if (!await next) break;
                    await Write(response, enumerator.Current, ct);
                    next = enumerator.MoveNextAsync().AsTask();
                }
                else
                {
                    await response.WriteAsync(": heartbeat\n\n", ct);
                    await response.Body.FlushAsync(ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task Write(HttpResponse response, ForgeDocEvent e, CancellationToken ct)
    {
        var data = e.Type == "updated"
            ? JsonSerializer.Serialize(new { revision = e.Revision, updatedAt = e.UpdatedAt, source = e.Source, clientId = e.ClientId }, Json)
            : "{}";
        await response.WriteAsync($"event: {e.Type}\ndata: {data}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}
