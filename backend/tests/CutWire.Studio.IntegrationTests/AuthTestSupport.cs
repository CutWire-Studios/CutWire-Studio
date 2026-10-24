using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;

namespace CutWire.Studio.IntegrationTests;

public static class AuthTestSupport
{
    public const string Password = "correct horse battery staple";

    public static HttpClient Browser(this StudioFactory factory, bool cookies = true) => factory.CreateBrowser(cookies);

    private static HttpClient CreateBrowser(this StudioFactory factory, bool cookies)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = cookies,
        });
        client.DefaultRequestHeaders.Add("X-Requested-With", "test");
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:8087");
        return client;
    }

    public static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    public static string NewHandle() => "u" + Guid.NewGuid().ToString("N")[..12];

    public static Task<HttpResponseMessage> PostJson(this HttpClient client, string url, object? body, CancellationToken ct) =>
        client.PostAsJsonAsync(url, body ?? new { }, ct);

    public static async Task<JsonNode> Json(this HttpResponseMessage response, CancellationToken ct) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;

    public static async Task<string> SignUpAndVerify(this StudioFactory factory, HttpClient client, string email, string handle, CancellationToken ct)
    {
        (await client.PostJson("/api/v1/account/sign-up", new { email, password = Password, handle }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var (userId, token) = factory.Emails.VerificationLink(email);
        (await client.PostJson("/api/v1/account/verify-email", new { userId, token }, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return userId;
    }

    public static async Task<HttpClient> SignedIn(this StudioFactory factory, CancellationToken ct, string? email = null, string? handle = null)
    {
        email ??= NewEmail();
        var client = factory.Browser();
        await factory.SignUpAndVerify(client, email, handle ?? NewHandle(), ct);
        var response = await client.PostJson("/api/v1/account/sign-in", new { email, password = Password }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Json(ct))["status"]!.GetValue<string>().ShouldBe("ok");
        return client;
    }

    public static string Totp(string base32Secret, DateTimeOffset? at = null)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in base32Secret.ToUpperInvariant().Where(c => c != '='))
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        var counter = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / 30;
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(counterBytes);
        var hash = HMACSHA1.HashData(bytes.ToArray(), counterBytes);
        var offset = hash[^1] & 0xf;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    public static (string Verifier, string Challenge) Pkce()
    {
        var verifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (verifier, challenge);
    }

    public static async Task<JsonNode> ReadJson(this HttpContent content, CancellationToken ct) =>
        JsonNode.Parse(await content.ReadAsStringAsync(ct))!;

    public static string Q(IEnumerable<KeyValuePair<string, string>> parameters) =>
        string.Join("&", parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
}
