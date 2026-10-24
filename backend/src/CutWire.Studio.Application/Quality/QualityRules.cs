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
    IReadOnlyList<KindRulesDto> Kinds,
    TextRulesDto Text,
    EnglishWordListDto EnglishWordList);

/// <summary>Data the client needs to run the text gates without hard-coding it.</summary>
public sealed record TextRulesDto(
    int TitleSingleWordMinLength,
    int TitleAllCapsMinLetters,
    int TitleMaxIdenticalRun,
    int TitleMaxPunctuationRun,
    string LinkPattern,
    IReadOnlyList<string> TechTerms,
    IReadOnlyList<string> Profanity);

public sealed record EnglishWordListDto(string Url, string Sha256, int Bytes, int Words, int Bits, int HashCount, long Seed);

public sealed class QualityRulesProducer(ContentRulesOptions rules, IAssetKindRegistry kinds, BloomWordList english)
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
            k.MediaSpecs.Select(m => new MediaRuleDto(m.Role.ToKey(), m.Required, m.Width, m.Height, m.MaxBytes, MediaSpec.MimeType)).ToList())).ToList(),
        new TextRulesDto(
            rules.TitleSingleWordMinLength,
            rules.TitleAllCapsMinLetters,
            rules.TitleMaxIdenticalRun,
            rules.TitleMaxPunctuationRun,
            TextRules.LinkPatternSource,
            EnglishGate.TechTermList.Order(StringComparer.Ordinal).ToList(),
            ProfanityGate.Terms.Order(StringComparer.Ordinal).ToList()),
        new EnglishWordListDto(english.UrlPath, english.Sha256, english.Bytes.Length, (int)english.Filter.WordCount,
            (int)english.Filter.BitCount, english.Filter.HashCount, english.Filter.Seed));
}
