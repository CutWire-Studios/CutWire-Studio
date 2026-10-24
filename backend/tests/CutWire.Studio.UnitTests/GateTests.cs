using CutWire.Studio.Application.Catalog;
using CutWire.Studio.Application.Quality;
using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Infrastructure.Quality;
using Shouldly;
using SkiaSharp;

namespace CutWire.Studio.UnitTests;

internal static class GateAssert
{
    public static string[] Codes(this IReadOnlyList<GateViolation> violations) => violations.Select(v => v.Code).ToArray();
}

public class TitleGateTests
{
    private static readonly TitleGate Gate = new(new ContentRulesOptions(), Drafts.Words);

    private static async Task<string[]> Run(string title) =>
        (await Gate.CheckAsync(Drafts.Valid() with { Title = title }, TestContext.Current.CancellationToken)).Codes();

    [Theory]
    [InlineData("Dreamy Glow")]
    [InlineData("Retro VHS Look 2")]
    [InlineData("Sunset")]
    [InlineData("Glitch - Hard Cut")]
    public async Task GoodTitles_Pass(string title) => (await Run(title)).ShouldBeEmpty();

    [Theory]
    [InlineData("", "required")]
    [InlineData("Glow", "too_short")]
    [InlineData("Ab Cd", "too_short")]
    [InlineData("Dreamy Glow With Extra Bloom And Many More Words To Overflow The Limit", "too_long")]
    [InlineData("Dreamy Glow ✨", "non_ascii")]
    [InlineData("Café Glow", "non_ascii")]
    [InlineData(" Dreamy Glow", "bad_whitespace")]
    [InlineData("Dreamy Glow ", "bad_whitespace")]
    [InlineData("Dreamy  Glow", "bad_whitespace")]
    [InlineData("Zzxqvw", "needs_words")]
    [InlineData("DREAMY GLOW", "all_caps")]
    [InlineData("Glow www.example.com", "contains_url")]
    [InlineData("Glow example.io", "contains_url")]
    [InlineData("https://x.y Glow", "contains_url")]
    [InlineData("Dreamy Glowwww", "repeated_chars")]
    [InlineData("Aaaa Glow", "repeated_chars")]
    [InlineData("Dreamy Glow!!!", "repeated_punctuation")]
    [InlineData("Dreamy Glow...", "repeated_punctuation")]
    public async Task BadTitles_AreRejected(string title, string code) => (await Run(title)).ShouldContain(code);

    [Fact]
    public async Task ShortAllCapsAcronym_IsNotAllCaps() => (await Run("VHS 90")).ShouldNotContain("all_caps");

    [Fact]
    public async Task TwoPunctuationMarks_AreFine() => (await Run("Dreamy Glow!!")).ShouldBeEmpty();
}

public class TextGateTests
{
    private static readonly TextGate Gate = new(new ContentRulesOptions());

    private static async Task<IReadOnlyList<GateViolation>> Run(string? summary = null, string? description = null) =>
        await Gate.CheckAsync(Drafts.Valid() with { Summary = summary ?? Drafts.Summary, Description = description ?? Drafts.Description }, TestContext.Current.CancellationToken);

    [Fact]
    public async Task GoodText_Passes() => (await Run()).ShouldBeEmpty();

    [Fact]
    public async Task NewlinesAndCrLf_AreAllowedInDescription() =>
        (await Run(description: Drafts.Description.Replace(". ", ".\r\n\r\n"))).ShouldBeEmpty();

    [Theory]
    [InlineData("Short summary", "too_short")]
    [InlineData("", "required")]
    public async Task SummaryLength(string summary, string code) => (await Run(summary)).Codes().ShouldContain(code);

    [Fact]
    public async Task SummaryOver140_IsRejected() => (await Run(new string('a', 141))).Codes().ShouldContain("too_long");

    [Fact]
    public async Task DescriptionLengthLimits()
    {
        (await Run(description: "Too short to describe anything useful.")).Codes().ShouldContain("too_short");
        (await Run(description: new string('a', 4001))).Codes().ShouldContain("too_long");
        (await Run(description: new string('a', 4000))).Codes().ShouldNotContain("too_long");
    }

    [Fact]
    public async Task EmojiIsReported_WithIndex()
    {
        var violations = await Run(description: Drafts.Description + " Enjoy \U0001F600");

        var v = violations.Single(x => x.Code == "non_ascii");
        v.Field.ShouldBe("description");
        v.Params!["index"].ShouldBe(Drafts.Description.Length + " Enjoy ".Length);
        v.Params["char"].ShouldBe("U+1F600");
    }

    [Theory]
    [InlineData("こんにちは this is a japanese greeting inside")]
    [InlineData("Tab\tcharacter in the summary of the item right here")]
    public async Task NonPrintableOrNonLatin_InSummary_IsRejected(string summary) =>
        (await Run(summary)).Codes().ShouldContain("non_ascii");

    [Fact]
    public async Task LinksInSummary_AreRejected() =>
        (await Run("See https://example.com for the glow effect details")).Codes().ShouldContain("links_not_allowed");

    [Fact]
    public async Task DescriptionAllowsTwoLinksButNotThree()
    {
        (await Run(description: Drafts.Description + " See https://a.com and www.b.org")).Codes().ShouldNotContain("too_many_links");
        (await Run(description: Drafts.Description + " See https://a.com and www.b.org and c.net")).Codes().ShouldContain("too_many_links");
    }

    [Fact]
    public async Task BlankLines_AreCapped()
    {
        (await Run(description: Drafts.Description + "\n\n\nMore text")).Codes().ShouldNotContain("too_many_blank_lines");
        (await Run(description: Drafts.Description + "\n\n\n\nMore text")).Codes().ShouldContain("too_many_blank_lines");
        (await Run(description: Drafts.Description + "\n \n\t\n \nMore text")).Codes().ShouldContain("too_many_blank_lines");
    }
}

public class EnglishGateTests
{
    private static readonly EnglishGate Gate = new(new ContentRulesOptions(), Drafts.Words);

    private static async Task<IReadOnlyList<GateViolation>> Run(string summary, string description, params string[] tags) =>
        await Gate.CheckAsync(Drafts.Valid() with { Summary = summary, Description = description, Tags = tags }, TestContext.Current.CancellationToken);

    [Fact]
    public async Task EnglishText_Passes() => (await Run(Drafts.Summary, Drafts.Description)).ShouldBeEmpty();

    [Fact]
    public async Task OtherLanguageLatinText_IsRejected()
    {
        var violations = await Run("Este efecto agrega un brillo suave", "Usa el control deslizante para ajustar la intensidad del resplandor en las partes mas brillantes del video.");

        violations.Codes().ShouldBe(["not_english"]);
    }

    [Fact]
    public async Task GibberishIsRejected() =>
        (await Run("Qwrtp zxcvb mnbvc lkjhg", "Asdfg hjklq wertyu zxcvbn poiuyt lkjhgf mnbvcx qazwsx edcrfv tgbyhn ujmik olp plmokn ijbuhv")).Codes().ShouldBe(["not_english"]);

    [Fact]
    public async Task TooFewTokens_SkipsTheCheck() => (await Run("Qwrtp zxcvb", "Asdfg hjklq")).ShouldBeEmpty();

    [Fact]
    public async Task TechnicalTerms_AreIgnored() =>
        (await Run("Bokeh LUT GLSL VHS RGB chroma fps shader", "A bokeh lut glsl vhs rgb hsv chroma fps with 24 and 60 and 4k values 1920 for the shader.")).ShouldBeEmpty();

    [Fact]
    public async Task TagWords_AreIgnored()
    {
        const string summary = "Zorblax quuxify frobnitz wibble flarb gleep snarf";
        const string description = "A zorblax quuxify frobnitz wibble flarb gleep snarf pack for clips.";

        (await Run(summary, description)).Codes().ShouldBe(["not_english"]);
        (await Run(summary, description, "zorblax", "quuxify", "frobnitz", "wibble", "flarb", "gleep", "snarf")).ShouldBeEmpty();
    }

    [Fact]
    public async Task TitleWordsUsedInDescription_AreIgnored()
    {
        var draft = Drafts.Valid() with { Title = "Zorblax Quuxify", Summary = "Zorblax quuxify frobnitz wibble flarb gleep snarf", Description = "Zorblax quuxify frobnitz wibble flarb gleep snarf pack for clips." };

        (await Gate.CheckAsync(draft, TestContext.Current.CancellationToken)).Codes().ShouldBe(["not_english"]);
        (await Gate.CheckAsync(draft with { Summary = "Zorblax quuxify pack", Description = "Zorblax and quuxify with the nice soft glow around highlights in your clips." }, TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public void SuffixNormalisation_FindsInflectedForms()
    {
        var words = new SetWordList(["glow", "bake", "stop", "carry", "quick"]);

        foreach (var w in new[] { "glows", "glowing", "glowed", "glowly", "glower", "glowest", "baking", "baked", "stopped", "stopping", "carries", "carried", "quickly" })
            EnglishGate.IsKnown(words, w).ShouldBeTrue(w);
        EnglishGate.IsKnown(words, "zorblax").ShouldBeFalse();
    }

    [Fact]
    public void BundledWordList_KnowsCommonWords()
    {
        foreach (var w in new[] { "the", "glow", "highlights", "slider", "transition", "color" })
            EnglishGate.IsKnown(Drafts.Words, w).ShouldBeTrue(w);
        EnglishGate.IsKnown(Drafts.Words, "zorblax").ShouldBeFalse();
    }

    [Fact]
    public async Task RatioIsExactlyAtThreshold_Passes()
    {
        var gate = new EnglishGate(new ContentRulesOptions { EnglishMinRatio = 0.75, EnglishMinTokens = 4 }, new SetWordList(["one", "two", "three"]));

        (await gate.CheckAsync(Drafts.Valid() with { Summary = "one two three zzz", Description = "" }, TestContext.Current.CancellationToken)).ShouldBeEmpty();
        (await gate.CheckAsync(Drafts.Valid() with { Summary = "one two zzz yyy", Description = "" }, TestContext.Current.CancellationToken)).Codes().ShouldBe(["not_english"]);
    }
}

public class ProfanityGateTests
{
    private static readonly ProfanityGate Gate = new();

    [Theory]
    [InlineData("title", "What the fuck effect")]
    [InlineData("title", "Sh1t Glow")]
    [InlineData("title", "B1tch Glow")]
    [InlineData("summary", "This is total $h!t for clips")]
    [InlineData("summary", "This is total sh1t for clips")]
    [InlineData("description", "A glow that is f4ck you level of bright, fuuu")]
    [InlineData("tags", "a55hole")]
    public async Task Profanity_IsRejected_PerField(string field, string text)
    {
        var draft = Drafts.Valid();
        draft = field switch
        {
            "title" => draft with { Title = text },
            "summary" => draft with { Summary = text },
            "description" => draft with { Description = text },
            _ => draft with { Tags = [text] },
        };

        var violations = await Gate.CheckAsync(draft, TestContext.Current.CancellationToken);

        if (text.Contains("fuuu") || text.Contains("$h!t"))
        {
            violations.ShouldBeEmpty("not in the denylist");
            return;
        }

        violations.Select(v => v.Field).ShouldContain(field);
        violations.Codes().ShouldAllBe(c => c == "profanity");
    }

    [Theory]
    [InlineData("Glass Class Assess")]
    [InlineData("Scunthorpe Cockpit Shitake Passage")]
    [InlineData("Analysis of an assistant")]
    public async Task InnocentWordsContainingBadSubstrings_Pass(string title) =>
        (await Gate.CheckAsync(Drafts.Valid() with { Title = title }, TestContext.Current.CancellationToken)).ShouldBeEmpty();

    [Fact]
    public async Task LeetMapping_AppliesToAllDigitsAndSymbols()
    {
        ProfanityGate.ContainsProfanity("5h1t").ShouldBeTrue();
        ProfanityGate.ContainsProfanity("b!tch").ShouldBeFalse();
        ProfanityGate.ContainsProfanity("@$$h0le").ShouldBeTrue();
        ProfanityGate.ContainsProfanity("@55h0le").ShouldBeTrue();
        ProfanityGate.ContainsProfanity("f0ck").ShouldBeFalse();
        ProfanityGate.ContainsProfanity("fu(k").ShouldBeFalse();
        ProfanityGate.ContainsProfanity("c0ck").ShouldBeTrue();
        ProfanityGate.ContainsProfanity("clean words only").ShouldBeFalse();
        (await Gate.CheckAsync(Drafts.Valid(), TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }
}

public class TaxonomyGateTests
{
    private static readonly TaxonomyGate Gate = new(new ContentRulesOptions(), Kinds.Registry);

    private static async Task<IReadOnlyList<GateViolation>> Run(Func<SubmissionDraft, SubmissionDraft> change) =>
        await Gate.CheckAsync(change(Drafts.Valid()), TestContext.Current.CancellationToken);

    [Fact]
    public async Task ValidTaxonomy_Passes() => (await Run(d => d)).ShouldBeEmpty();

    [Fact]
    public async Task Category_MustBelongToTheKind()
    {
        (await Run(d => d with { Category = "" })).Codes().ShouldBe(["required"]);
        (await Run(d => d with { Category = "basic" })).Codes().ShouldBe(["invalid_category"]);
        (await Run(d => d with { Category = "basic", Kind = AssetKindId.Transition })).Codes().ShouldNotContain("invalid_category");
    }

    [Fact]
    public async Task TagCount_IsOneToFive()
    {
        (await Run(d => d with { Tags = [] })).Codes().ShouldBe(["too_few_tags"]);
        (await Run(d => d with { Tags = ["glow", "blur", "glitch", "retro", "film", "grain"] })).Codes().ShouldBe(["too_many_tags"]);
        (await Run(d => d with { Tags = ["glow", "blur", "glitch", "retro", "film"] })).ShouldBeEmpty();
    }

    [Fact]
    public async Task Tags_MustBeInTheVocabulary_AndUnique()
    {
        (await Run(d => d with { Tags = ["glow", "not-a-tag"] })).Codes().ShouldBe(["unknown_tag"]);
        (await Run(d => d with { Tags = ["Glow"] })).Codes().ShouldBe(["unknown_tag"]);
        (await Run(d => d with { Tags = ["glow", "glow"] })).Codes().ShouldBe(["duplicate_tag"]);
        (await Run(d => d with { Tags = ["glow", "retro"], Kind = AssetKindId.Transition })).Codes().ShouldContain("unknown_tag");
    }

    [Theory]
    [InlineData("CC0-1.0", true)]
    [InlineData("CC-BY-4.0", true)]
    [InlineData("CC-BY-SA-4.0", true)]
    [InlineData("MIT", true)]
    [InlineData("Apache-2.0", true)]
    [InlineData("GPL-3.0-or-later", true)]
    [InlineData("GPL-3.0", false)]
    [InlineData("Proprietary", false)]
    [InlineData("", false)]
    public async Task License_MustBeInTheSpdxSubset(string license, bool ok) =>
        (await Run(d => d with { License = license })).Count.ShouldBe(ok ? 0 : 1);

    [Fact]
    public async Task UnknownOrDisabledKind_IsRejected()
    {
        (await Run(d => d with { Kind = new AssetKindId("nope") })).Codes().ShouldBe(["unknown_kind"]);
        (await Run(d => d with { Kind = AssetKindId.LanguagePack })).Codes().ShouldBe(["unknown_kind"]);
    }
}

public class MediaGateTests
{
    private static readonly MediaGate Gate = new(new ContentRulesOptions(), Kinds.Registry, Drafts.Analyzer);

    private static async Task<IReadOnlyList<GateViolation>> Run(SubmissionDraft draft) => await Gate.CheckAsync(draft, TestContext.Current.CancellationToken);

    private static SubmissionDraft WithThumbnail(byte[] data, string mime = "image/webp") =>
        Drafts.Valid() with { Media = [new MediaUpload(MediaRole.Thumbnail, data, mime)] };

    [Theory]
    [InlineData("effect")]
    [InlineData("transition")]
    [InlineData("audio-effect")]
    [InlineData("theme")]
    [InlineData("look")]
    [InlineData("effect-template")]
    public async Task ValidMediaForEveryKind_Passes(string kind) => (await Run(Drafts.Valid(new AssetKindId(kind)))).ShouldBeEmpty();

    [Fact]
    public async Task MissingRequiredRoles_AreReported()
    {
        var violations = await Run(Drafts.Valid(AssetKindId.Theme) with { Media = [] });

        violations.Select(v => (v.Field, v.Code)).ShouldBe([("media.screenshot", "media_missing"), ("media.thumbnail", "media_missing")]);
    }

    [Fact]
    public async Task UnexpectedRole_IsRejected()
    {
        var draft = Drafts.Valid();
        draft = draft with { Media = [.. draft.Media, new MediaUpload(MediaRole.Gallery, Images.Good(512, 288), "image/webp")] };

        (await Run(draft)).Codes().ShouldBe(["media_role_not_allowed"]);
    }

    [Fact]
    public async Task PngLyingAboutItsMime_IsRejected() =>
        (await Run(WithThumbnail(Images.Png(512, 288)))).Codes().ShouldBe(["media_not_webp"]);

    [Fact]
    public async Task WebpDeclaredAsPng_IsJudgedByContent() =>
        (await Run(WithThumbnail(Images.Good(512, 288), "image/png"))).ShouldBeEmpty();

    [Fact]
    public async Task Garbage_IsRejected() =>
        (await Run(WithThumbnail("not an image"u8.ToArray()))).Codes().ShouldBe(["media_undecodable"]);

    [Theory]
    [InlineData(511, 288)]
    [InlineData(512, 289)]
    [InlineData(256, 144)]
    public async Task WrongDimensions_AreRejected(int w, int h) =>
        (await Run(WithThumbnail(Images.Good(w, h)))).Codes().ShouldBe(["media_wrong_dimensions"]);

    [Fact]
    public async Task OversizedFile_IsRejected()
    {
        var noisy = Images.Webp(512, 288, (canvas, w, h) =>
        {
            var rng = new Random(1);
            using var paint = new SKPaint();
            for (var y = 0; y < h; y += 2)
                for (var x = 0; x < w; x += 2)
                {
                    paint.Color = new SKColor((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256));
                    canvas.DrawRect(x, y, 2, 2, paint);
                }
        }, quality: 100);
        noisy.Length.ShouldBeGreaterThan(40 * 1024);

        (await Run(WithThumbnail(noisy))).Codes().ShouldBe(["media_too_large"]);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(255, 255, 255)]
    [InlineData(120, 30, 200)]
    public async Task SolidImages_AreRejected(byte r, byte g, byte b) =>
        (await Run(WithThumbnail(Images.Solid(512, 288, new SKColor(r, g, b))))).Codes().ShouldBe(["media_blank"]);

    [Fact]
    public async Task NearlyFlatImage_IsRejected()
    {
        var flat = Images.Webp(512, 288, (c, w, h) =>
        {
            c.Clear(new SKColor(100, 100, 100));
            using var paint = new SKPaint { Color = new SKColor(103, 103, 103) };
            c.DrawRect(0, 0, w / 2, h, paint);
        });

        (await Run(WithThumbnail(flat))).Codes().ShouldBe(["media_blank"]);
    }

    [Fact]
    public async Task FullyTransparentImage_IsRejected() =>
        (await Run(WithThumbnail(Images.Solid(512, 288, SKColors.Transparent)))).Codes().ShouldBe(["media_transparent"]);

    [Fact]
    public async Task DuplicateRole_IsRejected()
    {
        var draft = Drafts.Valid();
        draft = draft with { Media = [.. draft.Media, .. draft.Media] };

        (await Run(draft)).Codes().ShouldBe(["media_duplicate_role"]);
    }

    [Fact]
    public async Task ThemeScreenshot_HasItsOwnByteCap()
    {
        var screenshot = Images.Good(1280, 720, quality: 80);
        (screenshot.Length <= 120_000).ShouldBeTrue("fixture must fit the cap");
        (await Run(Drafts.Valid(AssetKindId.Theme))).ShouldBeEmpty();
    }
}

public class DuplicateGateTests
{
    private readonly FakeDuplicateLookup _lookup = new();
    private DuplicateGate Gate => new(new ContentRulesOptions(), _lookup, Drafts.Analyzer);

    private static PackageInfo Package(string hex) =>
        new(PackageFormat.DriftFx, default, "user.x", "1.0.0", "0.7.0", "effects", [], "", new Sha256Hash(hex));

    [Fact]
    public async Task NoDuplicates_Passes() => (await Gate.CheckAsync(Drafts.Valid(), TestContext.Current.CancellationToken)).ShouldBeEmpty();

    [Fact]
    public async Task ExactPackage_IsBlocked()
    {
        var hex = new string('a', 64);
        _lookup.Packages.Add(hex);

        var v = (await Gate.CheckAsync(Drafts.Valid() with { PackageInfo = Package(hex) }, TestContext.Current.CancellationToken)).Single();

        (v.Code, v.Severity).ShouldBe(("duplicate_package", GateSeverity.Block));
        (await Gate.CheckAsync(Drafts.Valid() with { PackageInfo = Package(new string('b', 64)) }, TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("dreamy   glow")]
    [InlineData("DREAMY GLOW!")]
    [InlineData("Dreamy-Glow")]
    public async Task SameOwnerTitle_IsBlockedAfterNormalising(string existing)
    {
        _lookup.Titles.Add(existing);

        var v = (await Gate.CheckAsync(Drafts.Valid(), TestContext.Current.CancellationToken)).Single();

        (v.Code, v.Severity, v.Field).ShouldBe(("duplicate_title", GateSeverity.Block, "title"));
    }

    [Fact]
    public async Task SimilarThumbnail_IsFlaggedNotBlocked()
    {
        var draft = Drafts.Valid();
        _lookup.Thumbnails.Add(Drafts.Analyzer.Probe(Images.Good(512, 288, quality: 40))!.DHash);

        var v = (await Gate.CheckAsync(draft, TestContext.Current.CancellationToken)).Single();

        (v.Code, v.Severity).ShouldBe(("similar_thumbnail", GateSeverity.Flag));
    }

    [Fact]
    public async Task DifferentThumbnail_IsNotFlagged()
    {
        var other = Images.Webp(512, 288, (c, w, h) =>
        {
            c.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Black };
            for (var i = 0; i < 8; i++) c.DrawRect(i * 64, 0, 32, h, paint);
            c.DrawRect(0, 0, w, 40, paint);
        });
        _lookup.Thumbnails.Add(Drafts.Analyzer.Probe(other)!.DHash);

        (await Gate.CheckAsync(Drafts.Valid(), TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }
}

public class UploadQuotaGateTests
{
    private static Task<IReadOnlyList<GateViolation>> Run(int trust, int used) =>
        new UploadQuotaGate(new ContentRulesOptions(), new FakeUploadQuota(trust, used)).CheckAsync(Drafts.Valid(), TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(0, 2, false)]
    [InlineData(0, 3, true)]
    [InlineData(1, 9, false)]
    [InlineData(1, 10, true)]
    [InlineData(2, 29, false)]
    [InlineData(2, 30, true)]
    [InlineData(3, 10_000, false)]
    [InlineData(7, 10_000, false)]
    public async Task LimitsByTrustLevel(int trust, int used, bool blocked)
    {
        var result = await Run(trust, used);

        result.Codes().ShouldBe(blocked ? ["upload_limit"] : []);
    }
}

public class ContentGatePipelineTests
{
    private static ContentGatePipeline Pipeline(FakeDuplicateLookup? lookup = null, int trust = 1, int used = 0)
    {
        var rules = new ContentRulesOptions();
        return new ContentGatePipeline(
        [
            new TitleGate(rules, Drafts.Words), new TextGate(rules), new EnglishGate(rules, Drafts.Words), new ProfanityGate(),
            new TaxonomyGate(rules, Kinds.Registry), new MediaGate(rules, Kinds.Registry, Drafts.Analyzer),
            new DuplicateGate(rules, lookup ?? new FakeDuplicateLookup(), Drafts.Analyzer), new UploadQuotaGate(rules, new FakeUploadQuota(trust, used)),
        ]);
    }

    [Fact]
    public async Task CleanSubmission_Succeeds() => (await Pipeline().ValidateAsync(Drafts.Valid(), TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();

    [Fact]
    public async Task AllViolationsAreAggregated_NotJustTheFirst()
    {
        var draft = Drafts.Valid() with
        {
            Title = "BAD!!!!",
            Summary = "tiny",
            Category = "nope",
            License = "WTFPL",
            Media = [],
        };

        var result = await Pipeline(trust: 0, used: 3).ValidateAsync(draft, TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.Unprocessable);
        result.Error.Code.ShouldBe("content_rejected");
        result.Error.Errors!.Keys.ShouldBe(["title", "summary", "category", "license", "media.thumbnail", "account"], ignoreOrder: true);
        result.Error.Errors["title"].Length.ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task Flags_DoNotBlock_ButAreReported()
    {
        var lookup = new FakeDuplicateLookup();
        var draft = Drafts.Valid();
        lookup.Thumbnails.Add(Drafts.Analyzer.Probe(draft.Media[0].Data)!.DHash);

        var result = await Pipeline(lookup).ValidateAsync(draft, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Flags.Codes().ShouldBe(["similar_thumbnail"]);
    }

    [Fact]
    public async Task OnlyBlockingViolationsEnterTheError()
    {
        var lookup = new FakeDuplicateLookup();
        var draft = Drafts.Valid() with { Title = "Bad" };
        lookup.Thumbnails.Add(Drafts.Analyzer.Probe(draft.Media[0].Data)!.DHash);

        var result = await Pipeline(lookup).ValidateAsync(draft, TestContext.Current.CancellationToken);

        result.Error.Errors!.Keys.ShouldBe(["title"]);
        (await Pipeline(lookup).CheckAsync(draft, TestContext.Current.CancellationToken)).Codes().ShouldContain("similar_thumbnail");
    }
}

public class PerceptualHashTests
{
    [Fact]
    public void SameImage_HasDistanceZero()
    {
        var bytes = Images.Good(512, 288);

        PerceptualHash.Distance(Drafts.Analyzer.Probe(bytes)!.DHash, Drafts.Analyzer.Probe(bytes)!.DHash).ShouldBe(0);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(95)]
    public void Recompressed_IsClose(int quality)
    {
        var original = Drafts.Analyzer.Probe(Images.Good(512, 288, 80))!.DHash;
        var recompressed = Drafts.Analyzer.Probe(Images.Good(512, 288, quality))!.DHash;

        PerceptualHash.Distance(original, recompressed).ShouldBeLessThanOrEqualTo(6);
    }

    [Fact]
    public void Rescaled_IsClose()
    {
        var a = Drafts.Analyzer.Probe(Images.Good(512, 288))!.DHash;
        var b = Drafts.Analyzer.Probe(Images.Good(1024, 576))!.DHash;

        PerceptualHash.Distance(a, b).ShouldBeLessThanOrEqualTo(6);
    }

    [Fact]
    public void DifferentImages_AreFarApart()
    {
        var a = Drafts.Analyzer.Probe(Images.Good(512, 288))!.DHash;
        var b = Drafts.Analyzer.Probe(Images.Webp(512, 288, (c, w, h) =>
        {
            using var shader = SKShader.CreateLinearGradient(new SKPoint(w, 0), new SKPoint(0, h), [SKColors.Black, SKColors.White], null, SKShaderTileMode.Clamp);
            using var paint = new SKPaint { Shader = shader };
            c.DrawRect(0, 0, w, h, paint);
        }))!.DHash;

        PerceptualHash.Distance(a, b).ShouldBeGreaterThan(6);
    }

    [Fact]
    public void Distance_CountsDifferingBits()
    {
        PerceptualHash.Distance(0, ulong.MaxValue).ShouldBe(64);
        PerceptualHash.Distance(0b1010, 0b0110).ShouldBe(2);
    }
}

public class QualityRulesProducerTests
{
    [Fact]
    public void Produces_AllLimits_AndSerialises()
    {
        var dto = new QualityRulesProducer(new ContentRulesOptions(), Kinds.Registry, new BloomWordList()).Produce();

        dto.Title.ShouldBe(new LengthRuleDto(6, 60));
        dto.Summary.ShouldBe(new LengthRuleDto(20, 140));
        dto.Description.ShouldBe(new LengthRuleDto(80, 4000));
        dto.EnglishMinRatio.ShouldBe(0.75);
        dto.Licenses.ShouldContain("GPL-3.0-or-later");
        dto.DailyUploadLimits.ShouldBe([3, 10, 30, 0]);
        dto.Kinds.Select(k => k.Id).ShouldNotContain("language-pack");
        var transition = dto.Kinds.Single(k => k.Id == "transition");
        transition.Media.ShouldContain(new MediaRuleDto("preview-strip", true, 1536, 128, 80 * 1024, "image/webp"));
        transition.Tags.ShouldNotBeEmpty();

        var json = System.Text.Json.JsonSerializer.Serialize(dto, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        json.ShouldContain("\"englishMinRatio\":0.75");
    }
}
