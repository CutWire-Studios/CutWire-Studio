namespace CutWire.Studio.Application.Quality;

public sealed class ContentRulesOptions
{
    public const string Section = "ContentRules";

    public int TitleMinLength { get; set; } = 6;
    public int TitleMaxLength { get; set; } = 60;
    public int TitleSingleWordMinLength { get; set; } = 4;
    public int TitleAllCapsMinLetters { get; set; } = 4;
    public int TitleMaxIdenticalRun { get; set; } = 3;
    public int TitleMaxPunctuationRun { get; set; } = 2;

    public int SummaryMinLength { get; set; } = 20;
    public int SummaryMaxLength { get; set; } = 140;
    public int DescriptionMinLength { get; set; } = 80;
    public int DescriptionMaxLength { get; set; } = 4000;
    public int DescriptionMaxLinks { get; set; } = 2;
    public int MaxConsecutiveBlankLines { get; set; } = 2;

    public double EnglishMinRatio { get; set; } = 0.75;
    public int EnglishMinTokens { get; set; } = 8;

    public int MinTags { get; set; } = 1;
    public int MaxTags { get; set; } = 5;
    public string[] Licenses { get; set; } = ["CC0-1.0", "CC-BY-4.0", "CC-BY-SA-4.0", "MIT", "Apache-2.0", "GPL-3.0-or-later"];

    public double MinLumaStdDev { get; set; } = 6;
    public int SimilarThumbnailMaxDistance { get; set; } = 6;

    /// <summary>Uploads per rolling day by trust level (index 0 is T0); 0 means unlimited. Levels past the end are unlimited.</summary>
    public int[] DailyUploadLimits { get; set; } = [3, 10, 30, 0];
}
