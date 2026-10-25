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

        services.AddScoped<Forge.ForgeDocuments>();
        services.AddScoped<Forge.ForgeAssets>();
        services.AddScoped<Forge.ForgeEditor>();
        services.AddScoped<Forge.ThemeDrafts>();
        services.AddScoped<Forge.HostedAgent>();
        services.AddSingleton<Forge.KeyedLocks<Guid>>();

        return services;
    }
}
