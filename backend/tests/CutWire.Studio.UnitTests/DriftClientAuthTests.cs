using System.Text;
using CutWire.Studio.Api.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace CutWire.Studio.UnitTests;

public class DriftClientAuthTests
{
    private const string Key = "5409a0bebab4446f88e51d06024a5cf9a694ee141a6a1c6be7a49e45b6687438";
    private const string ClientId = "ac2acbdfa3a4e8c29c47d9105914e766ba71f3e74108e040aefcc2267ed27837";
    private const string Nonce = "00112233445566778899aabbccddeeff";
    private const string Target = "/api/v1/stock/search?q=a%20b+c%C3%A9&type=video";
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"a":1}""");

    private static string Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

    private static ServiceProvider Services(params string[] keys) => new ServiceCollection()
        .AddLogging()
        .AddMemoryCache()
        .AddSingleton<IOptions<DriftClientOptions>>(Options.Create(new DriftClientOptions { Keys = keys }))
        .BuildServiceProvider();

    private static DefaultHttpContext Request(IServiceProvider services, string timestamp, string nonce = Nonce, string target = Target, string key = Key, string? signature = null)
    {
        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.Method = "POST";
        http.Request.Body = new MemoryStream(Body);
        http.Features.Get<IHttpRequestFeature>()!.RawTarget = target;
        http.Request.Headers["X-Cutwire-Client"] = ClientId;
        http.Request.Headers["X-Cutwire-Timestamp"] = timestamp;
        http.Request.Headers["X-Cutwire-Nonce"] = nonce;
        http.Request.Headers["X-Cutwire-App"] = "Drift/1.4.0-beta.1 (linux)";
        http.Request.Headers["X-Cutwire-Distribution"] = "Flatpak";
        http.Request.Headers["X-Cutwire-Signature"] = signature ?? DriftClientAuth.SignatureFor(key, timestamp, nonce, ClientId, "POST", target, Body);
        return http;
    }

    private static async Task<int?> Verify(HttpContext http)
    {
        var failure = await DriftClientAuth.VerifyAsync(http, http.Request.Headers["X-Cutwire-Signature"].ToString());
        if (failure is null) return null;
        http.Response.Body = new MemoryStream();
        await failure.ExecuteAsync(http);
        return http.Response.StatusCode;
    }

    [Fact]
    public void SigningMatchesTheDriftClientVector()
    {
        DriftClientAuth.SignatureFor(Key, "1700000000", Nonce, ClientId, "POST", Target, Body)
            .ShouldBe("37cc3e32e9cd810443ad3a7708792822571de93c4ec8fc6a0a1c69836732c730");
    }

    [Fact]
    public async Task ValidRequestIsVerifiedAgainstTheRawTarget()
    {
        using var services = Services("other", Key);
        var http = Request(services, Now);

        (await Verify(http)).ShouldBeNull();

        var client = http.DriftClientOf().ShouldNotBeNull();
        client.ClientId.ShouldBe(ClientId);
        client.Platform.ShouldBe("linux");
        client.AppVersion.ShouldNotBeNull().Major.ShouldBe(1);
        client.Distribution.ShouldBe("flatpak");
        http.Request.Body.Position.ShouldBe(0);
    }

    [Theory]
    [InlineData("/api/v1/stock/search?q=a%20b+c%C3%A9&type=video")]
    [InlineData("/api/v1/stock/search?q=a b+c%c3%a9&type=video")]
    public async Task EncodedTargetsAreSignedAsSent(string target)
    {
        using var services = Services(Key);

        (await Verify(Request(services, Now, target: target))).ShouldBeNull();
    }

    [Fact]
    public async Task ReEncodedTargetIsRejected()
    {
        using var services = Services(Key);
        var signed = DriftClientAuth.SignatureFor(Key, Now, Nonce, ClientId, "POST", Target, Body);
        var http = Request(services, Now, target: "/api/v1/stock/search?q=a%20b%2Bc%C3%A9&type=video", signature: signed);

        (await Verify(http)).ShouldBe(401);
    }

    [Fact]
    public async Task BadSignatureIsRejected()
    {
        using var services = Services(Key);

        (await Verify(Request(services, Now, key: "wrong-key"))).ShouldBe(401);
        (await Verify(Request(services, Now, nonce: "ffeeddccbbaa99887766554433221100", signature: new string('0', 64)))).ShouldBe(401);
    }

    [Fact]
    public async Task SkewIsRejectedWithADateHeader()
    {
        using var services = Services(Key);
        var stale = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 301).ToString();
        var http = Request(services, stale);

        (await Verify(http)).ShouldBe(401);
        http.Response.Headers.Date.ToString().ShouldNotBeEmpty();
    }

    [Fact]
    public async Task ReplayedNonceIsRejected()
    {
        using var services = Services(Key);
        var now = Now;

        (await Verify(Request(services, now))).ShouldBeNull();
        (await Verify(Request(services, now))).ShouldBe(401);
    }

    [Fact]
    public async Task NoConfiguredKeysRejectsSignedRequests()
    {
        using var services = Services();

        (await Verify(Request(services, Now))).ShouldBe(401);
    }
}
