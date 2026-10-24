using System.IO.Compression;
using CutWire.Studio.Application.Quality;

// Usage: dotnet run --project tools/BloomGen [words.txt.gz] [english.bloom]
var root = AppContext.BaseDirectory;
while (root is not null && !File.Exists(Path.Combine(root, "CutWire.Studio.slnx"))) root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
if (root is null) throw new InvalidOperationException("Run from inside the backend directory.");

var input = args.Length > 0 ? args[0] : Path.Combine(root, "tools", "BloomGen", "data", "english-words.txt.gz");
var output = args.Length > 1 ? args[1] : Path.Combine(root, "src", "CutWire.Studio.Application", "Quality", "Resources", "english.bloom");

var words = new List<string>();
await using (var file = File.OpenRead(input))
await using (var stream = input.EndsWith(".gz") ? new GZipStream(file, CompressionMode.Decompress) : (Stream)file)
using (var reader = new StreamReader(stream))
{
    while (await reader.ReadLineAsync() is { } line)
    {
        var word = line.Trim();
        if (word.Length > 0 && word.All(char.IsAsciiLetterLower)) words.Add(word);
    }
}

var original = words.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
var pruned = WordListPruner.Prune(original);
var filter = BloomFilter.Build(pruned);

var rejected = WordListPruner.Rejected(original, new BloomFilterWordList(filter));
if (rejected.Count > 0)
    throw new InvalidOperationException($"Pruning broke lookup for {rejected.Count} words, e.g. {string.Join(", ", rejected.Take(10))}.");
Console.WriteLine($"original={original.Count} pruned={pruned.Count} selfCheck=ok");
var bytes = filter.ToBytes();
await File.WriteAllBytesAsync(output, bytes);
Console.WriteLine($"words={filter.WordCount} m={filter.BitCount} k={filter.HashCount} seed=0x{filter.Seed:X8} bytes={bytes.Length} -> {output}");
