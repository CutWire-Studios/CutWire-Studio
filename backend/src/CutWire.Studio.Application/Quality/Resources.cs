using System.IO.Compression;

namespace CutWire.Studio.Application.Quality;

internal static class Resources
{
    public static Stream Open(string name)
    {
        var assembly = typeof(Resources).Assembly;
        return assembly.GetManifestResourceStream($"CutWire.Studio.Application.Quality.Resources.{name}")
            ?? throw new InvalidOperationException($"Embedded resource {name} is missing.");
    }

    public static HashSet<string> LoadLines(string name)
    {
        using var reader = new StreamReader(name.EndsWith(".gz") ? new GZipStream(Open(name), CompressionMode.Decompress) : Open(name));
        var set = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            var word = line.Trim();
            if (word.Length > 0 && word[0] != '#') set.Add(word);
        }

        return set;
    }
}
