using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CutWire.Studio.Application.Catalog;
using CutWire.Studio.Application.Quality;
using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Infrastructure.Catalog;
using CutWire.Studio.Infrastructure.Quality;
using SkiaSharp;
using ZstdSharp;

namespace CutWire.Studio.UnitTests;

internal static class Fixture
{
    public static byte[] Bytes(string relative) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", relative));

    public static Stream Stream(byte[] bytes) => new MemoryStream(bytes);
}

internal static class Kinds
{
    public static readonly IAssetKind[] All =
    [
        new EffectKind(), new TransitionKind(), new AudioEffectKind(), new ThemeKind(), new LookKind(), new EffectTemplateKind(), new LanguagePackKind(),
    ];

    public static readonly AssetKindRegistry Registry = new(All);

    public static Task<Result<PackageInfo>> Validate(IAssetKind kind, byte[] bytes) => kind.ValidateAsync(new MemoryStream(bytes), TestContext.Current.CancellationToken);
}

internal static class ResultAssert
{
    public static string Code(this Result result) => result.IsFailure ? result.Error.Code : "ok";
}

/// <summary>Builds .driftfx containers so tests can break exactly one thing at a time.</summary>
internal sealed class DriftfxBuilder(string root = "effects")
{
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, object> Manifest { get; } = new()
    {
        ["schema"] = 1, ["id"] = "user.fx", ["version"] = "1.0.0", ["name"] = "Test", ["description"] = "", ["details"] = "", ["author"] = "",
        ["license"] = "", ["minAppVersion"] = "0.7.0", ["platform"] = "", ["generator"] = "test/1",
    };

    public string Root { get; set; } = root;
    public string Kind { get; set; } = root;
    public ulong? RawLengthOverride { get; set; }
    public int UntabledPayloadBytes { get; set; }

    public static DriftfxBuilder ValidEffect()
    {
        var b = new DriftfxBuilder();
        b.Files["effects/fx/effect.json"] = Encoding.UTF8.GetBytes("""
            {"id":"fx","displayName":"Fx","category":"color","backend":"gpu",
             "parameters":[{"identifier":"amount","displayName":"Amount","type":"float","minValue":0,"maxValue":1,"defaultValue":0.5}],
             "pipeline":{"intermediateBuffers":[],"passes":[{"passIndex":0,"fragmentShader":"main.frag","inputs":[{"type":"source_texture"}],"output":{"type":"canvas"}}]}}
            """);
        b.Files["effects/fx/main.frag"] = "#version 330 core\nvoid main(){}\n"u8.ToArray();
        return b;
    }

    public DriftfxBuilder Edit(string file, Func<string, string> change)
    {
        Files[file] = Encoding.UTF8.GetBytes(change(Encoding.UTF8.GetString(Files[file])));
        return this;
    }

    public byte[] Build(Action<byte[]>? tamper = null)
    {
        var ordered = Files.OrderBy(f => f.Key, StringComparer.Ordinal).ToList();
        var table = new List<SortedDictionary<string, object>>();
        var payload = new MemoryStream();
        foreach (var (path, data) in ordered)
        {
            table.Add(new SortedDictionary<string, object>(StringComparer.Ordinal)
            {
                ["path"] = path, ["offset"] = payload.Length, ["size"] = data.Length, ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(data)),
            });
            payload.Write(data);
        }

        var manifest = new SortedDictionary<string, object>(Manifest, StringComparer.Ordinal)
        {
            ["files"] = table,
            ["installedSize"] = payload.Length,
            ["provides"] = new[] { new SortedDictionary<string, object>(StringComparer.Ordinal) { ["items"] = 1, ["kind"] = Kind, ["root"] = Root } },
        };
        var meta = JsonSerializer.SerializeToUtf8Bytes(manifest);
        var inflated = payload.ToArray().Concat(new byte[UntabledPayloadBytes]).ToArray();
        var compressed = new Compressor(3).Wrap(inflated).ToArray();

        var body = new MemoryStream();
        body.Write("DRIFTFX\0"u8);
        body.Write(BitConverter.GetBytes(1u));
        body.Write(BitConverter.GetBytes((uint)meta.Length));
        body.Write(meta);
        body.Write(BitConverter.GetBytes((ulong)compressed.Length));
        body.Write(BitConverter.GetBytes(RawLengthOverride ?? (ulong)payload.Length));
        body.Write(compressed);
        var bytes = body.ToArray();
        tamper?.Invoke(bytes);
        return [.. bytes, .. SHA256.HashData(bytes)];
    }

    public static byte[] Redigest(byte[] package)
    {
        var body = package[..^32];
        return [.. body, .. SHA256.HashData(body)];
    }
}

internal static class Images
{
    public static byte[] Webp(int width, int height, Action<SKCanvas, int, int> draw, int quality = 80)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap)) draw(canvas, width, height);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Webp, quality).ToArray();
    }

    public static void Scene(SKCanvas canvas, int w, int h)
    {
        using var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, h), [SKColors.DarkBlue, SKColors.OrangeRed, SKColors.Gold], null, SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = shader };
        canvas.DrawRect(0, 0, w, h, paint);
        using var circle = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawCircle(w * 0.3f, h * 0.4f, h * 0.25f, circle);
        using var bar = new SKPaint { Color = SKColors.Black };
        canvas.DrawRect(w * 0.55f, h * 0.2f, w * 0.3f, h * 0.15f, bar);
    }

    public static byte[] Good(int w, int h, int quality = 80) => Webp(w, h, Scene, quality);

    public static byte[] Solid(int w, int h, SKColor color) => Webp(w, h, (c, _, _) => c.Clear(color));

    public static byte[] Png(int w, int h)
    {
        using var bitmap = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(bitmap)) Scene(canvas, w, h);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 90).ToArray();
    }
}

internal static class Drafts
{
    public static readonly IImageAnalyzer Analyzer = new SkiaImageAnalyzer();
    public static readonly IWordList Words = new BloomWordList();
    public static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public const string Summary = "Adds a soft dreamy glow around bright highlights.";

    public const string Description =
        "This effect adds a soft glow around the brightest parts of your clip. " +
        "Use the amount slider to control how strong the bloom looks, and lower it for a subtle result that keeps the original detail.";

    public static SubmissionDraft Valid(AssetKindId? kind = null)
    {
        var id = kind ?? AssetKindId.Effect;
        var media = Kinds.Registry.Get(id)!.MediaSpecs.Select(s => new MediaUpload(s.Role, Images.Good(s.Width, s.Height), "image/webp")).ToList();
        return new SubmissionDraft(id, Owner, "Dreamy Glow", Summary, Description, ["glow", "dream"], "dreamy", "CC0-1.0", media, null);
    }
}

internal sealed class FakeDuplicateLookup : IDuplicateLookup
{
    public HashSet<string> Packages { get; } = [];
    public List<ulong> Thumbnails { get; } = [];
    public List<string> Titles { get; } = [];

    public Task<bool> PackageExistsAsync(Sha256Hash packageSha256, Guid? excludeItemId, CancellationToken ct) => Task.FromResult(Packages.Contains(packageSha256.Value));

    public Task<bool> SimilarThumbnailExistsAsync(ulong dHash, int maxDistance, Guid? excludeItemId, CancellationToken ct) =>
        Task.FromResult(Thumbnails.Any(t => PerceptualHash.Distance(t, dHash) <= maxDistance));

    public Task<IReadOnlyList<string>> OwnerTitlesAsync(Guid ownerId, Guid? excludeItemId, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(Titles);
}

internal sealed class FakeUploadQuota(int trustLevel, int used) : IUploadQuota
{
    public Task<UploadUsage> GetUsageAsync(Guid ownerId, CancellationToken ct) => Task.FromResult(new UploadUsage(trustLevel, used));
}
