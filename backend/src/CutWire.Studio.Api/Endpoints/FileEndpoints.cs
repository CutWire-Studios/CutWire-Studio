using CutWire.Studio.Api.Http;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Blobs;
using Microsoft.Net.Http.Headers;

namespace CutWire.Studio.Api.Endpoints;

public static class FileEndpoints
{
    public static void MapFiles(this IEndpointRouteBuilder app) =>
        app.MapMethods("/files/{sha256}", ["GET", "HEAD"], async (
                string sha256,
                IQueryHandler<GetBlobQuery, BlobContent> handler,
                HttpContext http,
                CancellationToken ct) =>
            {
                var result = await handler.Handle(new GetBlobQuery(sha256), ct);
                return result.ToHttpResult(blob =>
                {
                    http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                    http.Response.Headers.XContentTypeOptions = "nosniff";
                    return Results.File(
                        blob.Content,
                        blob.Mime,
                        lastModified: blob.CreatedAt,
                        entityTag: new EntityTagHeaderValue($"\"{blob.Hash}\""),
                        enableRangeProcessing: true);
                });
            })
            .WithName("GetFile")
            .WithTags("Files")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .Produces(StatusCodes.Status206PartialContent)
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .DisableRateLimiting();
}
