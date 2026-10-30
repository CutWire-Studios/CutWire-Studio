using CutWire.Studio.Application.Moderation;
using CutWire.Studio.Domain.Accounts;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Infrastructure.Identity;

public sealed class UserDirectory(StudioDbContext db, UserManager<ApplicationUser> users, RoleManager<IdentityRole<Guid>> roleManager,
    IOptions<AuthOptions> auth) : IUserDirectory
{
    public async Task<IReadOnlyDictionary<Guid, AccountInfo>> GetAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        var accounts = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.EmailConfirmed }).ToListAsync(ct);
        var roles = (await (from ur in db.Set<IdentityUserRole<Guid>>().AsNoTracking()
                            join r in db.Set<IdentityRole<Guid>>().AsNoTracking() on ur.RoleId equals r.Id
                            where userIds.Contains(ur.UserId)
                            select new { ur.UserId, r.Name }).ToListAsync(ct))
            .GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Name!).Order(StringComparer.Ordinal).ToList());
        return accounts.ToDictionary(a => a.Id, a => new AccountInfo(a.Id, a.EmailConfirmed, roles.GetValueOrDefault(a.Id) ?? []));
    }

    public Task<Result> SetModeratorAsync(Guid userId, bool moderator, CancellationToken ct) =>
        SetRoleAsync(userId, Roles.Moderator, moderator, ct);

    public async Task<Result> SetRoleAsync(Guid userId, string role, bool granted, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return Error.NotFound("user.not_found", "User not found.");
        if (await users.IsInRoleAsync(user, role) == granted) return Result.Success();

        if (granted && !await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole<Guid>(role) { Id = Guid.NewGuid() });
        var result = granted ? await users.AddToRoleAsync(user, role) : await users.RemoveFromRoleAsync(user, role);
        if (!result.Succeeded)
            return Error.Unavailable("user.role_failed", string.Join("; ", result.Errors.Select(e => e.Description)));
        await users.UpdateSecurityStampAsync(user);
        return Result.Success();
    }

    public async Task<IReadOnlyList<StaffAccount>> ListStaffAsync(CancellationToken ct)
    {
        var rows = await (from ur in db.Set<IdentityUserRole<Guid>>().AsNoTracking()
                          join r in db.Set<IdentityRole<Guid>>().AsNoTracking() on ur.RoleId equals r.Id
                          join u in db.Users.AsNoTracking() on ur.UserId equals u.Id
                          where r.Name == Roles.Moderator || r.Name == Roles.Admin
                          select new { u.Id, u.Email, Role = r.Name! }).ToListAsync(ct);
        return rows.GroupBy(x => x.Id)
            .Select(g => new StaffAccount(g.Key, g.First().Email, g.Select(x => x.Role).Order(StringComparer.Ordinal).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<Guid>> FindByEmailAsync(string email, int limit, CancellationToken ct)
    {
        var needle = users.NormalizeEmail(email.Trim());
        if (string.IsNullOrEmpty(needle)) return [];
        return await db.Users.AsNoTracking()
            .Where(u => u.NormalizedEmail != null && u.NormalizedEmail.StartsWith(needle))
            .OrderBy(u => u.NormalizedEmail).Select(u => u.Id).Take(limit).ToListAsync(ct);
    }

    public Task<int> CountInRoleAsync(string role, CancellationToken ct) =>
        (from ur in db.Set<IdentityUserRole<Guid>>().AsNoTracking()
         join r in db.Set<IdentityRole<Guid>>().AsNoTracking() on ur.RoleId equals r.Id
         where r.Name == role
         select ur.UserId).CountAsync(ct);

    public async Task<bool> IsBootstrapAdminAsync(Guid userId, CancellationToken ct)
    {
        var email = auth.Value.BootstrapAdminEmail?.Trim();
        if (string.IsNullOrEmpty(email)) return false;
        var normalized = users.NormalizeEmail(email);
        return await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.NormalizedEmail == normalized, ct);
    }
}
