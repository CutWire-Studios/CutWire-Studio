using CutWire.Studio.Api.Http;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Domain.Common;

namespace CutWire.Studio.Api.Endpoints;

public sealed record HealthResponse(string Status);

public static class HealthEndpoints
{
    public static void MapHealth(this RouteGroupBuilder api) =>
        api.MapGet("/healthz", async (IAppDbContext db, HttpContext http, CancellationToken ct) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                return await db.CanConnectAsync(ct)
                    ? Results.Ok(new HealthResponse("ok"))
                    : Error.Unavailable("db.unavailable", "Database is unreachable.").ToHttpResult();
            })
            .WithName("GetHealth")
            .WithTags("Health")
            .Produces<HealthResponse>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .DisableRateLimiting();
}
