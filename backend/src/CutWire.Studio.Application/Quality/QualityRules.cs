using CutWire.Studio.Application.Catalog;
using CutWire.Studio.Domain.Catalog;

namespace CutWire.Studio.Application.Quality;

public sealed record LengthRuleDto(int Min, int Max);

public sealed record MediaRuleDto(string Role, bool Required, int Width, int Height, int MaxBytes, string MimeType);

public sealed record KindRulesDto(
    string Id,
    string DisplayName,
    IReadOnlyList<string> AcceptedExtensions,
    long MaxPackageBytes,
    long MaxUnpackedBytes,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Tags,
    IReadOnlyList<MediaRuleDto> Media);

public sealed record QualityRulesDto(
    LengthRuleDto Title,
    LengthRuleDto Summary,
    LengthRuleDto Description,
    int DescriptionMaxLinks,
    int MaxConsecutiveBlankLines,
    int MinTags,
    int MaxTags,
    IReadOnlyList<string> Licenses,
    double EnglishMinRatio,
    int EnglishMinTokens,
    double MinLumaStdDev,
    int SimilarThumbnailMaxDistance,
    IReadOnlyList<int> DailyUploadLimits,
    IReadOnlyList<KindRulesDto> Kinds);

public sealed class QualityRulesProducer(ContentRulesOptions rules, IAssetKindRegistry kinds)
{
    public QualityRulesDto Produce() => new(
        new LengthRuleDto(rules.TitleMinLength, rules.TitleMaxLength),
        new LengthRuleDto(rules.SummaryMinLength, rules.SummaryMaxLength),
        new LengthRuleDto(rules.DescriptionMinLength, rules.DescriptionMaxLength),
        rules.DescriptionMaxLinks,
        rules.MaxConsecutiveBlankLines,
        rules.MinTags,
        rules.MaxTags,
        rules.Licenses,
        rules.EnglishMinRatio,
        rules.EnglishMinTokens,
        rules.MinLumaStdDev,
        rules.SimilarThumbnailMaxDistance,
        rules.DailyUploadLimits,
        kinds.ListEnabled().Select(k => new KindRulesDto(
            k.Id.Value,
            k.DisplayName,
            k.AcceptedExtensions,
            k.MaxPackageBytes,
            k.MaxUnpackedBytes,
            k.Categories,
            k.TagVocabulary,
            k.MediaSpecs.Select(m => new MediaRuleDto(m.Role.ToKey(), m.Required, m.Width, m.Height, m.MaxBytes, MediaSpec.MimeType)).ToList())).ToList());
}
