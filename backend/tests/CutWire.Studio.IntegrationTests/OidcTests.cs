using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using CutWire.Studio.Infrastructure.Oidc;
using CutWire.Studio.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.EntityFrameworkCore.Models;
using Shouldly;

namespace CutWire.Studio.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class OidcTests(PostgresFixture postgres) : IDisposable
{
    private const string Scope = "openid profile email offline_access forge:read";

    private readonly StudioFactory _factory = new(postgres.ConnectionString,
        configure: b => b.ConfigureTestServices(s => s.AddSingleton<CutWire.Studio.Api.Endpoints.IApiContributor, TestEndpoints>()));

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _factory.Dispose();

    private static string AuthorizeUrl(string clientId, string redirectUri, string challenge, string scope = Scope, string state = "st4te") =>
        "/connect/authorize?" + AuthTestSupport.Q(
        [
            new("client_id", clientId), new("response_type", "code"), new("redirect_uri", redirectUri), new("scope", scope),
            new("code_challenge", challenge), new("code_challenge_method", "S256"), new("state", state),
        ]);

    private async Task<string> ConsentAndAuthorize(HttpClient browser, string url)
    {
        var first = await browser.GetAsync(url, Ct);
        first.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = first.Headers.Location!.OriginalString;
        location.ShouldStartWith("/account/consent?return=");

        var returnUrl = Uri.UnescapeDataString(location["/account/consent?return=".Length..]);
        returnUrl.ShouldBe(url);

        var info = await browser.GetAsync("/api/v1/account/consent-info?return=" + Uri.EscapeDataString(returnUrl), Ct);
        info.StatusCode.ShouldBe(HttpStatusCode.OK);

        var approve = await browser.PostJson("/api/v1/account/consent", new { @return = returnUrl, approve = true }, Ct);
        approve.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await approve.Json(Ct))["redirect"]!.GetValue<string>().ShouldBe(url);

        return url;
    }

    private static Dictionary<string, string> Query(Uri uri) =>
        uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));

    private async Task<JsonNode> Token(HttpClient client, params (string Key, string Value)[] fields)
    {
        var response = await client.PostAsync("/connect/token",
            new FormUrlEncodedContent(fields.Select(f => KeyValuePair.Create(f.Key, f.Value))), Ct);
        return await response.Content.ReadJson(Ct);
    }

    [Fact]
    public async Task AuthorizationCode_Pkce_EndToEnd_WithRefreshAndConsentMemory()
    {
        var browser = await _factory.SignedIn(Ct, handle: "drifter" + Guid.NewGuid().ToString("N")[..6]);
        var anonymous = _factory.Browser();
        var (verifier, challenge) = AuthTestSupport.Pkce();
        const string redirect = "http://127.0.0.1:54321/callback";
        var url = AuthorizeUrl("drift-desktop", redirect, challenge);

        var signInRedirect = await anonymous.GetAsync(url, Ct);
        signInRedirect.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        signInRedirect.Headers.Location!.OriginalString.ShouldStartWith("/account/sign-in?return=");

        await ConsentAndAuthorize(browser, url);

        var granted = await browser.GetAsync(url, Ct);
        granted.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var callback = granted.Headers.Location!;
        callback.GetLeftPart(UriPartial.Path).ShouldBe(redirect);
        var query = Query(callback);
        query["state"].ShouldBe("st4te");

        var tokens = _factory.Browser(cookies: false);
        (await Token(tokens, ("grant_type", "authorization_code"), ("code", query["code"]), ("redirect_uri", redirect),
            ("code_verifier", "wrong" + verifier), ("client_id", "drift-desktop")))["error"]!.GetValue<string>().ShouldBe("invalid_grant");

        var granted2 = await browser.GetAsync(url, Ct);
        var code = Query(granted2.Headers.Location!)["code"];
        var response = await Token(tokens, ("grant_type", "authorization_code"), ("code", code), ("redirect_uri", redirect),
            ("code_verifier", verifier), ("client_id", "drift-desktop"));
        var access = response["access_token"]!.GetValue<string>();
        var refresh = response["refresh_token"]!.GetValue<string>();
        response["id_token"].ShouldNotBeNull();
        response["scope"]!.GetValue<string>().Split(' ').ShouldBe(Scope.Split(' '), ignoreOrder: true);
        access.Split('.').Length.ShouldBe(3);

        var api = _factory.Browser(cookies: false);
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        var me = await api.GetAsync("/api/v1/me", Ct);
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await me.Json(Ct))["handle"]!.GetValue<string>().ShouldStartWith("drifter");
        (await api.GetAsync("/api/v1/_test/forge-read", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await api.GetAsync("/api/v1/_test/forge-write", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.GetAsync("/api/v1/account/connected-apps", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var userInfo = await (await api.GetAsync("/connect/userinfo", Ct)).Json(Ct);
        userInfo["sub"].ShouldNotBeNull();
        userInfo["email_verified"]!.GetValue<bool>().ShouldBeTrue();

        var refreshed = await Token(tokens, ("grant_type", "refresh_token"), ("refresh_token", refresh), ("client_id", "drift-desktop"));
        refreshed["access_token"].ShouldNotBeNull();
        var rotated = refreshed["refresh_token"]!.GetValue<string>();
        rotated.ShouldNotBe(refresh);

        var apps = await (await browser.GetAsync("/api/v1/account/connected-apps", Ct)).Json(Ct);
        apps.AsArray().Count.ShouldBe(1);
        apps[0]!["clientName"]!.GetValue<string>().ShouldBe("Drift");
        (await browser.DeleteAsync("/api/v1/account/connected-apps/" + apps[0]!["id"]!.GetValue<string>(), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Token(tokens, ("grant_type", "refresh_token"), ("refresh_token", rotated), ("client_id", "drift-desktop")))["error"]!.GetValue<string>().ShouldBe("invalid_grant");
        (await browser.GetAsync(url, Ct)).Headers.Location!.OriginalString.ShouldStartWith("/account/consent");
    }

    [Fact]
    public async Task Authorize_WithoutPkce_IsRejected()
    {
        var browser = await _factory.SignedIn(Ct);
        var url = "/connect/authorize?" + AuthTestSupport.Q(
            [new("client_id", "drift-desktop"), new("response_type", "code"), new("redirect_uri", "http://127.0.0.1:1234/callback"), new("scope", "openid")]);

        var response = await browser.GetAsync(url, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("error:invalid_request");
    }

    [Theory]
    [InlineData("http://127.0.0.1:1/callback", true)]
    [InlineData("http://127.0.0.1:65000/callback", true)]
    [InlineData("cutwire://auth/callback", true)]
    [InlineData("http://127.0.0.1:5000/other", false)]
    [InlineData("http://localhost:5000/callback", false)]
    [InlineData("https://127.0.0.1:5000/callback", false)]
    [InlineData("http://example.com/callback", false)]
    public async Task Authorize_RedirectUriMatching_IsPortAgnosticOnlyForRegisteredLoopback(string redirect, bool accepted)
    {
        var browser = await _factory.SignedIn(Ct);
        var (_, challenge) = AuthTestSupport.Pkce();

        var response = await browser.GetAsync(AuthorizeUrl("drift-desktop", redirect, challenge), Ct);

        if (accepted)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            response.Headers.Location!.OriginalString.ShouldStartWith("/account/consent");
        }
        else
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("error:invalid_request");
        }
    }

    [Fact]
    public async Task Consent_Deny_RedirectsWithAccessDenied()
    {
        var browser = await _factory.SignedIn(Ct);
        var (_, challenge) = AuthTestSupport.Pkce();
        var url = AuthorizeUrl("drift-desktop", "http://127.0.0.1:4000/callback", challenge, state: "abc");

        var denied = await browser.PostJson("/api/v1/account/consent", new { @return = url, approve = false }, Ct);

        denied.StatusCode.ShouldBe(HttpStatusCode.OK);
        var redirect = new Uri((await denied.Json(Ct))["redirect"]!.GetValue<string>());
        redirect.GetLeftPart(UriPartial.Path).ShouldBe("http://127.0.0.1:4000/callback");
        Query(redirect)["error"].ShouldBe("access_denied");
        Query(redirect)["state"].ShouldBe("abc");
    }

    [Fact]
    public async Task DeviceFlow_ApprovedInBrowser_YieldsTokens()
    {
        var browser = await _factory.SignedIn(Ct);
        var device = _factory.Browser(cookies: false);

        var start = await device.PostAsync("/connect/device", new FormUrlEncodedContent(
            [KeyValuePair.Create("client_id", "drift-desktop"), KeyValuePair.Create("scope", "openid forge:read")]), Ct);
        start.StatusCode.ShouldBe(HttpStatusCode.OK);
        var started = await start.Json(Ct);
        var userCode = started["user_code"]!.GetValue<string>();
        var deviceCode = started["device_code"]!.GetValue<string>();
        started["verification_uri"]!.GetValue<string>().ShouldEndWith("/connect/verify");

        var page = await browser.GetAsync("/connect/verify?user_code=" + Uri.EscapeDataString(userCode), Ct);
        page.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        page.Headers.Location!.OriginalString.ShouldStartWith("/account/device?user_code=");
        page.Headers.Location.OriginalString.ShouldContain("client=Drift");

        var anonymousPage = await _factory.Browser().GetAsync("/connect/verify?user_code=" + Uri.EscapeDataString(userCode), Ct);
        anonymousPage.Headers.Location!.OriginalString.ShouldStartWith("/account/sign-in?return=");

        var bad = await browser.GetAsync("/connect/verify?user_code=ZZZZ-ZZZZ", Ct);
        bad.Headers.Location!.OriginalString.ShouldBe("/account/device?error=invalid_code");

        var noCsrf = _factory.Browser();
        noCsrf.DefaultRequestHeaders.Remove("X-Requested-With");
        (await noCsrf.PostAsync("/connect/verify", new FormUrlEncodedContent(
            [KeyValuePair.Create("user_code", userCode), KeyValuePair.Create("submit", "accept")]), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var accept = await browser.PostAsync("/connect/verify", new FormUrlEncodedContent(
            [KeyValuePair.Create("user_code", userCode), KeyValuePair.Create("submit", "accept")]), Ct);
        accept.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        accept.Headers.Location!.OriginalString.ShouldBe("/account/device?result=approved");

        var tokens = await Token(device, ("grant_type", "urn:ietf:params:oauth:grant-type:device_code"),
            ("device_code", deviceCode), ("client_id", "drift-desktop"));
        tokens["access_token"].ShouldNotBeNull();
        tokens["scope"]!.GetValue<string>().ShouldBe("openid forge:read");
    }

    [Fact]
    public async Task DeviceFlow_Denied_ReturnsAccessDenied()
    {
        var browser = await _factory.SignedIn(Ct);
        var device = _factory.Browser(cookies: false);
        var started = await (await device.PostAsync("/connect/device", new FormUrlEncodedContent(
            [KeyValuePair.Create("client_id", "drift-desktop"), KeyValuePair.Create("scope", "openid")]), Ct)).Json(Ct);

        var deny = await browser.PostAsync("/connect/verify", new FormUrlEncodedContent(
            [KeyValuePair.Create("user_code", started["user_code"]!.GetValue<string>()), KeyValuePair.Create("submit", "deny")]), Ct);
        deny.Headers.Location!.OriginalString.ShouldBe("/account/device?result=denied");

        var tokens = await Token(device, ("grant_type", "urn:ietf:params:oauth:grant-type:device_code"),
            ("device_code", started["device_code"]!.GetValue<string>()), ("client_id", "drift-desktop"));
        tokens["error"]!.GetValue<string>().ShouldBe("access_denied");
    }

    [Fact]
    public async Task DynamicRegistration_ThenAuthorize_ThenUnusedClientsAreCleanedUp()
    {
        var client = _factory.Browser(cookies: false);

        (await client.PostJson("/connect/register", new { client_name = "x", redirect_uris = new[] { "http://example.com/cb" } }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var custom = await client.PostJson("/connect/register", new { client_name = "x", redirect_uris = new[] { "cutwire://cb" } }, Ct);
        (await custom.Json(Ct))["error"]!.GetValue<string>().ShouldBe("invalid_redirect_uri");
        (await client.PostJson("/connect/register", new { client_name = "x", redirect_uris = new[] { "https://app.example/cb" }, token_endpoint_auth_method = "client_secret_basic" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var register = await client.PostJson("/connect/register", new
        {
            client_name = "Claude Desktop",
            redirect_uris = new[] { "http://127.0.0.1:33333/callback", "https://app.example/cb" },
            grant_types = new[] { "authorization_code", "refresh_token" },
            response_types = new[] { "code" },
            token_endpoint_auth_method = "none",
        }, Ct);
        register.StatusCode.ShouldBe(HttpStatusCode.Created);
        var registered = await register.Json(Ct);
        var clientId = registered["client_id"]!.GetValue<string>();
        clientId.ShouldStartWith("dyn_");
        registered["token_endpoint_auth_method"]!.GetValue<string>().ShouldBe("none");

        var unused = (await (await client.PostJson("/connect/register", new { client_name = "Unused", redirect_uris = new[] { "https://unused.example/cb" } }, Ct)).Json(Ct))["client_id"]!.GetValue<string>();

        var browser = await _factory.SignedIn(Ct);
        var (verifier, challenge) = AuthTestSupport.Pkce();
        var url = AuthorizeUrl(clientId, "http://127.0.0.1:44444/callback", challenge, "openid forge:read offline_access");
        await ConsentAndAuthorize(browser, url);
        var withResource = url + "&resource=" + Uri.EscapeDataString("http://localhost:8087/mcp");
        var authorized = await browser.GetAsync(withResource, Ct);
        var code = Query(authorized.Headers.Location!)["code"];
        var tokens = await Token(client, ("grant_type", "authorization_code"), ("code", code), ("redirect_uri", "http://127.0.0.1:44444/callback"),
            ("code_verifier", verifier), ("client_id", clientId), ("resource", "http://localhost:8087/mcp"));
        tokens["access_token"].ShouldNotBeNull();
        var payload = tokens["access_token"]!.GetValue<string>().Split('.')[1];
        var claims = JsonNode.Parse(Convert.FromBase64String(payload.Replace('-', '+').Replace('_', '/').PadRight((payload.Length + 3) / 4 * 4, '=')))!;
        (claims["aud"] is JsonArray aud ? aud[0]!.GetValue<string>() : claims["aud"]!.GetValue<string>()).ShouldBe("http://localhost:8087/mcp");
        claims["scope"]!.GetValue<string>().ShouldContain("forge:read");

        var drift = AuthorizeUrl(clientId, "http://127.0.0.1:44444/callback", challenge, "drift");
        var forbiddenScope = await browser.GetAsync(drift, Ct);
        forbiddenScope.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await forbiddenScope.Content.ReadAsStringAsync(Ct)).ShouldContain("error:invalid_request");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StudioDbContext>();
        var old = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [OidcClients.DynamicProperty] = true,
            [OidcClients.CreatedAtProperty] = DateTimeOffset.UtcNow.AddDays(-8),
        });
        await db.Database.ExecuteSqlAsync($"update openiddict_applications set properties = {old} where client_id in ({clientId}, {unused})", Ct);

        await _factory.Services.GetServices<CutWire.Studio.Infrastructure.Jobs.IRecurringJob>().OfType<DynamicClientCleanupJob>().Single().RunAsync(Ct);

        var remaining = await db.Set<OpenIddictEntityFrameworkCoreApplication<Guid>>().Select(a => a.ClientId).ToListAsync(Ct);
        remaining.ShouldContain(clientId);
        remaining.ShouldNotContain(unused);
        remaining.ShouldContain("drift-desktop");
    }

    [Fact]
    public async Task Metadata_DiscoveryResourceAndJwks()
    {
        var client = _factory.Browser(cookies: false);

        var discovery = await (await client.GetAsync("/.well-known/openid-configuration", Ct)).Json(Ct);
        discovery["issuer"]!.GetValue<string>().ShouldBe("http://localhost:8087/");
        discovery["registration_endpoint"]!.GetValue<string>().ShouldEndWith("/connect/register");
        discovery["authorization_endpoint"]!.GetValue<string>().ShouldEndWith("/connect/authorize");
        discovery["device_authorization_endpoint"]!.GetValue<string>().ShouldEndWith("/connect/device");
        discovery["code_challenge_methods_supported"]!.AsArray().Select(c => c!.GetValue<string>()).ShouldContain("S256");
        discovery["grant_types_supported"]!.AsArray().Select(c => c!.GetValue<string>())
            .ShouldBe(["authorization_code", "refresh_token", "urn:ietf:params:oauth:grant-type:device_code"], ignoreOrder: true);
        discovery["scopes_supported"]!.AsArray().Select(c => c!.GetValue<string>()).ShouldContain("forge:write");

        var oauth = await (await client.GetAsync("/.well-known/oauth-authorization-server", Ct)).Json(Ct);
        oauth["token_endpoint"]!.GetValue<string>().ShouldBe(discovery["token_endpoint"]!.GetValue<string>());

        var jwks = await (await client.GetAsync(discovery["jwks_uri"]!.GetValue<string>().Replace("http://localhost:8087", ""), Ct)).Json(Ct);
        jwks["keys"]!.AsArray().Count.ShouldBeGreaterThan(0);

        foreach (var path in new[] { "/.well-known/oauth-protected-resource", "/.well-known/oauth-protected-resource/mcp" })
        {
            var resource = await client.GetAsync(path, Ct);
            resource.StatusCode.ShouldBe(HttpStatusCode.OK);
            resource.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
            var json = await resource.Json(Ct);
            json["resource"]!.GetValue<string>().ShouldBe("http://localhost:8087/mcp");
            json["authorization_servers"]![0]!.GetValue<string>().ShouldBe(discovery["issuer"]!.GetValue<string>());
            json["scopes_supported"]!.AsArray().Count.ShouldBe(3);
        }
    }
}
