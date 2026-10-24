using CutWire.Studio.Application.Catalog;
using CutWire.Studio.Application.Quality;
using CutWire.Studio.Infrastructure.Catalog;
using CutWire.Studio.Infrastructure.Quality;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Infrastructure;

public static class ContentPipelineServiceCollectionExtensions
{
    /// <remarks>
    /// <see cref="IDuplicateLookup"/> and <see cref="IUploadQuota"/> need the database and are registered separately.
    /// </remarks>
    public static IServiceCollection AddContentPipeline(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ContentRulesOptions>().Bind(configuration.GetSection(ContentRulesOptions.Section));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<ContentRulesOptions>>().Value);

        services.AddSingleton<IAssetKind, EffectKind>();
        services.AddSingleton<IAssetKind, TransitionKind>();
        services.AddSingleton<IAssetKind, AudioEffectKind>();
        services.AddSingleton<IAssetKind, ThemeKind>();
        services.AddSingleton<IAssetKind, LookKind>();
        services.AddSingleton<IAssetKind, EffectTemplateKind>();
        services.AddSingleton<IAssetKind, LanguagePackKind>();
        services.AddSingleton<IAssetKindRegistry, AssetKindRegistry>();

        services.AddSingleton<IImageAnalyzer, SkiaImageAnalyzer>();
        services.AddSingleton<IWordList, EmbeddedWordList>();
        services.AddSingleton<IContentGate, TitleGate>();
        services.AddSingleton<IContentGate, TextGate>();
        services.AddSingleton<IContentGate, EnglishGate>();
        services.AddSingleton<IContentGate, ProfanityGate>();
        services.AddSingleton<IContentGate, TaxonomyGate>();
        services.AddSingleton<IContentGate, MediaGate>();
        services.AddScoped<IContentGate, DuplicateGate>();
        services.AddScoped<IContentGate, UploadQuotaGate>();
        services.AddScoped<ContentGatePipeline>();
        services.AddSingleton<QualityRulesProducer>();
        return services;
    }
}
