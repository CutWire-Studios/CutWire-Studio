using CutWire.Studio.Application.Abstractions;
using SkiaSharp;

namespace CutWire.Studio.Infrastructure.Imaging;

public sealed class SkiaImageInspector : IImageInspector
{
    public ImageInfo? Inspect(Stream content)
    {
        try
        {
            content.Position = 0;
            using var data = SKData.Create(content);
            using var codec = data is null ? null : SKCodec.Create(data);
            if (codec is null) return null;

            var mime = codec.EncodedFormat switch
            {
                SKEncodedImageFormat.Png => "image/png",
                SKEncodedImageFormat.Jpeg => "image/jpeg",
                SKEncodedImageFormat.Webp => "image/webp",
                _ => null,
            };
            if (mime is null) return null;

            var info = codec.Info;
            if (info.Width > 4096 || info.Height > 4096) return new ImageInfo(mime, info.Width, info.Height);

            using var bitmap = SKBitmap.Decode(codec);
            return bitmap is null ? null : new ImageInfo(mime, info.Width, info.Height);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
