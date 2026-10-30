using CutWire.Studio.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace CutWire.Studio.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var handlerInterfaces = new[] { typeof(ICommandHandler<,>), typeof(IQueryHandler<,>) };
        var handlers = typeof(DependencyInjection).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false });

        foreach (var type in handlers)
            foreach (var iface in type.GetInterfaces())
                if (iface.IsGenericType && handlerInterfaces.Contains(iface.GetGenericTypeDefinition()))
                    services.AddScoped(iface, type);

        services.AddScoped<Catalog.BlobReferences>();
        services.AddScoped<Catalog.PackageIntake>();
        services.AddScoped<Moderation.AuditLog>();
        services.AddScoped<Moderation.AutoHideService>();
        services.AddScoped<Moderation.ItemRemoval>();
        services.AddScoped<Moderation.ModerationActions>();
        services.AddScoped<Moderation.ModerationQueries>();
        services.AddScoped<Moderation.ModerationMaintenance>();

        services.AddScoped<Admin.AdminRoles>();
        services.AddScoped<Admin.StockAdmin>();

        services.AddScoped<Forge.ForgeDocuments>();
        services.AddScoped<Forge.ForgeAssets>();
        services.AddScoped<Forge.ForgeEditor>();
        services.AddScoped<Forge.ThemeDrafts>();
        services.AddScoped<Forge.HostedAgent>();
        services.AddSingleton<Forge.KeyedLocks<Guid>>();

        services.AddSingleton<Stock.StockRegistry>();
        services.AddSingleton<Stock.StockAssembler>();
        services.AddSingleton<Stock.ResolveJobStore>();
        services.AddScoped<Stock.StockSettings>();
        services.AddScoped<Stock.StockQuota>();
        services.AddScoped<Stock.StockBrowse>();
        services.AddScoped<Stock.DownloadJobs>();
        services.AddScoped<Stock.DownloadRunner>();
        services.AddScoped<Stock.StockResolve>();
        services.AddScoped<Stock.StockFiles>();
        services.AddScoped<Stock.StockExpiry>();
        services.AddScoped<Stock.SfxCatalog>();
        services.AddScoped<Stock.DriftAssetsCatalog>();

        services.AddSingleton<Marketplace.ManifestCache>();
        services.AddScoped<Marketplace.MarketplaceManifest>();
        services.AddScoped<Marketplace.MarketplaceBrowse>();
        services.AddScoped<Marketplace.MarketplaceInstalls>();

        return services;
    }
}
