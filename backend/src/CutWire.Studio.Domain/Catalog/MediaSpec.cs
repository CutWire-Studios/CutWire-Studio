namespace CutWire.Studio.Domain.Catalog;

public sealed record MediaSpec(MediaRole Role, bool Required, int Width, int Height, int MaxBytes)
{
    public const string MimeType = "image/webp";
}
