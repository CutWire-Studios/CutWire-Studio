using CutWire.Studio.Domain.Accounts;
using CutWire.Studio.Infrastructure.Oidc;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;

namespace CutWire.Studio.Infrastructure.Identity;

public sealed class AuthSeeder(
    RoleManager<IdentityRole<Guid>> roles,
    UserManager<ApplicationUser> users,
    BootstrapAdmin bootstrapAdmin,
    IOpenIddictApplicationManager applications,
    IOpenIddictScopeManager scopes,
    PublicUrl publicUrl,
    IOptions<AuthOptions> options)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        foreach (var role in Roles.All)
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new IdentityRole<Guid>(role) { Id = Guid.NewGuid() });

        if (!string.IsNullOrWhiteSpace(options.Value.BootstrapAdminEmail) &&
            await users.FindByEmailAsync(options.Value.BootstrapAdminEmail.Trim()) is { } admin)
            await bootstrapAdmin.EnsureAsync(admin);

        foreach (var scope in OidcClients.ServerScopes)
        {
            var descriptor = new OpenIddictScopeDescriptor { Name = scope, DisplayName = scope };
            foreach (var resource in publicUrl.Resources) descriptor.Resources.Add(resource);
            if (await scopes.FindByNameAsync(scope, ct) is { } existing)
                await scopes.UpdateAsync(existing, descriptor, ct);
            else
                await scopes.CreateAsync(descriptor, ct);
        }

        await UpsertAsync(OidcClients.Drift(publicUrl.Resources), ct);
        await UpsertAsync(OidcClients.McpPlaceholder(publicUrl.Resources), ct);
    }

    private async Task UpsertAsync(OpenIddictApplicationDescriptor descriptor, CancellationToken ct)
    {
        if (await applications.FindByClientIdAsync(descriptor.ClientId!, ct) is { } existing)
            await applications.UpdateAsync(existing, descriptor, ct);
        else
            await applications.CreateAsync(descriptor, ct);
    }
}
