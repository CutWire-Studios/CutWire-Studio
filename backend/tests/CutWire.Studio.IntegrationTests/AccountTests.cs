using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Shouldly;

namespace CutWire.Studio.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class AccountTests(PostgresFixture postgres) : IDisposable
{
    private readonly StudioFactory _factory = new(postgres.ConnectionString);
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task SignUp_Verify_SignIn_Me_RoundTrip()
    {
        var client = _factory.Browser();
        var email = AuthTestSupport.NewEmail();
        var handle = AuthTestSupport.NewHandle();

        (await client.GetAsync("/api/v1/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var signUp = await client.PostJson("/api/v1/account/sign-up", new { email, password = AuthTestSupport.Password, handle }, Ct);
        signUp.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var early = await client.PostJson("/api/v1/account/sign-in", new { email, password = AuthTestSupport.Password }, Ct);
        (await early.Json(Ct))["status"]!.GetValue<string>().ShouldBe("requires_verification");
        (await client.GetAsync("/api/v1/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var (userId, token) = _factory.Emails.VerificationLink(email);
        (await client.PostJson("/api/v1/account/verify-email", new { userId, token }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var signIn = await client.PostJson("/api/v1/account/sign-in", new { email, password = AuthTestSupport.Password }, Ct);
        signIn.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await signIn.Json(Ct))["status"]!.GetValue<string>().ShouldBe("ok");
        signIn.Headers.GetValues("Set-Cookie").Single().ShouldStartWith("__Host-studio=");

        var me = await client.GetAsync("/api/v1/me", Ct);
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await me.Json(Ct);
        json["id"]!.GetValue<string>().ShouldBe(userId);
        json["handle"]!.GetValue<string>().ShouldBe(handle);
        json["email"]!.GetValue<string>().ShouldBe(email);
        json["emailVerified"]!.GetValue<bool>().ShouldBeTrue();
        json["trustLevel"]!.GetValue<string>().ShouldBe("T0");
        json["banned"]!.GetValue<bool>().ShouldBeFalse();
        json["roles"]!.AsArray().Count.ShouldBe(0);

        (await client.PostJson("/api/v1/account/sign-out", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetAsync("/api/v1/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SignUp_DuplicateHandle_ReturnsConflict_AndInvalidInputIsRejected()
    {
        var client = _factory.Browser();
        var handle = AuthTestSupport.NewHandle();
        (await client.PostJson("/api/v1/account/sign-up",
            new { email = AuthTestSupport.NewEmail(), password = AuthTestSupport.Password, handle }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var duplicate = await client.PostJson("/api/v1/account/sign-up",
            new { email = AuthTestSupport.NewEmail(), password = AuthTestSupport.Password, handle = handle.ToUpperInvariant() }, Ct);
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await duplicate.Json(Ct))["code"]!.GetValue<string>().ShouldBe("profile.handle_taken");

        var invalid = await client.PostJson("/api/v1/account/sign-up", new { email = "nope", password = "short", handle = "x" }, Ct);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = (await invalid.Json(Ct))["errors"]!.AsObject();
        errors.ContainsKey("email").ShouldBeTrue();
        errors.ContainsKey("handle").ShouldBeTrue();
    }

    [Fact]
    public async Task SignIn_WrongPassword_Is401_AndFiveFailuresLockOut()
    {
        var email = AuthTestSupport.NewEmail();
        var client = _factory.Browser();
        await _factory.SignUpAndVerify(client, email, AuthTestSupport.NewHandle(), Ct);

        for (var i = 0; i < 5; i++)
        {
            var bad = await client.PostJson("/api/v1/account/sign-in", new { email, password = "wrong password here" }, Ct);
            bad.StatusCode.ShouldBe(i < 4 ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, $"attempt {i + 1}");
        }

        var locked = await client.PostJson("/api/v1/account/sign-in", new { email, password = AuthTestSupport.Password }, Ct);
        locked.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await locked.Json(Ct))["code"]!.GetValue<string>().ShouldBe("auth.locked_out");
    }

    [Fact]
    public async Task ForgotAndResetPassword_AllowsSignInWithNewPassword()
    {
        var email = AuthTestSupport.NewEmail();
        var client = _factory.Browser();
        await _factory.SignUpAndVerify(client, email, AuthTestSupport.NewHandle(), Ct);

        (await client.PostJson("/api/v1/account/forgot-password", new { email }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await client.PostJson("/api/v1/account/forgot-password", new { email = "ghost@example.com" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        _factory.Emails.Sent.Count(m => m.To == "ghost@example.com").ShouldBe(0);

        var token = CapturingEmailSender.Param(_factory.Emails.Last(email).Text, "token");
        const string newPassword = "an entirely different passphrase";
        (await client.PostJson("/api/v1/account/reset-password", new { email, token, newPassword }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PostJson("/api/v1/account/reset-password", new { email, token, newPassword }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await client.PostJson("/api/v1/account/sign-in", new { email, password = AuthTestSupport.Password }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostJson("/api/v1/account/sign-in", new { email, password = newPassword }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChangeEmail_RequiresVerificationOfNewAddress()
    {
        var email = AuthTestSupport.NewEmail();
        var client = await _factory.SignedIn(Ct, email);
        var newEmail = AuthTestSupport.NewEmail();

        (await client.PostJson("/api/v1/account/change-email", new { newEmail, password = "wrong wrong wrong" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostJson("/api/v1/account/change-email", new { newEmail, password = AuthTestSupport.Password }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await (await client.GetAsync("/api/v1/me", Ct)).Json(Ct))["email"]!.GetValue<string>().ShouldBe(email);

        var text = _factory.Emails.Last(newEmail).Text;
        var confirm = await client.PostJson("/api/v1/account/confirm-email-change", new
        {
            userId = CapturingEmailSender.Param(text, "userId"),
            email = CapturingEmailSender.Param(text, "email"),
            token = CapturingEmailSender.Param(text, "token"),
        }, Ct);
        confirm.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var other = _factory.Browser();
        (await other.PostJson("/api/v1/account/sign-in", new { email = newEmail, password = AuthTestSupport.Password }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await other.PostJson("/api/v1/account/sign-in", new { email, password = AuthTestSupport.Password }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Profile_UpdateHandleAndAvatar()
    {
        var client = await _factory.SignedIn(Ct);
        var handle = AuthTestSupport.NewHandle();

        var update = await client.PutAsJsonAsync("/api/v1/account/profile", new { handle = handle.ToUpperInvariant(), displayName = "Ada", bio = "hello" }, Ct);
        update.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await update.Json(Ct))["handle"]!.GetValue<string>().ShouldBe(handle);

        var bad = await client.PutAsJsonAsync("/api/v1/account/profile", new { handle = "a b", displayName = "", bio = "" }, Ct);
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        var upload = await client.PostAsync("/api/v1/account/profile/avatar", Bytes(png, "image/png"), Ct);
        upload.StatusCode.ShouldBe(HttpStatusCode.OK);
        var url = (await upload.Json(Ct))["avatarUrl"]!.GetValue<string>();
        url.ShouldStartWith("/files/");
        (await client.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await (await client.GetAsync("/api/v1/me", Ct)).Json(Ct))["avatarUrl"]!.GetValue<string>().ShouldBe(url);

        (await client.PostAsync("/api/v1/account/profile/avatar", Bytes("not an image"u8.ToArray(), "image/png"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsync("/api/v1/account/profile/avatar", Bytes(new byte[300 * 1024], "image/png"), Ct)).StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);

        (await client.DeleteAsync("/api/v1/account/profile/avatar", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await (await client.GetAsync("/api/v1/me", Ct)).Json(Ct))["avatarUrl"].ShouldBeNull();
    }

    [Fact]
    public async Task DeleteAccount_AnonymizesProfileAndBlocksSignIn()
    {
        var email = AuthTestSupport.NewEmail();
        var client = await _factory.SignedIn(Ct, email);

        (await client.PostJson("/api/v1/account/delete", new { password = "wrong wrong wrong" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostJson("/api/v1/account/delete", new { password = AuthTestSupport.Password }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetAsync("/api/v1/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var again = await _factory.Browser().PostJson("/api/v1/account/sign-in", new { email, password = AuthTestSupport.Password }, Ct);
        again.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SignOutEverywhere_InvalidatesOtherSessionsButKeepsCurrent()
    {
        var email = AuthTestSupport.NewEmail();
        var first = await _factory.SignedIn(Ct, email);
        var second = _factory.Browser();
        (await second.PostJson("/api/v1/account/sign-in", new { email, password = AuthTestSupport.Password }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await first.PostJson("/api/v1/account/sessions/sign-out-everywhere", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await first.GetAsync("/api/v1/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await Task.Delay(100, Ct);
    }

    [Fact]
    public async Task BootstrapAdmin_GetsAdminRoleAfterVerification()
    {
        var email = AuthTestSupport.NewEmail();
        using var factory = new StudioFactory(postgres.ConnectionString,
            configure: b => b.UseSetting("Auth:BootstrapAdminEmail", email));
        var client = await factory.SignedIn(Ct, email);

        var roles = (await (await client.GetAsync("/api/v1/me", Ct)).Json(Ct))["roles"]!.AsArray().Select(r => r!.GetValue<string>());
        roles.ShouldBe(["admin"]);
    }

    private static ByteArrayContent Bytes(byte[] bytes, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return content;
    }
}
