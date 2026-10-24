using CutWire.Studio.Domain.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Infrastructure.Identity;

public sealed class BootstrapAdmin(
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole<Guid>> roles,
    IOptions<AuthOptions> options)
{
    public async Task EnsureAsync(ApplicationUser user)
    {
        var email = options.Value.BootstrapAdminEmail;
        if (string.IsNullOrWhiteSpace(email) || !user.EmailConfirmed ||
            !string.Equals(user.Email, email.Trim(), StringComparison.OrdinalIgnoreCase))
            return;

        if (!await roles.RoleExistsAsync(Roles.Admin))
            await roles.CreateAsync(new IdentityRole<Guid>(Roles.Admin) { Id = Guid.NewGuid() });
        if (await users.IsInRoleAsync(user, Roles.Admin)) return;
        await users.AddToRoleAsync(user, Roles.Admin);
        await users.UpdateSecurityStampAsync(user);
    }
}
