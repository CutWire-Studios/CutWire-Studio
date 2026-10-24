using System.Net;
using Shouldly;

namespace CutWire.Studio.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class TwoFactorTests(PostgresFixture postgres) : IDisposable
{
    private readonly StudioFactory _factory = new(postgres.ConnectionString);
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task EnableTotp_RequiresSecondStepOnSignIn_AndRecoveryCodeWorksOnce()
    {
        var email = AuthTestSupport.NewEmail();
        var client = await _factory.SignedIn(Ct, email);

        var setup = await (await client.PostJson("/api/v1/account/2fa/setup", null, Ct)).Json(Ct);
        var key = setup["sharedKey"]!.GetValue<string>();
        setup["otpAuthUri"]!.GetValue<string>().ShouldStartWith("otpauth://totp/");

        (await client.PostJson("/api/v1/account/2fa/enable", new { code = "000000" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var enabled = await client.PostJson("/api/v1/account/2fa/enable", new { code = AuthTestSupport.Totp(key) }, Ct);
        enabled.StatusCode.ShouldBe(HttpStatusCode.OK);
        var codes = (await enabled.Json(Ct))["recoveryCodes"]!.AsArray().Select(c => c!.GetValue<string>()).ToArray();
        codes.Length.ShouldBe(10);
        (await (await client.GetAsync("/api/v1/account/2fa", Ct)).Json(Ct))["recoveryCodesLeft"]!.GetValue<int>().ShouldBe(10);

        var second = _factory.Browser();
        var first = await second.PostJson("/api/v1/account/sign-in", new { email, password = AuthTestSupport.Password }, Ct);
        (await first.Json(Ct))["status"]!.GetValue<string>().ShouldBe("requires_2fa");
        (await second.GetAsync("/api/v1/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await second.PostJson("/api/v1/account/sign-in-2fa", new { code = "123456" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await second.PostJson("/api/v1/account/sign-in-2fa", new { code = AuthTestSupport.Totp(key) }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await second.GetAsync("/api/v1/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var third = _factory.Browser();
        await third.PostJson("/api/v1/account/sign-in", new { email, password = AuthTestSupport.Password }, Ct);
        (await third.PostJson("/api/v1/account/sign-in-2fa", new { recoveryCode = codes[0] }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var fourth = _factory.Browser();
        await fourth.PostJson("/api/v1/account/sign-in", new { email, password = AuthTestSupport.Password }, Ct);
        (await fourth.PostJson("/api/v1/account/sign-in-2fa", new { recoveryCode = codes[0] }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await client.PostJson("/api/v1/account/2fa/disable", new { password = "wrong wrong wrong" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostJson("/api/v1/account/2fa/disable", new { password = AuthTestSupport.Password }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var plain = _factory.Browser();
        (await (await plain.PostJson("/api/v1/account/sign-in", new { email, password = AuthTestSupport.Password }, Ct)).Json(Ct))["status"]!.GetValue<string>().ShouldBe("ok");
    }

    [Fact]
    public async Task Passkey_OptionsAreIssued_AndListIsEmpty()
    {
        var client = await _factory.SignedIn(Ct);

        var creation = await client.PostJson("/api/v1/account/passkeys/creation-options", null, Ct);
        creation.StatusCode.ShouldBe(HttpStatusCode.OK);
        var options = await creation.Json(Ct);
        options["rp"]!["id"]!.GetValue<string>().ShouldBe("localhost");
        options["challenge"].ShouldNotBeNull();

        var request = await _factory.Browser().PostJson("/api/v1/account/passkeys/request-options", null, Ct);
        request.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await request.Json(Ct))["challenge"].ShouldNotBeNull();

        (await (await client.GetAsync("/api/v1/account/passkeys", Ct)).Json(Ct)).AsArray().Count.ShouldBe(0);
        (await client.DeleteAsync("/api/v1/account/passkeys/AAAA", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _factory.Browser().PostJson("/api/v1/account/passkeys/sign-in", new { credential = new { id = "x" } }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
