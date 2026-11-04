using System.Text.Json;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Forge;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Domain.Synth;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Application.Synth;

public sealed record SynthSongSummaryDto(Guid Id, string LocalId, string Name, int Revision, DateTimeOffset UpdatedAt);

public sealed record SynthSongDto(Guid Id, string LocalId, string Name, int Revision, DateTimeOffset UpdatedAt, JsonElement Doc);

/// <summary>Cloud songs for CutWire Synth. The document is opaque to Studio apart from its size and <c>name</c>; writes are revision-checked.</summary>
public sealed class SynthSongs(IAppDbContext db, IClock clock, IForgeEventHub events)
{
    public const string Source = "synth";

    public async Task<IReadOnlyList<SynthSongSummaryDto>> ListAsync(Guid userId, CancellationToken ct) =>
        await db.SynthSongs.AsNoTracking().Where(s => s.OwnerId == userId)
            .OrderByDescending(s => s.UpdatedAt)
            .Select(s => new SynthSongSummaryDto(s.Id, s.LocalId, s.Name, s.Revision, s.UpdatedAt))
            .ToListAsync(ct);

    public Task<bool> ExistsAsync(Guid userId, Guid id, CancellationToken ct) =>
        db.SynthSongs.AnyAsync(s => s.Id == id && s.OwnerId == userId, ct);

    public async Task<Result<SynthSongDto>> GetAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var row = await db.SynthSongs.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == userId, ct);
        return row is null ? NotFound() : ToDto(row);
    }

    public async Task<Result<SynthSongDto>> CreateAsync(Guid userId, string? localId, string docJson, CancellationToken ct)
    {
        localId = string.IsNullOrWhiteSpace(localId) ? Guid.NewGuid().ToString() : localId.Trim();
        if (localId.Length > SynthSong.MaxLocalIdLength)
            return Error.Validation("local_id_invalid", $"localId must be 1-{SynthSong.MaxLocalIdLength} characters.");
        var name = Inspect(docJson);
        if (name.IsFailure) return name.Error;

        var existing = await db.SynthSongs.AsNoTracking().Where(s => s.OwnerId == userId && s.LocalId == localId)
            .Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        if (existing is { } existingId) return Exists(existingId);
        if (await db.SynthSongs.CountAsync(s => s.OwnerId == userId, ct) >= SynthSong.MaxPerUser)
            return Error.Conflict("song_limit", $"You can keep at most {SynthSong.MaxPerUser} songs in the cloud.");

        var song = new SynthSong(userId, localId, name.Value, docJson, clock.UtcNow);
        db.SynthSongs.Add(song);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            var raced = await db.SynthSongs.AsNoTracking().Where(s => s.OwnerId == userId && s.LocalId == localId)
                .Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
            if (raced is { } id) return Exists(id);
            throw;
        }

        return ToDto(song);
    }

    public async Task<Result<SynthSongDto>> UpdateAsync(Guid userId, Guid id, string docJson, int? baseRevision, string? clientId,
        CancellationToken ct)
    {
        var name = Inspect(docJson);
        if (name.IsFailure) return name.Error;

        var now = clock.UtcNow;
        var changed = await db.SynthSongs
            .Where(s => s.Id == id && s.OwnerId == userId && (baseRevision == null || s.Revision == baseRevision))
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.DocJson, docJson)
                .SetProperty(s => s.Name, name.Value)
                .SetProperty(s => s.Revision, s => s.Revision + 1)
                .SetProperty(s => s.UpdatedAt, now), ct);
        var current = await db.SynthSongs.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == userId, ct);
        if (current is null) return NotFound();
        var dto = ToDto(current);
        if (changed == 0)
            return Error.Conflict("revision_conflict", "The song changed since you last loaded it.")
                .With("current", new { revision = dto.Revision, updatedAt = dto.UpdatedAt, doc = dto.Doc });

        events.Publish(id, new ForgeDocEvent("updated", dto.Revision, dto.UpdatedAt, Source, clientId, dto.Doc));
        return dto;
    }

    public async Task<Result<bool>> DeleteAsync(Guid userId, Guid id, CancellationToken ct)
    {
        if (await db.SynthSongs.Where(s => s.Id == id && s.OwnerId == userId).ExecuteDeleteAsync(ct) == 0) return NotFound();
        events.Publish(id, new ForgeDocEvent("deleted"));
        return true;
    }

    private static Result<string> Inspect(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > SynthSong.MaxDocBytes)
            return Error.TooLarge("song_too_large", $"A song can be at most {SynthSong.MaxDocBytes / 1024} KB.");
        JsonDocument parsed;
        try
        {
            parsed = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        }
        catch (JsonException)
        {
            return Error.Validation("song_invalid", "The song is not valid JSON.");
        }

        using (parsed)
        {
            if (parsed.RootElement.ValueKind != JsonValueKind.Object) return Error.Validation("song_invalid", "doc must be an object.");
            var name = parsed.RootElement.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()!.Trim() : "";
            if (name.Length == 0) name = "Untitled song";
            return name.Length > SynthSong.MaxNameLength ? name[..SynthSong.MaxNameLength] : name;
        }
    }

    private static SynthSongDto ToDto(SynthSong s)
    {
        using var parsed = JsonDocument.Parse(s.DocJson);
        return new SynthSongDto(s.Id, s.LocalId, s.Name, s.Revision, s.UpdatedAt, parsed.RootElement.Clone());
    }

    private static Error NotFound() => Error.NotFound("song_not_found", "Song not found.");

    private static Error Exists(Guid id) => Error.Conflict("song_exists", "A song with this localId already exists.").With("id", id);
}
