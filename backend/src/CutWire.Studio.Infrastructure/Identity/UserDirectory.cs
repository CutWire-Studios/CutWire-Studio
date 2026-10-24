using CutWire.Studio.Application.Moderation;
using CutWire.Studio.Domain.Accounts;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Infrastructure.Identity;

public sealed class UserDirectory(StudioDbContext db, UserManager<ApplicationUser> users) : IUserDirectory
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

    public async Task<Result> SetModeratorAsync(Guid userId, bool moderator, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return Error.NotFound("user.not_found", "User not found.");
        if (await users.IsInRoleAsync(user, Roles.Moderator) == moderator) return Result.Success();

        var result = moderator ? await users.AddToRoleAsync(user, Roles.Moderator) : await users.RemoveFromRoleAsync(user, Roles.Moderator);
        return result.Succeeded
            ? Result.Success()
            : Error.Unavailable("user.role_failed", string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
