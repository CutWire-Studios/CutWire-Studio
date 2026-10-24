namespace CutWire.Studio.Application.Abstractions;

public sealed record ImageInfo(string Mime, int Width, int Height);

public interface IImageInspector
{
    /// <summary>Returns null when the stream is not a decodable PNG, JPEG or WebP image.</summary>
    ImageInfo? Inspect(Stream content);
}
