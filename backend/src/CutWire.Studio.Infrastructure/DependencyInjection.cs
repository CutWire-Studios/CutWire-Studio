using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Catalog;
using CutWire.Studio.Application.Quality;
using CutWire.Studio.Infrastructure.Catalog;
using CutWire.Studio.Infrastructure.Blobs;
using CutWire.Studio.Infrastructure.Identity;
using CutWire.Studio.Infrastructure.Jobs;
using CutWire.Studio.Infrastructure.Persistence;
using CutWire.Studio.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace CutWire.Studio.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddSingleton(_ => NpgsqlDataSource.Create(
            configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.")));
        services.AddDbContext<StudioDbContext>((sp, o) => o
            .UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>())
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<StudioDbContext>());

        services.AddSingleton<IClock, SystemClock>();

        services.AddOptions<BlobStoreOptions>().Bind(configuration.GetSection(BlobStoreOptions.Section))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<IBlobStore, FileSystemBlobStore>();

        services.AddOptions<BlobGcOptions>().Bind(configuration.GetSection(BlobGcOptions.Section));
        services.AddSingleton<JobRunner>();
        services.AddSingleton<IRecurringJob, BlobGcJob>();
        if (configuration.GetValue("Jobs:Enabled", true))
            services.AddHostedService<RecurringJobHostedService>();

        services.AddOptions<DownloadRollupOptions>().Bind(configuration.GetSection(DownloadRollupOptions.Section));
        services.AddSingleton<IRecurringJob, DownloadRollupJob>();

        services.AddContentPipeline(configuration);
        services.AddSingleton<IItemTextSearch, PostgresItemTextSearch>();
        services.AddScoped<IDuplicateLookup, DuplicateLookup>();
        services.AddScoped<IUploadQuota, UploadQuota>();
        services.AddScoped<CatalogSeeder>();

        services.AddStudioIdentity(configuration, environment);

        return services;
    }
}
