using CutWire.Studio.Api.Http;
using CutWire.Studio.Application.Quality;
using Microsoft.Net.Http.Headers;

namespace CutWire.Studio.Api.Endpoints;

public static class MetaEndpoints
{
    public static void MapMeta(this RouteGroupBuilder api)
    {
        var meta = api.MapGroup("/meta").WithTags("Meta");

        meta.MapGet("/kinds", (HttpContext http, QualityRulesProducer producer) => http.JsonWithEtag(producer.Produce().Kinds))
            .WithName("GetKinds")
            .Produces<IReadOnlyList<KindRulesDto>>();

        meta.MapGet("/quality-rules", (HttpContext http, QualityRulesProducer producer) => http.JsonWithEtag(producer.Produce()))
            .WithName("GetQualityRules")
            .Produces<QualityRulesDto>();

        meta.MapGet("/{file}", (string file, HttpContext http, BloomWordList words) =>
            {
                if (file != $"english-{words.Hash8}.bloom") return Results.NotFound();
                http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                http.Response.Headers.XContentTypeOptions = "nosniff";
                return Results.File(words.Bytes, "application/octet-stream", entityTag: new EntityTagHeaderValue($"\"{words.Sha256}\""));
            })
            .WithName("GetEnglishBloom")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .Produces(StatusCodes.Status404NotFound)
            .DisableRateLimiting();
    }
}
