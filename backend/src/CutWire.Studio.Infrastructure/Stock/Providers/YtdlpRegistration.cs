using CutWire.Studio.Application.Stock;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CutWire.Studio.Infrastructure.Stock.Providers;

public static class YtdlpRegistration
{
    private static readonly string[] ResolveIds =
    [
        YtdlpHosts.YoutubeId, YtdlpHosts.YoutubeMusicId, YtdlpHosts.InstagramId, YtdlpHosts.FacebookId,
        YtdlpHosts.TikTokId, YtdlpHosts.VimeoId, YtdlpHosts.XId, YtdlpHosts.SoundCloudId,
    ];

    public static void Register(IServiceCollection services, IConfiguration config)
    {
        var options = config.GetSection(YtdlpOptions.Section).Get<YtdlpOptions>() ?? new YtdlpOptions();
        if (!options.Enabled) return;

        var binary = ExecutableLocator.Find(options.Path);
        if (binary is null)
        {
            var reason = $"yt-dlp not found (looked for {options.Path})";
            foreach (var id in ResolveIds)
            {
                var providerId = id;
                services.AddSingleton(new UnavailableProvider(providerId, reason, url => YtdlpHosts.Owns(providerId, url)));
            }
            return;
        }

        services.AddSingleton(_ => new YtdlpRunner(options, binary, new SystemProcessRunner()));
        AddProvider(services, r => new YoutubeVideoProvider(r));
        AddProvider(services, r => new YoutubeMusicProvider(r));
        AddProvider(services, SocialProvider.Instagram);
        AddProvider(services, SocialProvider.Facebook);
        AddProvider(services, SocialProvider.TikTok);
        AddProvider(services, SocialProvider.Vimeo);
        AddProvider(services, SocialProvider.X);
        AddProvider(services, SocialProvider.SoundCloud);
    }

    private static void AddProvider(IServiceCollection services, Func<YtdlpRunner, IStockProvider> create) =>
        services.AddSingleton(sp => create(sp.GetRequiredService<YtdlpRunner>()));
}
