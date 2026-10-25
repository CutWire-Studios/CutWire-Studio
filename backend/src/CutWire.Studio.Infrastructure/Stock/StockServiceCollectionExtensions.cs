using CutWire.Studio.Application.Stock;
using CutWire.Studio.Infrastructure.Jobs;
using CutWire.Studio.Infrastructure.Stock.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CutWire.Studio.Infrastructure.Stock;

public static class StockServiceCollectionExtensions
{
    public static IServiceCollection AddStock(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.AddOptions<StockOptions>().Bind(configuration.GetSection(StockOptions.Section));
        services.AddOptions<StockJobOptions>().Bind(configuration.GetSection(StockJobOptions.Section));
        services.AddOptions<StockMediaOptions>().Bind(configuration.GetSection(StockMediaOptions.Section));
        services.AddOptions<FfmpegOptions>().Bind(configuration.GetSection(FfmpegOptions.Section));
        services.AddOptions<SfxOptions>().Bind(configuration.GetSection(SfxOptions.Section));
        services.AddOptions<DriftAssetsOptions>().Bind(configuration.GetSection(DriftAssetsOptions.Section));
        services.AddOptions<StockExpiryJobOptions>().Bind(configuration.GetSection(StockExpiryJobOptions.Section));

        services.AddSingleton<IProviderCache, ProviderCache>();
        services.AddSingleton<IMediaSealer, MediaSealer>();
        services.AddSingleton<IMediaUrls, MediaUrls>();
        services.AddSingleton<IStockHttpClients, StockHttpClients>();
        services.AddSingleton<IMediaNormalizer, FfmpegNormalizer>();
        services.AddSingleton<DownloadQueue>();
        services.AddSingleton<IDownloadQueue>(sp => sp.GetRequiredService<DownloadQueue>());
        services.AddSingleton<FileSfxSource>();
        services.AddSingleton<ISfxSource>(sp => sp.GetRequiredService<FileSfxSource>());
        services.AddSingleton<DriftAssetsMirror>();
        services.AddSingleton<IRecurringJob, StockExpiryJob>();
        services.AddSingleton<IRecurringJob, DriftAssetsSyncJob>();
        services.AddHostedService<DownloadWorkerService>();
        services.AddHostedService<ResolveWorkerService>();

        PexelsRegistration.Register(services, configuration);
        UnsplashRegistration.Register(services, configuration);
        PixabayRegistration.Register(services, configuration);
        JamendoRegistration.Register(services, configuration);
        FreetouseRegistration.Register(services, configuration);
        GreenscreenRegistration.Register(services, configuration);
        YtdlpRegistration.Register(services, configuration);

        return services;
    }
}
