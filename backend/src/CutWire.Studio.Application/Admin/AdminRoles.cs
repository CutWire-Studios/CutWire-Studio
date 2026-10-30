using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Moderation;
using CutWire.Studio.Domain.Accounts;
using CutWire.Studio.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Application.Admin;

public sealed record StaffMemberDto(Guid Id, string Handle, string? Email, IReadOnlyList<string> Roles);

/// <summary>Grants and revokes the moderator and admin roles. Only reachable from the admin dashboard.</summary>
public sealed class AdminRoles(IAppDbContext db, IClock clock, IUserDirectory directory, AuditLog audit)
{
    private static Error Bad(string field, string code, string message) =>
        Error.Validation("bad_request", message, [new FieldViolation(field, code, message)]);

    public async Task<IReadOnlyList<StaffMemberDto>> ListAsync(CancellationToken ct)
    {
        var staff = await directory.ListStaffAsync(ct);
        var ids = staff.Select(s => s.UserId).ToList();
        var handles = await db.UserProfiles.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Handle, ct);
        return staff.Select(s => new StaffMemberDto(s.UserId, handles.GetValueOrDefault(s.UserId) ?? s.UserId.ToString(), s.Email, s.Roles))
            .OrderBy(s => s.Handle, StringComparer.Ordinal).ToList();
    }

    /// <summary>Resolves a handle or email, exact or prefix. Fails unless exactly one account matches (an exact match wins).</summary>
    public async Task<Result<Guid>> FindAsync(string? query, CancellationToken ct)
    {
        var needle = query?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(needle)) return Bad("query", "required", "Enter a handle or email.");

        var byHandle = await db.UserProfiles.AsNoTracking().Where(p => p.DeletedAt == null && p.Handle.StartsWith(needle))
            .OrderBy(p => p.Handle).Select(p => new { p.Id, p.Handle }).Take(5).ToListAsync(ct);
        if (byHandle.FirstOrDefault(h => h.Handle == needle) is { } exact) return exact.Id;

        var ids = byHandle.Select(h => h.Id).Union(await directory.FindByEmailAsync(needle, 5, ct)).ToList();
        return ids.Count switch
        {
            0 => Error.NotFound("user.not_found", "No user matches that handle or email."),
            1 => ids[0],
            _ => Bad("query", "ambiguous", "More than one user matches; be more specific."),
        };
    }

    public async Task<Result> SetAsync(Guid actor, Guid userId, string? role, bool granted, CancellationToken ct)
    {
        if (role is not (Roles.Moderator or Roles.Admin)) return Bad("role", "invalid_role", "Role must be moderator or admin.");
        if (userId == actor) return Error.Forbidden("user.self", "You cannot change your own roles.");

        var profile = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == userId, ct);
        if (profile is null) return Error.NotFound("user.not_found", "User not found.");
        var account = (await directory.GetAsync([userId], ct)).GetValueOrDefault(userId);
        if (account is null) return Error.NotFound("user.not_found", "User not found.");

        if (granted)
        {
            if (!account.EmailVerified) return Error.Conflict("user.email_unverified", "Only users with a confirmed email can hold a role.");
            if (profile.IsBanned(clock.UtcNow)) return Error.Conflict("user.banned", "A banned user cannot be granted a role.");
        }
        else if (role == Roles.Admin && account.Roles.Contains(Roles.Admin))
        {
            if (await directory.IsBootstrapAdminAsync(userId, ct))
                return Error.Forbidden("user.bootstrap_admin", "The bootstrap admin cannot lose the admin role.");
            if (await directory.CountInRoleAsync(Roles.Admin, ct) <= 1)
                return Error.Conflict("user.last_admin", "The last admin cannot lose the admin role.");
        }

        var result = await directory.SetRoleAsync(userId, role, granted, ct);
        if (result.IsFailure) return result.Error;
        audit.Record(actor, (granted ? "grant_" : "revoke_") + role, "user", userId);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
