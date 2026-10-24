using System.IO.Compression;
using System.Text;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Infrastructure.Catalog;
using Shouldly;

namespace CutWire.Studio.UnitTests;

public class ThemeKindTests
{
    private static readonly ThemeKind Theme = new();

    private static byte[] Zip(Dictionary<string, byte[]> entries, Action<ZipArchiveEntry>? configure = null)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, data) in entries)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                configure?.Invoke(entry);
                using var s = entry.Open();
                s.Write(data);
            }

        return stream.ToArray();
    }

    private static byte[] Json(string text) => Encoding.UTF8.GetBytes(text);

    [Theory]
    [InlineData("themes/frosted.json")]
    [InlineData("themes/high-contrast.json")]
    [InlineData("themes/override-example.json")]
    public async Task BundledExamples_AreAccepted(string fixture)
    {
        var result = await Kinds.Validate(Theme, Fixture.Bytes(fixture));

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message + string.Join("; ", result.Error.Errors?.Values.SelectMany(v => v) ?? []) : "");
        result.Value.DriftInstallKind.ShouldBe("themes");
        result.Value.Format.ShouldBe(PackageFormat.Json);
    }

    [Fact]
    public async Task ZipWithThemeJson_IsAccepted()
    {
        var result = await Kinds.Validate(Theme, Zip(new() { ["theme.json"] = Fixture.Bytes("themes/frosted.json") }));

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : "");
        result.Value.Format.ShouldBe(PackageFormat.Zip);
        result.Value.PackageId.ShouldBe("frosted");
    }

    [Fact]
    public async Task ZipWithQmlFolder_IsRejected()
    {
        var zip = Zip(new()
        {
            ["theme.json"] = Fixture.Bytes("themes/override-example.json"),
            ["qml/ThemedButtonBackground.qml"] = Json("import QtQuick\nItem {}"),
        });

        (await Kinds.Validate(Theme, zip)).Code().ShouldBe("qml_not_allowed");
    }

    [Fact]
    public async Task ZipWithQmlFileAnywhere_IsRejected()
    {
        var zip = Zip(new() { ["theme.json"] = Fixture.Bytes("themes/frosted.json"), ["evil.qml"] = Json("Item {}") });

        (await Kinds.Validate(Theme, zip)).Code().ShouldBe("qml_not_allowed");
    }

    [Fact]
    public async Task ZipWithTraversal_IsRejected()
    {
        var zip = Zip(new() { ["theme.json"] = Fixture.Bytes("themes/frosted.json"), ["../theme.json"] = Json("{}") });

        (await Kinds.Validate(Theme, zip)).Code().ShouldBe("zip_unsafe_path");
    }

    [Fact]
    public async Task ZipWithSymlink_IsRejected()
    {
        var zip = Zip(new() { ["theme.json"] = Json("link") }, e => e.ExternalAttributes = (0xA000 | 0x1FF) << 16);

        (await Kinds.Validate(Theme, zip)).Code().ShouldBe("zip_symlink");
    }

    [Fact]
    public async Task ZipWithUnexpectedFile_IsRejected()
    {
        var zip = Zip(new() { ["theme.json"] = Fixture.Bytes("themes/frosted.json"), ["readme.txt"] = Json("hi") });

        (await Kinds.Validate(Theme, zip)).Code().ShouldBe("zip_unexpected_entry");
    }

    [Fact]
    public async Task ZipWithoutThemeJson_IsRejected()
    {
        var zip = Zip(new() { ["theme.dark.json"] = Json("{}") });

        (await Kinds.Validate(Theme, zip)).Code().ShouldBe("missing_file");
    }

    [Fact]
    public async Task ZipWithTooManyEntries_IsRejected()
    {
        var entries = Enumerable.Range(0, 65).ToDictionary(i => $"f{i}.txt", _ => Json("x"));
        entries["theme.json"] = Fixture.Bytes("themes/frosted.json");

        (await Kinds.Validate(Theme, Zip(entries))).Code().ShouldBe("zip_too_many_entries");
    }

    [Fact]
    public async Task ZipEntryOverTwoMegabytes_IsRejected()
    {
        var zip = Zip(new() { ["theme.json"] = new byte[3 * 1024 * 1024] });

        (await Kinds.Validate(Theme, zip)).Code().ShouldBe("zip_entry_too_large");
    }

    [Theory]
    [InlineData("dark")]
    [InlineData("light")]
    [InlineData("system")]
    [InlineData("graphite")]
    [InlineData("frost")]
    public async Task ReservedNames_AreRejected(string name) =>
        (await Kinds.Validate(Theme, Json($$"""{"name":"{{name}}","schemaVersion":1}"""))).Code().ShouldBe("theme_name_invalid");

    [Theory]
    [InlineData("""{"schemaVersion":1}""", "theme_name_required")]
    [InlineData("""{"name":".hidden"}""", "theme_name_invalid")]
    [InlineData("""{"name":"a/b"}""", "theme_name_invalid")]
    [InlineData("""{"name":"ok","schemaVersion":2}""", "theme_schema_version")]
    [InlineData("""{"name":"ok","bogusKey":1}""", "theme_schema_invalid")]
    [InlineData("""{"name":"ok","palette":{"a":"notacolor"}}""", "theme_schema_invalid")]
    [InlineData("""[1]""", "theme_invalid")]
    [InlineData("""not json""", "theme_invalid")]
    public async Task ThemeRules(string json, string code) =>
        (await Kinds.Validate(Theme, Json(json))).Code().ShouldBe(code);

    [Fact]
    public async Task MinimalTheme_IsAccepted() =>
        (await Kinds.Validate(Theme, Json("""{"name":"Midnight Blue","schemaVersion":1,"extends":"dark"}"""))).IsSuccess.ShouldBeTrue();
}

public class LookKindTests
{
    private static readonly LookKind Look = new();

    private static string Cube(int size, int rows, string header = "") =>
        $"TITLE \"Test Look\"\nLUT_3D_SIZE {size}\n{header}" + string.Concat(Enumerable.Range(0, rows).Select(i => $"{i % 2:0.0} 0.5 1.0\n"));

    private static Task<CutWire.Studio.Domain.Common.Result<CutWire.Studio.Application.Catalog.PackageInfo>> Run(string text) =>
        Kinds.Validate(Look, Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task IdentityLut_IsAccepted()
    {
        var result = await Kinds.Validate(Look, Fixture.Bytes("looks/identity-2.cube"));

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : "");
        result.Value.PackageId.ShouldBe("identity-two");
        result.Value.DriftInstallKind.ShouldBe("looks");
        result.Value.Manifest.GetProperty("size").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task LargestLut_IsAccepted() => (await Run(Cube(65, 65 * 65 * 65))).IsSuccess.ShouldBeTrue();

    [Fact]
    public async Task DomainAndInputRange_AreParsed()
    {
        (await Run(Cube(2, 8, "DOMAIN_MIN 0 0 0\nDOMAIN_MAX 2 2 2\n"))).IsSuccess.ShouldBeTrue();
        (await Run(Cube(2, 8, "LUT_3D_INPUT_RANGE 0.0 1.0\n"))).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(1, 1, "lut_size_invalid")]
    [InlineData(66, 1, "lut_size_invalid")]
    [InlineData(2, 7, "lut_row_count")]
    [InlineData(2, 9, "lut_row_count")]
    public async Task SizeAndRowRules(int size, int rows, string code) => (await Run(Cube(size, rows))).Code().ShouldBe(code);

    [Theory]
    [InlineData("TITLE \"x\"\n0 0 0\n", "lut_size_missing")]
    [InlineData("LUT_1D_SIZE 4\n0 0 0\n", "lut_1d_unsupported")]
    [InlineData("LUT_3D_SIZE 2\nBOGUS 1\n", "lut_unknown_keyword")]
    [InlineData("LUT_3D_SIZE 2\nDOMAIN_MIN 0 0\n", "lut_domain_invalid")]
    [InlineData("LUT_3D_SIZE 2\nDOMAIN_MIN 1 1 1\nDOMAIN_MAX 0 0 0\n0 0 0\n1 0 0\n0 1 0\n1 1 0\n0 0 1\n1 0 1\n0 1 1\n1 1 1\n", "lut_domain_invalid")]
    [InlineData("LUT_3D_SIZE 2\n0 0 NaN\n", "lut_row_invalid")]
    [InlineData("LUT_3D_SIZE 2\n0 0 Infinity\n", "lut_row_invalid")]
    [InlineData("LUT_3D_SIZE 2\n0 0\n", "lut_row_invalid")]
    [InlineData("LUT_3D_SIZE 2\n0 0 abc\n", "lut_row_invalid")]
    public async Task MalformedLuts(string text, string code) => (await Run(text)).Code().ShouldBe(code);

    [Fact]
    public async Task BinaryFile_IsRejected() =>
        (await Kinds.Validate(Look, [0x00, 0x01, 0x02])).Code().ShouldBe("lut_not_text");

    [Fact]
    public async Task FileOverFourMegabytes_IsRejected() =>
        (await Look.ValidateAsync(new MemoryStream(new byte[4 * 1024 * 1024 + 1]), TestContext.Current.CancellationToken)).Error.Code.ShouldBe("package_too_large");
}

public class EffectTemplateKindTests
{
    private static readonly EffectTemplateKind Template = new();

    private static Task<CutWire.Studio.Domain.Common.Result<CutWire.Studio.Application.Catalog.PackageInfo>> Run(string json) =>
        Kinds.Validate(Template, Encoding.UTF8.GetBytes(json));

    [Theory]
    [InlineData("templates/anime_punch.json", "halftone_comic")]
    [InlineData("templates/blur_behind.json", "bokeh_dream")]
    public async Task BundledTemplates_AreAccepted(string fixture, string effectId)
    {
        var result = await Kinds.Validate(Template, Fixture.Bytes(fixture));

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : "");
        result.Value.DriftInstallKind.ShouldBe("effect-templates");
        result.Value.SearchText.ShouldContain(effectId);
    }

    [Theory]
    [InlineData("""{"displayName":"X Y","category":"c","sync":"beat","layers":[{"effectId":"a"}]}""")]
    [InlineData("""{"id":"Bad Id","displayName":"X Y","category":"c","sync":"beat","layers":[{"effectId":"a"}]}""")]
    [InlineData("""{"id":"ok","category":"c","sync":"beat","layers":[{"effectId":"a"}]}""")]
    [InlineData("""{"id":"ok","displayName":"X Y","sync":"beat","layers":[{"effectId":"a"}]}""")]
    [InlineData("""{"id":"ok","displayName":"X Y","category":"c","sync":"never","layers":[{"effectId":"a"}]}""")]
    [InlineData("""{"id":"ok","displayName":"X Y","category":"c","sync":"beat"}""")]
    [InlineData("""{"id":"ok","displayName":"X Y","category":"c","sync":"beat","layers":[]}""")]
    [InlineData("""{"id":"ok","displayName":"X Y","category":"c","sync":"beat","layers":[{"params":{}}]}""")]
    [InlineData("""{"id":"ok","displayName":"X Y","category":"c","sync":"beat","layers":[{"effectId":"a","params":{"x":{"nested":1}}}]}""")]
    [InlineData("""{"id":"ok","displayName":"X Y","category":"c","sync":"beat","layers":[{"effectId":"a","pulse":{"param":"x","rest":0,"peak":1}}]}""")]
    [InlineData("""{"id":"ok","displayName":"X Y","category":"c","sync":"beat","layers":[{"effectId":"a","pulse":{"rest":0,"peak":1,"decayMs":10}}]}""")]
    [InlineData("""{"id":"ok","displayName":"X Y","category":"c","sync":"beat","tracks":[{"layers":[{"effectId":"a"}]}]}""")]
    [InlineData("""[]""")]
    [InlineData("""nope""")]
    public async Task InvalidTemplates_AreRejected(string json) =>
        (await Run(json)).Code().ShouldBe("template_invalid");

    [Fact]
    public async Task MinimalTemplate_IsAccepted() =>
        (await Run("""{"id":"ok","displayName":"X Y","category":"rhythm","sync":"onset","layers":[{"effectId":"zoom_pulse","params":{"punch":0.0},"pulse":{"param":"punch","rest":0.0,"peak":0.7,"decayMs":160}}]}""")).IsSuccess.ShouldBeTrue();
}

public class AssetKindRegistryTests
{
    [Fact]
    public void AllKindsAreRegistered_AndOnlyLanguagePackIsDisabled()
    {
        Kinds.Registry.ListEnabled().Select(k => k.Id.Value).ShouldBe(["audio-effect", "effect", "effect-template", "look", "theme", "transition"]);
        Kinds.Registry.Get(AssetKindId.LanguagePack)!.Enabled.ShouldBeFalse();
        Kinds.Registry.Get(new AssetKindId("nope")).ShouldBeNull();
    }

    [Fact]
    public async Task LanguagePack_RejectsUploads() =>
        (await Kinds.Validate(Kinds.Registry.Get(AssetKindId.LanguagePack)!, [1])).Code().ShouldBe("kind_disabled");

    [Fact]
    public void KindData_MatchesTheSpec()
    {
        var effect = Kinds.Registry.Get(AssetKindId.Effect)!;
        effect.MaxPackageBytes.ShouldBe(2 * 1024 * 1024);
        effect.MaxUnpackedBytes.ShouldBe(8 * 1024 * 1024);
        effect.MediaSpecs.ShouldBe([new MediaSpec(MediaRole.Thumbnail, true, 512, 288, 40 * 1024)]);

        Kinds.Registry.Get(AssetKindId.Transition)!.MediaSpecs.Select(s => (s.Role, s.Width, s.Height, s.MaxBytes)).ShouldBe(
            [(MediaRole.PreviewStrip, 1536, 128, 80 * 1024), (MediaRole.Thumbnail, 512, 288, 40 * 1024)]);
        Kinds.Registry.Get(AssetKindId.Theme)!.MediaSpecs.Select(s => (s.Role, s.Width, s.Height, s.MaxBytes)).ShouldBe(
            [(MediaRole.Screenshot, 1280, 720, 120_000), (MediaRole.Thumbnail, 512, 288, 40 * 1024)]);
        Kinds.Registry.Get(AssetKindId.Look)!.MaxPackageBytes.ShouldBe(4 * 1024 * 1024);
        Kinds.Registry.Get(AssetKindId.Theme)!.AcceptedExtensions.ShouldBe([".json", ".zip"]);
    }

    [Fact]
    public void Vocabularies_AreCuratedAndAscii()
    {
        foreach (var kind in Kinds.Registry.ListEnabled())
        {
            kind.TagVocabulary.Count.ShouldBeInRange(20, 40, kind.Id.Value);
            kind.TagVocabulary.ShouldAllBe(t => t == t.ToLowerInvariant() && t.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'));
            kind.TagVocabulary.Distinct().Count().ShouldBe(kind.TagVocabulary.Count);
            kind.Categories.ShouldNotBeEmpty();
        }

        Kinds.Registry.Get(AssetKindId.Effect)!.Categories.ShouldContain("dreamy");
    }

    [Theory]
    [InlineData("Effect")]
    [InlineData("has space")]
    [InlineData("-x")]
    [InlineData("")]
    public void KindIds_AreValidated(string value) => AssetKindId.TryParse(value, out _).ShouldBe(value == "Effect");
}
