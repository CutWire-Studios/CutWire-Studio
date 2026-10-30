using CutWire.Studio.Api.Http;
using CutWire.Studio.Application.Marketplace;
using CutWire.Studio.Infrastructure.Identity;

namespace CutWire.Studio.Api.Endpoints;

public sealed record MarketResolveRequest(string? Url);

public sealed record MarketInstallRequest(string? Source, string? ItemId, string? VariantId);

public static class MarketplaceEndpoints
{
    private static readonly string[] ReservedQuery = ["category", "q", "cursor", "limit", "sort", "period", "provider"];

    private static MarketCaller MarketCallerOf(this HttpContext http, PublicUrl publicUrl, IConfiguration configuration)
    {
        var caller = http.MarketplaceCallerOf()!;
        var distribution = MarketplaceManifest.Normalize(caller.Distribution);
        return new MarketCaller(distribution, caller.AppVersion, http.StockCallerOf() with { Distribution = distribution }, publicUrl.Base,
            http.InstallIdOf(), http.IpHashOf(configuration));
    }

    public static void MapMarketplace(this RouteGroupBuilder api)
    {
        var market = api.MapGroup("/marketplace").WithTags("Marketplace").RequireMarketplaceCaller();

        market.MapGet("", async (HttpContext http, MarketplaceManifest manifest, CancellationToken ct) =>
                http.JsonWithEtag(await manifest.BuildAsync(http.MarketplaceCallerOf()!.Distribution, http.MarketplaceCallerOf()!.AppVersion, ct),
                    "X-Cutwire-Distribution, X-Cutwire-App", "private, no-cache"))
            .WithName("GetMarketplaceManifest")
            .Produces<MarketplaceManifestDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        market.MapGet("/sources/{source}/items", async (string source, HttpContext http, MarketplaceBrowse browse, PublicUrl publicUrl, IConfiguration configuration,
                string? category, string? q, string? cursor, int? limit, string? sort, string? period, string? provider, CancellationToken ct) =>
            {
                var filters = http.Request.Query.Where(kv => !ReservedQuery.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
                    .ToDictionary(kv => kv.Key, kv => kv.Value.ToString(), StringComparer.Ordinal);
                http.Response.Headers.CacheControl = "no-store";
                return (await browse.ListAsync(http.MarketCallerOf(publicUrl, configuration), source,
                    new MarketQuery(category, q, cursor, limit, sort, period, provider, filters), ct)).ToHttpResult(Results.Ok);
            })
            .WithName("ListMarketplaceItems")
            .Produces<MarketItemPageDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        market.MapGet("/sources/{source}/items/{id}", async (string source, string id, HttpContext http, MarketplaceBrowse browse, PublicUrl publicUrl,
                IConfiguration configuration, CancellationToken ct) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                return (await browse.GetAsync(http.MarketCallerOf(publicUrl, configuration), source, id, ct)).ToHttpResult(Results.Ok);
            })
            .WithName("GetMarketplaceItem")
            .Produces<MarketItemDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        market.MapPost("/resolve", async (MarketResolveRequest body, HttpContext http, MarketplaceBrowse browse, PublicUrl publicUrl, IConfiguration configuration, CancellationToken ct) =>
            (await browse.StartResolveAsync(http.MarketCallerOf(publicUrl, configuration), body.Url, ct))
            .ToHttpResult(job => Results.Json(job, statusCode: StatusCodes.Status202Accepted)))
            .WithName("CreateMarketplaceResolve")
            .RequireRateLimiting(RateLimitingSettings.StockWritePolicy)
            .Produces<MarketResolveJobDto>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        market.MapGet("/resolve/{id}", (string id, HttpContext http, MarketplaceBrowse browse, PublicUrl publicUrl, IConfiguration configuration) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                return browse.GetResolve(http.MarketCallerOf(publicUrl, configuration), id).ToHttpResult(Results.Ok);
            })
            .WithName("GetMarketplaceResolve")
            .Produces<MarketResolveJobDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        market.MapPost("/installs", async (MarketInstallRequest body, HttpContext http, MarketplaceInstalls installs, PublicUrl publicUrl, IConfiguration configuration, CancellationToken ct) =>
            (await installs.CreateAsync(http.MarketCallerOf(publicUrl, configuration), new MarketInstallCommand(body.Source, body.ItemId, body.VariantId), ct))
            .ToHttpResult(job => Results.Json(job, statusCode: StatusCodes.Status201Created)))
            .WithName("CreateMarketplaceInstall")
            .RequireRateLimiting(RateLimitingSettings.StockWritePolicy)
            .Produces<InstallJobDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        market.MapGet("/installs/{id}", async (string id, HttpContext http, MarketplaceInstalls installs, PublicUrl publicUrl, IConfiguration configuration, CancellationToken ct) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                return (await installs.GetAsync(http.MarketCallerOf(publicUrl, configuration), id, ct)).ToHttpResult(Results.Ok);
            })
            .WithName("GetMarketplaceInstall")
            .Produces<InstallJobDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
