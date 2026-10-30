using CutWire.Studio.Api.Http;
using CutWire.Studio.Application.Marketplace;

namespace CutWire.Studio.Api.Endpoints;

public static class MarketplaceEndpoints
{
    public static void MapMarketplace(this RouteGroupBuilder api)
    {
        var market = api.MapGroup("/marketplace").WithTags("Marketplace").RequireMarketplaceCaller();

        market.MapGet("", async (HttpContext http, MarketplaceManifest manifest, CancellationToken ct) =>
                http.JsonWithEtag(await manifest.BuildAsync(http.MarketplaceCallerOf()!.Distribution, http.MarketplaceCallerOf()!.AppVersion, ct),
                    "X-Cutwire-Distribution, X-Cutwire-App", "private, no-cache"))
            .WithName("GetMarketplaceManifest")
            .Produces<MarketplaceManifestDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}
