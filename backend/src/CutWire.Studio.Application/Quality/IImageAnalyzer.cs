namespace CutWire.Studio.Application.Quality;

public sealed record ImageProbe(string Format, int Width, int Height, double LumaStdDev, bool FullyTransparent, ulong DHash);

public interface IImageAnalyzer
{
    /// <summary>Decodes the bytes by content, ignoring any declared type. Null when they are not a decodable image.</summary>
    ImageProbe? Probe(byte[] data);
}

public static class PerceptualHash
{
    public static int Distance(ulong a, ulong b) => System.Numerics.BitOperations.PopCount(a ^ b);
}
