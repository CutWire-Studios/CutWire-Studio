using System.Text;
using CutWire.Studio.Application.Catalog;
using CutWire.Studio.Infrastructure.Catalog;
using Shouldly;

namespace CutWire.Studio.UnitTests;

public class DriftfxKindTests
{
    private static readonly EffectKind Effect = new();
    private static readonly TransitionKind Transition = new();
    private static readonly AudioEffectKind Audio = new();

    [Fact]
    public async Task ForgeEffect_IsAccepted()
    {
        var result = await Kinds.Validate(Effect, Fixture.Bytes("effect.driftfx"));

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : "");
        result.Value.PackageId.ShouldBe("user.forge_fixture_tint");
        result.Value.Version.ShouldBe("1.0.0");
        result.Value.MinAppVersion.ShouldBe("0.7.0");
        result.Value.DriftInstallKind.ShouldBe("effects");
        result.Value.Files.Select(f => f.Path).ShouldContain("effects/forge_fixture_tint/main.frag");
        result.Value.SearchText.ShouldContain("Fixture tint");
    }

    [Fact]
    public async Task ForgeTransition_IsAccepted()
    {
        var result = await Kinds.Validate(Transition, Fixture.Bytes("transition.driftfx"));

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : "");
        result.Value.DriftInstallKind.ShouldBe("transitions");
    }

    [Fact]
    public async Task ForgeAudioEffect_IsAccepted()
    {
        var result = await Kinds.Validate(Audio, Fixture.Bytes("audio-effect.driftfx"));

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : "");
        result.Value.MinAppVersion.ShouldBe("0.8.0");
    }

    [Theory]
    [InlineData("transition.driftfx")]
    [InlineData("audio-effect.driftfx")]
    public async Task EffectHandler_RejectsOtherKinds(string fixture) =>
        (await Kinds.Validate(Effect, Fixture.Bytes(fixture))).Code().ShouldBe("wrong_kind");

    [Fact]
    public async Task TransitionHandler_RejectsEffect() =>
        (await Kinds.Validate(Transition, Fixture.Bytes("effect.driftfx"))).Code().ShouldBe("wrong_kind");

    [Fact]
    public async Task BadMagic_IsRejected()
    {
        var bytes = Fixture.Bytes("effect.driftfx");
        bytes[0] = (byte)'X';

        (await Kinds.Validate(Effect, bytes)).Code().ShouldBe("driftfx_bad_magic");
    }

    [Fact]
    public async Task DriftpkgMagic_IsRejected()
    {
        var bytes = Fixture.Bytes("effect.driftfx");
        Encoding.ASCII.GetBytes("DRIFTPKG").CopyTo(bytes, 0);

        (await Kinds.Validate(Effect, bytes)).Code().ShouldBe("driftfx_bad_magic");
    }

    [Fact]
    public async Task BadFormatVersion_IsRejected()
    {
        var bytes = Fixture.Bytes("effect.driftfx");
        bytes[8] = 2;

        (await Kinds.Validate(Effect, bytes)).Code().ShouldBe("driftfx_unsupported_version");
    }

    [Fact]
    public async Task FlippedPayloadByte_FailsTheTrailingDigest()
    {
        var bytes = Fixture.Bytes("effect.driftfx");
        bytes[^40] ^= 0xFF;

        (await Kinds.Validate(Effect, bytes)).Code().ShouldBe("driftfx_digest_mismatch");
    }

    [Fact]
    public async Task TruncatedPackage_IsRejected()
    {
        var bytes = Fixture.Bytes("effect.driftfx");

        (await Kinds.Validate(Effect, bytes[..100])).IsFailure.ShouldBeTrue();
        (await Kinds.Validate(Effect, bytes[..^1])).Code().ShouldBe("driftfx_size_mismatch");
    }

    [Fact]
    public async Task FileWithWrongHash_IsRejected()
    {
        var builder = DriftfxBuilder.ValidEffect();
        var bytes = builder.Build();
        var text = Encoding.UTF8.GetBytes(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(builder.Files["effects/fx/main.frag"])));
        var at = bytes.AsSpan().IndexOf(text);
        bytes[at] = (byte)(bytes[at] == '0' ? '1' : '0');

        var result = await Kinds.Validate(Effect, DriftfxBuilder.Redigest(bytes));

        result.Code().ShouldBe("file_digest_mismatch");
    }

    [Fact]
    public async Task ValidBuilderPackage_IsAccepted() =>
        (await Kinds.Validate(Effect, DriftfxBuilder.ValidEffect().Build())).IsSuccess.ShouldBeTrue();

    [Theory]
    [InlineData("effects/../evil/effect.json")]
    [InlineData("/etc/passwd")]
    [InlineData("effects/fx/../../x.frag")]
    [InlineData("effects\\fx\\x.frag")]
    public async Task PathTraversal_IsRejected(string path)
    {
        var builder = DriftfxBuilder.ValidEffect();
        builder.Files[path] = [1, 2, 3];

        (await Kinds.Validate(Effect, builder.Build())).Code().ShouldBe("unsafe_path");
    }

    [Fact]
    public async Task UnsafeRoot_IsRejected()
    {
        var builder = DriftfxBuilder.ValidEffect();
        builder.Root = "../effects";

        (await Kinds.Validate(Effect, builder.Build())).Code().ShouldBe("manifest_invalid");
    }

    [Fact]
    public async Task FileOutsideThePackageFolder_IsRejected()
    {
        var builder = DriftfxBuilder.ValidEffect();
        builder.Files["effects/other/effect.json"] = [1];

        (await Kinds.Validate(Effect, builder.Build())).Code().ShouldBe("unsafe_layout");
    }

    [Fact]
    public async Task DeclaredUnpackedSizeAboveCap_IsRejectedBeforeDecompressing()
    {
        var builder = DriftfxBuilder.ValidEffect();
        builder.RawLengthOverride = 9UL * 1024 * 1024 * 1024;

        (await Kinds.Validate(Effect, builder.Build())).Code().ShouldBe("unpacked_too_large");
    }

    [Fact]
    public async Task RealZipBomb_IsRejected()
    {
        var builder = DriftfxBuilder.ValidEffect();
        builder.Files["effects/fx/tex0.png"] = new byte[20 * 1024 * 1024];

        var bytes = builder.Build();

        bytes.Length.ShouldBeLessThan(2 * 1024 * 1024);
        (await Kinds.Validate(Effect, bytes)).Code().ShouldBe("unpacked_too_large");
    }

    [Fact]
    public async Task PayloadThatInflatesBeyondItsDeclaredSize_IsRejected()
    {
        var builder = DriftfxBuilder.ValidEffect();
        builder.UntabledPayloadBytes = 6 * 1024 * 1024;

        (await Kinds.Validate(Effect, builder.Build())).Code().ShouldBe("payload_invalid");
    }

    [Fact]
    public async Task OversizedUpload_IsRejectedBeforeParsing()
    {
        var result = await Effect.ValidateAsync(new MemoryStream(new byte[2 * 1024 * 1024 + 1]), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe("package_too_large");
    }

    [Fact]
    public async Task MissingFragmentShader_IsRejected()
    {
        var builder = DriftfxBuilder.ValidEffect();
        builder.Files.Remove("effects/fx/main.frag");

        (await Kinds.Validate(Effect, builder.Build())).Code().ShouldBe("missing_file");
    }

    [Fact]
    public async Task MissingTexture_IsRejected()
    {
        var builder = DriftfxBuilder.ValidEffect().Edit("effects/fx/effect.json", s => s.Replace("\"intermediateBuffers\":[]", "\"intermediateBuffers\":[],\"textures\":[{\"id\":\"t\",\"file\":\"tex0.png\"}]"));

        (await Kinds.Validate(Effect, builder.Build())).Code().ShouldBe("missing_file");
    }

    [Fact]
    public async Task ReferencedTexture_IsAccepted()
    {
        var builder = DriftfxBuilder.ValidEffect().Edit("effects/fx/effect.json", s => s.Replace("\"intermediateBuffers\":[]", "\"intermediateBuffers\":[],\"textures\":[{\"id\":\"t\",\"file\":\"tex0.png\"}]"));
        builder.Files["effects/fx/tex0.png"] = [1, 2, 3];

        (await Kinds.Validate(Effect, builder.Build())).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task MissingManifestJson_IsRejected()
    {
        var builder = DriftfxBuilder.ValidEffect();
        builder.Files.Remove("effects/fx/effect.json");
        builder.Files["effects/fx/forge.json"] = [1];

        (await Kinds.Validate(Effect, builder.Build())).Code().ShouldBe("missing_file");
    }

    [Fact]
    public async Task ShaderWithoutVersionDirective_IsRejected()
    {
        var builder = DriftfxBuilder.ValidEffect();
        builder.Files["effects/fx/main.frag"] = "void main(){}"u8.ToArray();

        (await Kinds.Validate(Effect, builder.Build())).Code().ShouldBe("shader_version");
    }

    [Fact]
    public async Task ExecutableFiles_AreRejected()
    {
        var builder = DriftfxBuilder.ValidEffect();
        builder.Files["effects/fx/run.sh"] = "#!/bin/sh"u8.ToArray();

        (await Kinds.Validate(Effect, builder.Build())).Code().ShouldBe("unexpected_file");
    }

    [Fact]
    public async Task AudioEffect_CannotCarryShaders()
    {
        var builder = new DriftfxBuilder("audio-effects");
        builder.Files["audio-effects/fx/audio-effect.json"] = """{"id":"fx","displayName":"Fx","category":"space","backend":"juce","processor":"echo","parameters":[]}"""u8.ToArray();
        builder.Files["audio-effects/fx/main.frag"] = "#version 330 core"u8.ToArray();

        (await Kinds.Validate(Audio, builder.Build())).Code().ShouldBe("unexpected_file");
    }

    [Theory]
    [InlineData("\"type\":\"float\"", "\"type\":\"file\"", "param_type_not_allowed")]
    [InlineData("\"minValue\":0,\"maxValue\":1", "\"minValue\":2,\"maxValue\":1", "param_invalid")]
    [InlineData("\"identifier\":\"amount\"", "\"identifier\":\"1bad id\"", "param_invalid")]
    [InlineData("\"type\":\"float\"", "\"type\":\"choice\"", "param_invalid")]
    [InlineData("\"backend\":\"gpu\"", "\"backend\":\"model3d\"", "manifest_invalid")]
    [InlineData("\"category\":\"color\",", "", "manifest_invalid")]
    [InlineData("\"id\":\"fx\",\"displayName\"", "\"id\":\"other\",\"displayName\"", "manifest_invalid")]
    [InlineData("\"output\":{\"type\":\"canvas\"}", "\"output\":{\"type\":\"buffer\",\"id\":\"nope\"}", "manifest_invalid")]
    public async Task EffectDefinitionRules(string find, string replace, string code)
    {
        var builder = DriftfxBuilder.ValidEffect().Edit("effects/fx/effect.json", s => s.Replace(find, replace));

        (await Kinds.Validate(Effect, builder.Build())).Code().ShouldBe(code);
    }

    [Theory]
    [InlineData("\"version\"", "\"nope\"")]
    [InlineData("\"minAppVersion\"", "\"1.0\"")]
    public async Task ManifestVersionsMustBeSemver(string key, string value)
    {
        var builder = DriftfxBuilder.ValidEffect();
        builder.Manifest[key.Trim('"')] = value.Trim('"');

        (await Kinds.Validate(Effect, builder.Build())).Code().ShouldBe("manifest_invalid");
    }

    [Fact]
    public async Task AudioGraphWithoutGraph_IsRejected()
    {
        var builder = new DriftfxBuilder("audio-effects");
        builder.Files["audio-effects/fx/audio-effect.json"] = """{"id":"fx","displayName":"Fx","category":"space","backend":"juce","processor":"graph","parameters":[]}"""u8.ToArray();

        (await Kinds.Validate(Audio, builder.Build())).Code().ShouldBe("manifest_invalid");
    }
}
