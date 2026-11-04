using System.Text.Json;
using CutWire.Studio.Api.Auth;
using CutWire.Studio.Api.Http;
using CutWire.Studio.Application.Forge;
using CutWire.Studio.Application.Synth;
using CutWire.Studio.Domain.Accounts;
using CutWire.Studio.Domain.Common;

namespace CutWire.Studio.Api.Endpoints;

public sealed record CreateSynthSongRequest(string? LocalId, JsonElement Doc);

public sealed record UpdateSynthSongRequest(JsonElement Doc, int? BaseRevision);

public sealed record SynthSongList(IReadOnlyList<SynthSongSummaryDto> Songs);

public static class SynthEndpoints
{
    public static void MapSynth(this RouteGroupBuilder api)
    {
        var synth = api.MapGroup("/synth").WithTags("Synth");
        var read = synth.MapGroup("").RequireAuthorization(AuthPolicies.Scope(AuthScopes.SynthRead))
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden);
        var write = synth.MapGroup("").RequireAuthorization(AuthPolicies.Scope(AuthScopes.SynthWrite), AuthPolicies.NotBanned)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden);

        read.MapGet("/songs", async (HttpContext http, SynthSongs songs, CancellationToken ct) =>
                Results.Ok(new SynthSongList(await songs.ListAsync(http.User.GetUserId(), ct))))
            .WithName("ListSynthSongs")
            .Produces<SynthSongList>();

        read.MapGet("/songs/{id:guid}", async (Guid id, HttpContext http, SynthSongs songs, CancellationToken ct) =>
                (await songs.GetAsync(http.User.GetUserId(), id, ct)).ToHttpResult(Results.Ok))
            .WithName("GetSynthSong")
            .Produces<SynthSongDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        read.MapGet("/songs/{id:guid}/events", async Task<IResult> (Guid id, HttpContext http, SynthSongs songs, IForgeEventHub hub,
                    IConfiguration configuration, CancellationToken ct) =>
                {
                    if (!await songs.ExistsAsync(http.User.GetUserId(), id, ct)) return Error.NotFound("song_not_found", "Song not found.").ToHttpResult();
                    var heartbeat = TimeSpan.FromSeconds(configuration.GetValue("Forge:SseHeartbeatSeconds", 25));
                    return (IResult)new ForgeEventStream(hub.Subscribe(id, ct), heartbeat);
                })
            .WithName("SynthSongEvents")
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
            .ProducesProblem(StatusCodes.Status404NotFound);

        write.MapPost("/songs", async (CreateSynthSongRequest body, HttpContext http, SynthSongs songs, CancellationToken ct) =>
            {
                if (body.Doc.ValueKind != JsonValueKind.Object) return Error.Validation("song_invalid", "doc must be an object.").ToHttpResult();
                var result = await songs.CreateAsync(http.User.GetUserId(), body.LocalId, body.Doc.GetRawText(), ct);
                return result.ToHttpResult(s => Results.Created($"/api/v1/synth/songs/{s.Id}", s));
            })
            .WithName("CreateSynthSong")
            .Produces<SynthSongDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);

        write.MapPut("/songs/{id:guid}", async (Guid id, UpdateSynthSongRequest body, HttpContext http, SynthSongs songs, CancellationToken ct) =>
            {
                if (body.Doc.ValueKind != JsonValueKind.Object) return Error.Validation("song_invalid", "doc must be an object.").ToHttpResult();
                var result = await songs.UpdateAsync(http.User.GetUserId(), id, body.Doc.GetRawText(), body.BaseRevision, http.ClientIdOf(), ct);
                return result.ToHttpResult(Results.Ok);
            })
            .WithName("UpdateSynthSong")
            .Produces<SynthSongDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);

        write.MapDelete("/songs/{id:guid}", async (Guid id, HttpContext http, SynthSongs songs, CancellationToken ct) =>
                (await songs.DeleteAsync(http.User.GetUserId(), id, ct)).ToHttpResult(_ => Results.NoContent()))
            .WithName("DeleteSynthSong")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
