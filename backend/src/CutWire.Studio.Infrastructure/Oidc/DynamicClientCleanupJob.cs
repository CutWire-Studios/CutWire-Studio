using System.Text.Json;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Infrastructure.Jobs;
using CutWire.Studio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;

namespace CutWire.Studio.Infrastructure.Oidc;

public sealed class DynamicClientCleanupJob(IServiceScopeFactory scopes, ILogger<DynamicClientCleanupJob> logger) : IRecurringJob
{
    public static readonly TimeSpan MaxUnusedAge = TimeSpan.FromDays(7);

    public string Name => "dynamic-client-cleanup";
    public TimeSpan Interval => TimeSpan.FromHours(6);

    public async Task RunAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StudioDbContext>();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var cutoff = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow - MaxUnusedAge;
        var deleted = 0;

        var candidates = await db.Set<OpenIddictEntityFrameworkCoreApplication<Guid>>()
            .Where(a => a.Properties != null && a.Properties.Contains("\"" + OidcClients.DynamicProperty + "\""))
            .ToListAsync(ct);

        foreach (var app in candidates)
        {
            if (!TryGetCreatedAt(app.Properties!, out var createdAt) || createdAt > cutoff) continue;
            var used = await db.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().AnyAsync(a => a.Application!.Id == app.Id, ct)
                       || await db.Set<OpenIddictEntityFrameworkCoreToken<Guid>>().AnyAsync(t => t.Application!.Id == app.Id, ct);
            if (used) continue;

            await applications.DeleteAsync(app, ct);
            deleted++;
        }

        if (deleted > 0) logger.LogInformation("Removed {Count} unused dynamic OAuth clients", deleted);
    }

    private static bool TryGetCreatedAt(string properties, out DateTimeOffset createdAt)
    {
        createdAt = default;
        try
        {
            using var doc = JsonDocument.Parse(properties);
            return doc.RootElement.TryGetProperty(OidcClients.CreatedAtProperty, out var value) &&
                   value.TryGetDateTimeOffset(out createdAt);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
