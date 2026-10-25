using System.Text.Json;
using System.Text.RegularExpressions;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Domain.Forge;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Application.Forge;

public sealed record ThemeDraftDto(string Id, string Name, JsonElement Doc, long UpdatedAt);

/// <summary>Owner-scoped Theme Maker drafts. The client owns the id and the timestamp; the newest timestamp wins.</summary>
public sealed partial class ThemeDrafts(IAppDbContext db)
{
    public async Task<IReadOnlyList<ThemeDraftDto>> ListAsync(Guid userId, CancellationToken ct)
    {
        var rows = await db.ThemeDrafts.AsNoTracking().Where(d => d.OwnerId == userId).OrderByDescending(d => d.UpdatedAtMs).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<Result<ThemeDraftDto>> GetAsync(Guid userId, string id, CancellationToken ct)
    {
        var row = await db.ThemeDrafts.AsNoTracking().FirstOrDefaultAsync(d => d.OwnerId == userId && d.Id == id, ct);
        return row is null ? Error.NotFound("draft_not_found", "Draft not found.") : ToDto(row);
    }

    public async Task<Result<ThemeDraftDto>> PutAsync(Guid userId, string id, string? name, JsonElement doc, long? updatedAt, CancellationToken ct)
    {
        if (id.Length is 0 or > ThemeDraft.MaxIdLength || !IdPattern().IsMatch(id))
            return Error.Validation("draft_id_invalid", "The draft id must be 1-64 letters, digits, '-' or '_'.");
        if (doc.ValueKind != JsonValueKind.Object) return Error.Validation("draft_invalid", "doc must be an object.");
        var json = doc.GetRawText();
        if (System.Text.Encoding.UTF8.GetByteCount(json) > ThemeDraft.MaxDocBytes)
            return Error.TooLarge("draft_too_large", "A draft can be at most 512 KB.");
        name = string.IsNullOrWhiteSpace(name) ? "Untitled" : name.Trim();
        if (name.Length > ThemeDraft.MaxNameLength) name = name[..ThemeDraft.MaxNameLength];
        var stamp = updatedAt ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var row = await db.ThemeDrafts.FirstOrDefaultAsync(d => d.OwnerId == userId && d.Id == id, ct);
        if (row is null)
        {
            if (await db.ThemeDrafts.CountAsync(d => d.OwnerId == userId, ct) >= ThemeDraft.MaxPerUser)
                return Error.Conflict("draft_limit", $"You can keep at most {ThemeDraft.MaxPerUser} drafts in the cloud.");
            row = new ThemeDraft(userId, id, name, json, stamp);
            db.ThemeDrafts.Add(row);
        }
        else if (stamp >= row.UpdatedAtMs)
            row.Replace(name, json, stamp);

        await db.SaveChangesAsync(ct);
        return ToDto(row);
    }

    public async Task<Result<bool>> DeleteAsync(Guid userId, string id, CancellationToken ct)
    {
        await db.ThemeDrafts.Where(d => d.OwnerId == userId && d.Id == id).ExecuteDeleteAsync(ct);
        return true;
    }

    private static ThemeDraftDto ToDto(ThemeDraft d)
    {
        using var parsed = JsonDocument.Parse(d.DocJson);
        return new ThemeDraftDto(d.Id, d.Name, parsed.RootElement.Clone(), d.UpdatedAtMs);
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex IdPattern();
}
