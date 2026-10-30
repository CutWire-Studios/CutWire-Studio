using System.Text.RegularExpressions;

namespace CutWire.Studio.Application.Marketplace;

/// <summary>Vocabulary of the layout document. docs/marketplace-layout.schema.json publishes the same rules.</summary>
public static partial class LayoutDocument
{
    public const int MaxPages = 12;
    public const int MaxSections = 30;
    public const int MaxString = 200;
    public const int MaxText = 500;
    public const int MaxUrl = 500;
    public const int MaxLimit = 48;
    public const int DefaultLimit = 12;

    public const string Hero = "hero";
    public const string Notice = "notice";
    public const string Shelf = "shelf";
    public const string Grid = "grid";
    public const string Chips = "chips";
    public const string StockBrowser = "stock-browser";
    public const string SfxBrowser = "sfx-browser";
    public const string Downloads = "downloads";

    public static readonly string[] SectionTypes = [Hero, Notice, Shelf, Grid, Chips, StockBrowser, SfxBrowser, Downloads];
    public static readonly string[] SourceBound = [Shelf, Grid, Chips, StockBrowser, SfxBrowser];
    public static readonly string[] SourceKeys = ["id", "category", "q", "sort", "period", "provider", "filters"];
    public static readonly string[] Tones = ["info", "warning"];
    public static readonly string[] ActionTypes = ["open-page", "open-item", "open-url"];

    public static bool IsId(string value) => IdPattern().IsMatch(value);

    public static bool IsSourceId(string value) => SourcePattern().IsMatch(value);

    public static bool IsLanguage(string value) => LanguagePattern().IsMatch(value);

    [GeneratedRegex("^[a-z0-9-]{1,40}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^(drift-assets|sfx|stock\.[a-z0-9-]+|community\.[a-z0-9-]+)$")]
    private static partial Regex SourcePattern();

    [GeneratedRegex("^[A-Za-z]{2,3}([-_][A-Za-z0-9]{2,8})*$")]
    private static partial Regex LanguagePattern();
}
