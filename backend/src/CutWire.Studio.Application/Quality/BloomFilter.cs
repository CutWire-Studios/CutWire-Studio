using System.Buffers.Binary;
using System.Text;

namespace CutWire.Studio.Application.Quality;

/// <summary>
/// Read-only Bloom filter over UTF-8 words. The binary layout and hashing are specified in docs/quality-gates.md and
/// mirrored by <c>web/packages/content-rules</c>; change both together.
/// </summary>
public sealed class BloomFilter
{
    public const string Magic = "CWBLOOM1";
    public const int HeaderSize = 24;
    public const byte FormatVersion = 1;
    public const uint DefaultSeed = 0x00C0FFEE;
    public const double DefaultFalsePositiveRate = 0.01;
    public const byte DefaultHashCount = 7;

    private readonly byte[] _bits;

    private BloomFilter(byte[] bits, uint bitCount, byte hashCount, uint seed, uint wordCount)
    {
        _bits = bits;
        BitCount = bitCount;
        HashCount = hashCount;
        Seed = seed;
        WordCount = wordCount;
    }

    public uint BitCount { get; }
    public byte HashCount { get; }
    public uint Seed { get; }
    public uint WordCount { get; }

    public static BloomFilter Parse(ReadOnlySpan<byte> file)
    {
        if (file.Length < HeaderSize || !file[..8].SequenceEqual("CWBLOOM1"u8))
            throw new InvalidDataException("Not a CWBLOOM1 file.");
        if (file[8] != FormatVersion) throw new InvalidDataException($"Unsupported Bloom filter version {file[8]}.");

        var hashCount = file[9];
        var bitCount = BinaryPrimitives.ReadUInt32LittleEndian(file[12..]);
        var seed = BinaryPrimitives.ReadUInt32LittleEndian(file[16..]);
        var wordCount = BinaryPrimitives.ReadUInt32LittleEndian(file[20..]);
        if (hashCount == 0 || bitCount == 0 || bitCount % 8 != 0 || file.Length != HeaderSize + bitCount / 8)
            throw new InvalidDataException("Bloom filter header is inconsistent with its length.");

        return new BloomFilter(file[HeaderSize..].ToArray(), bitCount, hashCount, seed, wordCount);
    }

    /// <summary>Builds a filter for the distinct words with the smallest bit count that keeps the expected false-positive rate at or below <paramref name="falsePositiveRate"/>.</summary>
    public static BloomFilter Build(IReadOnlyCollection<string> words, double falsePositiveRate = DefaultFalsePositiveRate,
        byte hashCount = DefaultHashCount, uint seed = DefaultSeed)
    {
        var distinct = words.Distinct(StringComparer.Ordinal).ToList();
        var bitCount = RequiredBitCount(distinct.Count, hashCount, falsePositiveRate);
        var bits = new byte[bitCount / 8];
        foreach (var word in distinct)
            foreach (var position in Positions(Encoding.UTF8.GetBytes(word), bitCount, hashCount, seed))
                bits[position >> 3] |= (byte)(1 << (int)(position & 7));
        return new BloomFilter(bits, bitCount, hashCount, seed, (uint)distinct.Count);
    }

    /// <summary>Smallest multiple of 8 such that (1 - e^(-k*n/m))^k is at most p.</summary>
    public static uint RequiredBitCount(int wordCount, byte hashCount, double falsePositiveRate)
    {
        var perHashFill = Math.Pow(falsePositiveRate, 1.0 / hashCount);
        var m = Math.Ceiling(hashCount * wordCount / -Math.Log(1 - perHashFill));
        return (uint)(Math.Ceiling(m / 8) * 8);
    }

    public bool MightContain(string word) => MightContain(Encoding.UTF8.GetBytes(word));

    public bool MightContain(ReadOnlySpan<byte> word)
    {
        foreach (var position in Positions(word, BitCount, HashCount, Seed))
            if ((_bits[position >> 3] & (1 << (int)(position & 7))) == 0)
                return false;
        return true;
    }

    public byte[] ToBytes()
    {
        var file = new byte[HeaderSize + _bits.Length];
        "CWBLOOM1"u8.CopyTo(file);
        file[8] = FormatVersion;
        file[9] = HashCount;
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(12), BitCount);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(16), Seed);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(20), WordCount);
        _bits.CopyTo(file, HeaderSize);
        return file;
    }

    /// <summary>h1 = FNV-1a(word) seeded, h2 = Murmur3-32(word, seed) | 1, position_i = (h1 + i*h2) mod m.</summary>
    public static IEnumerable<uint> Positions(ReadOnlySpan<byte> word, uint bitCount, byte hashCount, uint seed)
    {
        var (h1, h2) = Hashes(word, seed);
        var positions = new uint[hashCount];
        for (var i = 0; i < hashCount; i++)
            positions[i] = (uint)(((ulong)h1 + (ulong)i * h2) % bitCount);
        return positions;
    }

    public static (uint H1, uint H2) Hashes(ReadOnlySpan<byte> word, uint seed) =>
        (Fnv1a(word, seed), Murmur3(word, seed) | 1);

    public static uint Fnv1a(ReadOnlySpan<byte> data, uint seed)
    {
        var hash = 0x811C9DC5u ^ seed;
        foreach (var b in data)
        {
            hash ^= b;
            hash *= 0x01000193u;
        }

        return hash;
    }

    public static uint Murmur3(ReadOnlySpan<byte> data, uint seed)
    {
        const uint c1 = 0xcc9e2d51, c2 = 0x1b873593;
        var h = seed;
        var blocks = data.Length / 4;
        for (var i = 0; i < blocks; i++)
        {
            var k = BinaryPrimitives.ReadUInt32LittleEndian(data[(i * 4)..]);
            k *= c1;
            k = uint.RotateLeft(k, 15);
            k *= c2;
            h ^= k;
            h = uint.RotateLeft(h, 13);
            h = h * 5 + 0xe6546b64;
        }

        var tail = data[(blocks * 4)..];
        uint t = 0;
        switch (tail.Length)
        {
            case 3: t ^= (uint)tail[2] << 16; goto case 2;
            case 2: t ^= (uint)tail[1] << 8; goto case 1;
            case 1:
                t ^= tail[0];
                t *= c1;
                t = uint.RotateLeft(t, 15);
                t *= c2;
                h ^= t;
                break;
        }

        h ^= (uint)data.Length;
        h ^= h >> 16;
        h *= 0x85ebca6b;
        h ^= h >> 13;
        h *= 0xc2b2ae35;
        h ^= h >> 16;
        return h;
    }
}
