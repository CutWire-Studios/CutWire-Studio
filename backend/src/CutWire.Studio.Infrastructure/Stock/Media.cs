using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Stock;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Infrastructure.Stock;

public sealed class StockMediaOptions
{
    public const string Section = "Stock:Media";

    /// <summary>Secret for sealing media URLs. Unset means a random key per process (URLs do not survive a restart).</summary>
    public string SealKey { get; set; } = "";

    public TimeSpan PreviewTtl { get; set; } = TimeSpan.FromHours(24);

    /// <summary>How imgproxy reaches this service. Defaults to PublicUrl.</summary>
    public string InternalUrl { get; set; } = "";

    public ImgproxyOptions Imgproxy { get; set; } = new();

    /// <summary>Testing only: lets the preview relay and downloads reach loopback and private addresses.</summary>
    public bool AllowPrivateNetworks { get; set; }
}

public sealed class ImgproxyOptions
{
    /// <summary>Public prefix clients are pointed at (e.g. https://stock.example/img). Empty disables imgproxy.</summary>
    public string Prefix { get; set; } = "";

    public string Key { get; set; } = "";
    public string Salt { get; set; } = "";
    public TimeSpan Ttl { get; set; } = TimeSpan.FromHours(24);
}

/// <summary>
/// AES-GCM sealed claim sets. Sealed rather than signed-but-readable, because a preview claim holds the origin URL and a base64 JSON token
/// would hand every client exactly the provider URL we are not exposing. The nonce is derived from the plaintext, so the same claim always
/// yields the same token and stays cacheable.
/// </summary>
public sealed class MediaSealer : IMediaSealer
{
    private readonly AesGcm _aes;
    private readonly byte[] _ivKey;
    private readonly IClock _clock;
    private readonly TimeSpan _previewTtl;

    public MediaSealer(IOptions<StockMediaOptions> options, IClock clock, ILogger<MediaSealer> logger)
    {
        var o = options.Value;
        byte[] secret;
        if (string.IsNullOrEmpty(o.SealKey))
        {
            logger.LogWarning("Stock:Media:SealKey is not set; sealed stock URLs will not survive a restart or work across instances");
            secret = RandomNumberGenerator.GetBytes(32);
        }
        else
        {
            secret = Encoding.UTF8.GetBytes(o.SealKey);
        }

        _aes = new AesGcm(SHA256.HashData(secret), AesGcm.TagByteSizes.MaxSize);
        _ivKey = HMACSHA256.HashData(secret, "cutwire-stock-iv"u8);
        _clock = clock;
        _previewTtl = o.PreviewTtl;
    }

    private sealed record Payload(string? U, string? K, string? N, string? M, long E);

    public string Seal(MediaClaims claims)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(new Payload(claims.Source, claims.BlobSha256, claims.Filename, claims.Mime, claims.Expires));
        var nonce = HMACSHA256.HashData(_ivKey, plain)[..AesGcm.NonceByteSizes.MaxSize];
        var cipher = new byte[plain.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        _aes.Encrypt(nonce, plain, cipher, tag);
        return Convert.ToBase64String([.. nonce, .. cipher, .. tag]).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public MediaClaims? Open(string token)
    {
        try
        {
            var padded = token.Replace('-', '+').Replace('_', '/');
            var raw = Convert.FromBase64String(padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '='));
            const int nonceSize = 12, tagSize = 16;
            if (raw.Length <= nonceSize + tagSize) return null;
            var plain = new byte[raw.Length - nonceSize - tagSize];
            _aes.Decrypt(raw.AsSpan(0, nonceSize), raw.AsSpan(nonceSize, plain.Length), raw.AsSpan(nonceSize + plain.Length, tagSize), plain);
            var p = JsonSerializer.Deserialize<Payload>(plain);
            if (p is null || (p.E > 0 && _clock.UtcNow.ToUnixTimeSeconds() > p.E)) return null;
            return new MediaClaims(p.U, p.K, p.N, p.M, p.E);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Rounded up to the hour so the same source yields a stable, cacheable URL within that hour.</summary>
    public long PreviewExpiry()
    {
        var at = _clock.UtcNow + _previewTtl;
        return new DateTimeOffset(at.Year, at.Month, at.Day, at.Hour, 0, 0, TimeSpan.Zero).AddHours(1).ToUnixTimeSeconds();
    }
}

public sealed class MediaUrls(IMediaSealer sealer, IOptions<StockMediaOptions> options, IConfiguration configuration, IClock clock) : IMediaUrls
{
    private const string Thumbnail = "rs:fit:480:480:0/q:80";
    private const string PreviewStill = "rs:fit:1280:1280:0/q:85";

    private readonly ImgproxyOptions _img = options.Value.Imgproxy;
    private readonly byte[]? _key = TryHex(options.Value.Imgproxy.Key);
    private readonly byte[]? _salt = TryHex(options.Value.Imgproxy.Salt);
    private readonly string _internal = (options.Value.InternalUrl is { Length: > 0 } u ? u : configuration["PublicUrl"] ?? "").TrimEnd('/');

    public const string RelayBase = "/api/v1/stock/media/";
    public const string FileBase = "/api/v1/stock/files/";

    private bool UseImgproxy => !string.IsNullOrEmpty(_img.Prefix) && _key is not null && _salt is not null;

    public string Thumb(string source) => Image(source, Thumbnail);

    public string Preview(string source, string mime) =>
        string.IsNullOrEmpty(mime) || mime.StartsWith("image/", StringComparison.Ordinal) ? Image(source, PreviewStill) : Relay(source, mime, "");

    public string FileUrl(string sha256, string filename, string mime, DateTimeOffset expires) =>
        FileBase + sealer.Seal(new MediaClaims(null, sha256, filename, mime, expires.ToUnixTimeSeconds()));

    private string Relay(string source, string mime, string baseUrl) =>
        baseUrl + RelayBase + sealer.Seal(new MediaClaims(source, null, null, string.IsNullOrEmpty(mime) ? null : mime, sealer.PreviewExpiry()));

    /// <summary>imgproxy is handed a sealed relay link back to this service, never the origin: its plain source segment is only base64.</summary>
    private string Image(string source, string rendition)
    {
        if (!UseImgproxy) return Relay(source, "", "");
        var relay = Relay(source, "", _internal);
        var expiry = clock.UtcNow.Add(_img.Ttl);
        var exp = new DateTimeOffset(expiry.Year, expiry.Month, expiry.Day, expiry.Hour, 0, 0, TimeSpan.Zero).AddHours(1).ToUnixTimeSeconds();
        var path = $"/exp:{exp}/{rendition}/{Base64Url(Encoding.UTF8.GetBytes(relay))}";
        using var mac = new HMACSHA256(_key!);
        mac.TransformBlock(_salt!, 0, _salt!.Length, null, 0);
        var bytes = Encoding.UTF8.GetBytes(path);
        mac.TransformFinalBlock(bytes, 0, bytes.Length);
        return $"{_img.Prefix.TrimEnd('/')}/{Base64Url(mac.Hash!)}{path}";
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[]? TryHex(string value)
    {
        try
        {
            return string.IsNullOrEmpty(value) ? null : Convert.FromHexString(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>An HTTP client that resolves DNS itself and refuses loopback, link-local, private and CGNAT addresses (also across redirects).</summary>
public sealed class StockHttpClients : IStockHttpClients, IDisposable
{
    private readonly HttpClient _client;

    public StockHttpClients(IOptions<StockMediaOptions> options)
    {
        var allowPrivate = options.Value.AllowPrivateNetworks;
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectCallback = async (context, ct) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
                var allowed = addresses.Where(a => allowPrivate || IsPublic(a)).ToArray();
                if (allowed.Length == 0) throw new HttpRequestException("refused non-public address");
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
        _client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("CutWireStudio/1.0 (+https://cutwire.studio)");
    }

    public HttpClient Public => _client;

    public void Dispose() => _client.Dispose();

    public static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.IsIPv6LinkLocal || ip.IsIPv6Multicast
            || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal)
            return false;
        if (ip.AddressFamily != AddressFamily.InterNetwork) return true;
        var b = ip.GetAddressBytes();
        return !(b[0] is 0 or 10 or 127 or >= 224
                 || (b[0] == 100 && (b[1] & 0xc0) == 64)
                 || (b[0] == 169 && b[1] == 254)
                 || (b[0] == 172 && b[1] is >= 16 and <= 31)
                 || (b[0] == 192 && b[1] == 168));
    }
}
