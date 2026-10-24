using System.Net;
using System.Text;
using AspNet.Security.OAuth.GitHub;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace CutWire.Studio.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class ExternalLoginTests(PostgresFixture postgres)
{
    private readonly FakeGitHub _github = new();
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private StudioFactory Create() => new(postgres.ConnectionString, configure: b =>
    {
        b.UseSetting("Auth:External:GitHub:ClientId", "test-client");
        b.UseSetting("Auth:External:GitHub:ClientSecret", "test-secret");
        b.ConfigureTestServices(s => s.Configure<GitHubAuthenticationOptions>(
            GitHubAuthenticationDefaults.AuthenticationScheme, o => o.BackchannelHttpHandler = _github));
    });

    private async Task<HttpResponseMessage> RunProviderFlow(StudioFactory factory, HttpClient browser, string returnUrl = "/community")
    {
        var challenge = await browser.GetAsync("/api/v1/account/external/github?return=" + Uri.EscapeDataString(returnUrl), Ct);
        challenge.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = challenge.Headers.Location!;
        location.Host.ShouldBe("github.com");
        var state = Uri.UnescapeDataString(location.Query.TrimStart('?').Split('&').Single(p => p.StartsWith("state=", StringComparison.Ordinal))[6..]);

        var providerCallback = await browser.GetAsync("/signin-github?code=abc&state=" + Uri.EscapeDataString(state), Ct);
        providerCallback.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return await browser.GetAsync(providerCallback.Headers.Location!.OriginalString, Ct);
    }

    [Fact]
    public async Task ProvidersList_ReflectsConfiguration()
    {
        using var enabled = Create();
        var list = await (await enabled.Browser().GetAsync("/api/v1/account/external", Ct)).Json(Ct);
        list.AsArray().Select(p => p!["name"]!.GetValue<string>()).ShouldBe(["GitHub"]);

        using var none = new StudioFactory(postgres.ConnectionString);
        (await (await none.Browser().GetAsync("/api/v1/account/external", Ct)).Json(Ct)).AsArray().Count.ShouldBe(0);
        (await none.Browser().GetAsync("/api/v1/account/external/github", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task NewExternalUser_MustCompleteProfile_ThenSignsInDirectlyNextTime()
    {
        using var factory = Create();
        var email = AuthTestSupport.NewEmail();
        _github.Setup(id: Random.Shared.Next(), email: email, verified: true);
        var browser = factory.Browser();

        var pending = await RunProviderFlow(factory, browser);
        pending.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        pending.Headers.Location!.OriginalString.ShouldBe("/account/pending-profile?return=%2Fcommunity");
        (await browser.GetAsync("/api/v1/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var info = await (await browser.GetAsync("/api/v1/account/external/pending", Ct)).Json(Ct);
        info["email"]!.GetValue<string>().ShouldBe(email);
        info["suggestedHandle"]!.GetValue<string>().ShouldBe("octo-cat");

        var handle = AuthTestSupport.NewHandle();
        (await browser.PostJson("/api/v1/account/external/complete", new { handle = "!!", displayName = "x" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await browser.PostJson("/api/v1/account/external/complete", new { handle, displayName = "Octo" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var me = await (await browser.GetAsync("/api/v1/me", Ct)).Json(Ct);
        me["handle"]!.GetValue<string>().ShouldBe(handle);
        me["email"]!.GetValue<string>().ShouldBe(email);
        me["emailVerified"]!.GetValue<bool>().ShouldBeTrue();

        var again = factory.Browser();
        var signedIn = await RunProviderFlow(factory, again);
        signedIn.Headers.Location!.OriginalString.ShouldBe("/community");
        (await again.GetAsync("/api/v1/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnverifiedProviderEmail_IsRejected()
    {
        using var factory = Create();
        _github.Setup(id: Random.Shared.Next(), email: AuthTestSupport.NewEmail(), verified: false);

        var result = await RunProviderFlow(factory, factory.Browser());

        result.Headers.Location!.OriginalString.ShouldStartWith("/account/sign-in?error=external_email_unverified");
    }

    [Fact]
    public async Task ExistingVerifiedAccount_IsLinkedByEmail_UnverifiedIsNot()
    {
        using var factory = Create();
        var verified = AuthTestSupport.NewEmail();
        await factory.SignUpAndVerify(factory.Browser(), verified, AuthTestSupport.NewHandle(), Ct);
        _github.Setup(id: Random.Shared.Next(), email: verified, verified: true);

        var browser = factory.Browser();
        var result = await RunProviderFlow(factory, browser);
        result.Headers.Location!.OriginalString.ShouldBe("/community");
        (await (await browser.GetAsync("/api/v1/me", Ct)).Json(Ct))["email"]!.GetValue<string>().ShouldBe(verified);

        var unverified = AuthTestSupport.NewEmail();
        (await factory.Browser().PostJson("/api/v1/account/sign-up",
            new { email = unverified, password = AuthTestSupport.Password, handle = AuthTestSupport.NewHandle() }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        _github.Setup(id: Random.Shared.Next(), email: unverified, verified: true);

        var conflict = await RunProviderFlow(factory, factory.Browser());
        conflict.Headers.Location!.OriginalString.ShouldStartWith("/account/sign-in?error=external_email_conflict");
    }

    private sealed class FakeGitHub : HttpMessageHandler
    {
        private int _id;
        private string _email = "";
        private bool _verified;

        public void Setup(int id, string email, bool verified)
        {
            _id = id;
            _email = email;
            _verified = verified;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var json = path switch
            {
                "/login/oauth/access_token" => """{"access_token":"gho_test","token_type":"bearer","scope":"user:email"}""",
                "/user" => $$"""{"id":{{_id}},"login":"octo-cat","name":"Octo Cat","email":null}""",
                "/user/emails" => $$"""[{"email":"{{_email}}","primary":true,"verified":{{_verified.ToString().ToLowerInvariant()}}}]""",
                _ => throw new InvalidOperationException("Unexpected request " + request.RequestUri),
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
