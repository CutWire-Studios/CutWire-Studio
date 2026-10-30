using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CutWire.Studio.Api.Http;
using CutWire.Studio.Application.Stock;
using CutWire.Studio.Domain.Stock;
using CutWire.Studio.Infrastructure.Stock;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace CutWire.Studio.Api.Endpoints;

public sealed record StockDownloadRequest(string? ItemId, string? VariantId);

public sealed record StockResolveRequest(string? Url);

public static class StockEndpoints
{
    private static readonly string[] ReservedQuery = ["type", "provider", "q", "cursor", "limit"];

    /// <summary>Anonymous callers are identified by the install id (falling back to a hash of the IP); signed-in users also carry their account for per-account quota.</summary>
    public static StockCaller StockCallerOf(this HttpContext http)
    {
        var client = http.InstallIdOf() is { } install
            ? "i:" + install
            : "ip:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(http.Connection.RemoteIpAddress?.ToString() ?? "unknown")))[..24];
        Guid? user = http.User.Identity?.IsAuthenticated == true && Guid.TryParse(http.User.FindFirstValue(Claims.Subject), out var id) ? id : null;
        var distribution = http.Request.Headers[Distributions.Header].ToString().Trim().ToLowerInvariant();
        return new StockCaller(client, user, distribution.Length == 0 ? Distributions.Unknown : distribution);
    }

    public static void MapStock(this RouteGroupBuilder api)
    {
        var stock = api.MapGroup("/stock").WithTags("Stock");

        stock.MapGet("/catalog", async (HttpContext http, StockBrowse browse, CancellationToken ct) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                return Results.Ok(await browse.CatalogAsync(http.StockCallerOf(), ct));
            })
            .WithName("GetStockCatalog")
            .Produces<StockCatalogDto>();

        stock.MapGet("/search", async (HttpContext http, StockBrowse browse, string? type, string? provider, string? q, string? cursor, int? limit, CancellationToken ct) =>
            {
                var filters = http.Request.Query
                    .Where(kv => !ReservedQuery.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
                    .ToDictionary(kv => kv.Key, kv => kv.Value.ToString(), StringComparer.Ordinal);
                http.Response.Headers.CacheControl = "no-store";
                return (await browse.SearchAsync(http.StockCallerOf(), type, provider, q, cursor, limit, filters, ct)).ToHttpResult(Results.Ok);
            })
            .WithName("SearchStock")
            .Produces<StockPageDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        stock.MapGet("/items/{id}", async (string id, HttpContext http, StockBrowse browse, CancellationToken ct) =>
            (await browse.GetItemAsync(http.StockCallerOf(), id, ct)).ToHttpResult(Results.Ok))
            .WithName("GetStockItem")
            .Produces<StockItemDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        stock.MapPost("/resolve", async (StockResolveRequest body, HttpContext http, StockResolve resolve, CancellationToken ct) =>
            (await resolve.StartAsync(new CreateResolveCommand(http.StockCallerOf(), body.Url ?? ""), ct)).ToHttpResult(job => Results.Json(job, statusCode: StatusCodes.Status202Accepted)))
            .WithName("CreateStockResolve")
            .RequireRateLimiting(RateLimitingSettings.StockWritePolicy)
            .Produces<ResolveJobDto>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        stock.MapGet("/resolve/{id}", (string id, HttpContext http, StockResolve resolve) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                return resolve.Get(http.StockCallerOf(), id).ToHttpResult(Results.Ok);
            })
            .WithName("GetStockResolve")
            .Produces<ResolveJobDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        stock.MapPost("/downloads", async (StockDownloadRequest body, HttpContext http, DownloadJobs jobs, CancellationToken ct) =>
            (await jobs.CreateAsync(new CreateDownloadCommand(http.StockCallerOf(), body.ItemId ?? "", body.VariantId), ct))
            .ToHttpResult(job => Results.Json(job, statusCode: StatusCodes.Status201Created)))
            .WithName("CreateStockDownload")
            .RequireRateLimiting(RateLimitingSettings.StockWritePolicy)
            .Produces<DownloadJobDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        stock.MapGet("/downloads/{id}", async (string id, HttpContext http, DownloadJobs jobs, CancellationToken ct) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                return (await jobs.GetAsync(http.StockCallerOf(), id, ct)).ToHttpResult(Results.Ok);
            })
            .WithName("GetStockDownload")
            .Produces<DownloadJobDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        stock.MapGet("/sfx", async (HttpContext http, SfxCatalog sfx, CancellationToken ct) => http.JsonWithEtag(await sfx.GetAsync(ct)))
            .WithName("GetStockSfx")
            .Produces<SfxLibraryDto>();

        stock.MapGet("/drift-assets", async (HttpContext http, DriftAssetsCatalog assets, CancellationToken ct) => http.JsonWithEtag(await assets.GetAsync(ct)))
            .WithName("GetStockDriftAssets")
            .Produces<DriftAssetLibraryDto>();
    }

    /// <summary>Token-gated byte endpoints. The sealed token is the credential, so they skip the per-IP limiter (a grid of thumbnails would trip it).</summary>
    public static void MapStockMedia(this RouteGroupBuilder api)
    {
        var stock = api.MapGroup("/stock").WithTags("Stock");

        stock.MapMethods("/files/{token}", ["GET", "HEAD"], async (string token, StockFiles files, HttpContext http, CancellationToken ct) =>
            {
                var result = await files.OpenAsync(token, ct);
                return result.ToHttpResult(opened =>
                {
                    http.Response.Headers.CacheControl = "private, max-age=300";
                    http.Response.Headers.XContentTypeOptions = "nosniff";
                    return Results.File(opened.Content.Content, opened.Content.Mime, fileDownloadName: CatalogHttp.SafeDownloadName(opened.FileName),
                        lastModified: opened.Content.CreatedAt, entityTag: new EntityTagHeaderValue($"\"{opened.Content.Hash}\""), enableRangeProcessing: true);
                });
            })
            .WithName("GetStockFile")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .Produces(StatusCodes.Status206PartialContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .DisableRateLimiting();

        stock.MapMethods("/media/{token}", ["GET", "HEAD"], async (string token, HttpContext http, IMediaSealer sealer, IStockHttpClients clients,
                IOptions<StockJobOptions> jobs, CancellationToken ct) =>
            {
                var claims = sealer.Open(token);
                if (claims?.Source is null || !ProviderSupport.TryParseHttpUrl(claims.Source, out var origin)) return Results.NotFound();

                using var request = new HttpRequestMessage(HttpMethod.Get, origin);
                foreach (var header in new[] { "Range", "If-Range", "Accept" })
                    if (http.Request.Headers.TryGetValue(header, out var value)) request.Headers.TryAddWithoutValidation(header, value.ToArray());

                HttpResponseMessage response;
                try
                {
                    response = await clients.Public.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                }
                catch (HttpRequestException)
                {
                    return Results.StatusCode(StatusCodes.Status502BadGateway);
                }

                using (response)
                {
                    if ((int)response.StatusCode >= 400) return Results.StatusCode(StatusCodes.Status502BadGateway);
                    http.Response.StatusCode = (int)response.StatusCode;
                    var h = http.Response.Headers;
                    var contentType = claims.Mime ?? response.Content.Headers.ContentType?.ToString();
                    if (claims.Mime is null && contentType is not null && !contentType.StartsWith("application/octet-stream", StringComparison.OrdinalIgnoreCase)
                        && !SecurityHeadersMiddleware.IsInlineSafe(contentType))
                        return Results.StatusCode(StatusCodes.Status502BadGateway);
                    if (contentType is not null) http.Response.ContentType = contentType;
                    if (response.Content.Headers.ContentLength is { } length) http.Response.ContentLength = length;
                    foreach (var name in new[] { "Content-Range", "Accept-Ranges", "Last-Modified", "ETag" })
                        if (response.Headers.TryGetValues(name, out var v) || response.Content.Headers.TryGetValues(name, out v)) h[name] = v.ToArray();
                    var remaining = Math.Max(0, claims.Expires - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    h.CacheControl = remaining > 0 ? $"public, max-age={remaining}, immutable" : "public, max-age=0";
                    h.XContentTypeOptions = "nosniff";
                    if (HttpMethods.IsHead(http.Request.Method)) return Results.Empty;

                    await using var body = await response.Content.ReadAsStreamAsync(ct);
                    var buffer = new byte[81920];
                    long sent = 0;
                    int read;
                    while ((read = await body.ReadAsync(buffer, ct)) > 0 && sent < jobs.Value.MaxDownloadBytes)
                    {
                        sent += read;
                        await http.Response.Body.WriteAsync(buffer.AsMemory(0, read), ct);
                    }
                }

                return Results.Empty;
            })
            .WithName("GetStockMedia")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .Produces(StatusCodes.Status206PartialContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status502BadGateway)
            .DisableRateLimiting();

        stock.MapMethods("/sfx/files/{area}/{**path}", ["GET", "HEAD"], (string area, string path, FileSfxSource sfx, HttpContext http) =>
            {
                var file = sfx.Resolve(area, path);
                if (file is null) return Results.NotFound();
                http.Response.Headers.XContentTypeOptions = "nosniff";
                var preview = area == "previews";
                http.Response.Headers.CacheControl = preview ? "public, max-age=31536000, immutable" : "public, max-age=86400";
                return Results.File(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete), SfxContentType(file),
                    fileDownloadName: preview ? null : Path.GetFileName(file), lastModified: File.GetLastWriteTimeUtc(file), enableRangeProcessing: true);
            })
            .WithName("GetStockSfxFile")
            .Produces(StatusCodes.Status200OK, contentType: "audio/ogg", additionalContentTypes: "audio/flac")
            .Produces(StatusCodes.Status206PartialContent)
            .Produces(StatusCodes.Status404NotFound)
            .DisableRateLimiting();
    }

    private static string SfxContentType(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".flac" => "audio/flac",
        ".opus" or ".ogg" => "audio/ogg",
        ".webm" => "audio/webm",
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".m4a" => "audio/mp4",
        _ => "application/octet-stream",
    };
}
