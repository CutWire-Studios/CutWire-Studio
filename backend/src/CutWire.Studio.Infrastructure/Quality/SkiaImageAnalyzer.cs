using CutWire.Studio.Application.Quality;
using SkiaSharp;

namespace CutWire.Studio.Infrastructure.Quality;

public sealed class SkiaImageAnalyzer : IImageAnalyzer
{
    public ImageProbe? Probe(byte[] data)
    {
        try
        {
            using var codec = SKCodec.Create(new SKMemoryStream(data));
            if (codec is null) return null;
            var format = codec.EncodedFormat switch
            {
                SKEncodedImageFormat.Webp => "webp",
                SKEncodedImageFormat.Png => "png",
                SKEncodedImageFormat.Jpeg => "jpeg",
                SKEncodedImageFormat.Gif => "gif",
                var other => other.ToString().ToLowerInvariant(),
            };

            using var bitmap = SKBitmap.Decode(codec, new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            if (bitmap is null) return null;
            return new ImageProbe(format, bitmap.Width, bitmap.Height, LumaStdDev(bitmap, out var transparent), transparent, DHash(bitmap));
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    private static double LumaStdDev(SKBitmap bitmap, out bool fullyTransparent)
    {
        var pixels = bitmap.Pixels;
        double sum = 0, sumSquares = 0;
        var opaque = false;
        foreach (var p in pixels)
        {
            if (p.Alpha != 0) opaque = true;
            var luma = Composited(p);
            sum += luma;
            sumSquares += luma * luma;
        }

        fullyTransparent = !opaque;
        var mean = sum / pixels.Length;
        return Math.Sqrt(Math.Max(0, sumSquares / pixels.Length - mean * mean));
    }

    private static double Composited(SKColor p)
    {
        var a = p.Alpha / 255.0;
        var luma = 0.299 * p.Red + 0.587 * p.Green + 0.114 * p.Blue;
        return luma * a + 255 * (1 - a);
    }

    private static ulong DHash(SKBitmap bitmap)
    {
        using var small = bitmap.Resize(new SKImageInfo(9, 8, SKColorType.Rgba8888, SKAlphaType.Unpremul), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        if (small is null) return 0;
        ulong hash = 0;
        for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++)
            {
                hash <<= 1;
                if (Composited(small.GetPixel(x, y)) > Composited(small.GetPixel(x + 1, y))) hash |= 1;
            }

        return hash;
    }
}
