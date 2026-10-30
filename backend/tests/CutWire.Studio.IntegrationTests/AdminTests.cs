using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CutWire.Studio.Application.Admin;
using CutWire.Studio.Application.Moderation;
using CutWire.Studio.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace CutWire.Studio.IntegrationTests;

public static partial class AdminSupport
{
    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex Token();

    public static async Task<(string Body, string Token)> GetPage(this HttpClient client, string url, CancellationToken ct)
    {
        var response = await client.GetAsync(url, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, url);
        var body = await response.Content.ReadAsStringAsync(ct);
        return (body, Token().Match(body) is { Success: true } m ? m.Groups[1].Value : "");
    }

    public static async Task<string> PostForm(this HttpClient client, string page, string url, IEnumerable<(string, string)> fields, CancellationToken ct)
    {
        var (_, token) = await client.GetPage(page, ct);
        var form = fields.Select(f => new KeyValuePair<string, string>(f.Item1, f.Item2)).Append(new("__RequestVerificationToken", token));
        var response = await client.PostAsync(url, new FormUrlEncodedContent(form), ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var back = await client.GetAsync(response.Headers.Location!, ct);
        back.StatusCode.ShouldBe(HttpStatusCode.OK);
        return WebUtility.HtmlDecode(await back.Content.ReadAsStringAsync(ct));
    }
}

[Collection(PostgresCollection.Name)]
public sealed class AdminAreaTests(PostgresFixture postgres)
    : ModerationTestBase(postgres, b => b.UseSetting("Auth:BootstrapAdminEmail", BootstrapEmail))
{
    private const string BootstrapEmail = "bootstrap-admin@example.com";

    private static readonly string[] ModeratorPages = ["/admin", "/admin/queue?type=reported", "/admin/queue?type=hidden", "/admin/queue?type=flagged", "/admin/users", "/admin/audit"];
    private static readonly string[] AdminPages = ["/admin/admins", "/admin/stock", "/admin/distributions", "/admin/categories", "/admin/marketplace", "/admin/marketplacelayout", "/admin/marketplaceflag"];

    private Task<int> RoleCount(Guid userId, string role) => F.Db(db => db.Database
        .SqlQuery<int>($"select count(*)::int as \"Value\" from user_roles ur join roles r on r.id = ur.role_id where ur.user_id = {userId} and r.name = {role}")
        .SingleAsync(Ct));

    [Fact]
    public async Task Access_RolesDecideWhichPagesRender()
    {
        var (user, _, _) = await F.User("T3");
        var (mod, _) = await Staff("moderator");
        var (admin, _) = await Staff("admin");

        foreach (var url in ModeratorPages.Concat(AdminPages))
            (await user.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, url);
        foreach (var url in ModeratorPages)
            (await mod.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK, url);
        foreach (var url in AdminPages)
        {
            var denied = await mod.GetAsync(url, Ct);
            denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden, url);
            denied.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        }

        foreach (var url in ModeratorPages.Concat(AdminPages))
            (await admin.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK, url);

        var nav = await (await mod.GetAsync("/admin", Ct)).Content.ReadAsStringAsync(Ct);
        nav.ShouldContain("/admin/queue");
        nav.ShouldNotContain("/admin/admins");
        (await (await admin.GetAsync("/admin", Ct)).Content.ReadAsStringAsync(Ct)).ShouldContain("/admin/admins");
    }

    [Fact]
    public async Task Access_AdminBearerTokenIsNotAccepted()
    {
        var (admin, _) = await Staff("admin");
        var created = await admin.PostJson("/api/v1/account/tokens", new { name = "ci", scopes = new[] { "forge:read" } }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var secret = (await created.Json(Ct))["secret"]!.GetValue<string>();

        var bearer = F.Factory.Browser(cookies: false);
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        var response = await bearer.GetAsync("/admin", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldStartWith("/account/sign-in");
    }

    [Fact]
    public async Task Admins_GrantAndRevokeRolesThroughTheForm()
    {
        var (admin, _) = await Staff("admin");
        var (_, handle, targetId) = await F.User("T3");

        var granted = await admin.PostForm("/admin/admins", "/admin/admins?handler=Add", [("query", handle), ("role", "moderator")], Ct);
        granted.ShouldContain("Granted moderator.");
        (await RoleCount(targetId, "moderator")).ShouldBe(1);

        await admin.PostForm("/admin/admins", "/admin/admins?handler=Add", [("query", handle[..8]), ("role", "admin")], Ct);
        (await RoleCount(targetId, "admin")).ShouldBe(1);

        var revoked = await admin.PostForm("/admin/admins", "/admin/admins?handler=Set",
            [("id", targetId.ToString()), ("role", "admin"), ("granted", "false")], Ct);
        revoked.ShouldContain("Revoked admin.");
        (await RoleCount(targetId, "admin")).ShouldBe(0);
        (await Audit(targetId)).ShouldBe(["grant_moderator", "grant_admin", "revoke_admin"]);
    }

    [Fact]
    public async Task Admins_PostWithoutAntiforgeryTokenIsRejected()
    {
        var (admin, _) = await Staff("admin");
        var (_, handle, _) = await F.User("T3");

        var response = await admin.PostAsync("/admin/admins?handler=Add",
            new FormUrlEncodedContent([new("query", handle), new("role", "admin")]), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Admins_GuardsRejectSelfBootstrapAndUnverified()
    {
        var (admin, adminId) = await Staff("admin");
        var boot = await F.Factory.SignedIn(Ct, BootstrapEmail);
        var bootId = Guid.Parse((await (await boot.GetAsync("/api/v1/me", Ct)).Json(Ct))["id"]!.GetValue<string>());
        (await RoleCount(bootId, "admin")).ShouldBe(1);

        var self = await admin.PostForm("/admin/admins", "/admin/admins?handler=Set",
            [("id", adminId.ToString()), ("role", "admin"), ("granted", "false")], Ct);
        self.ShouldContain("You cannot change your own roles.");
        (await RoleCount(adminId, "admin")).ShouldBe(1);

        var bootstrap = await admin.PostForm("/admin/admins", "/admin/admins?handler=Set",
            [("id", bootId.ToString()), ("role", "admin"), ("granted", "false")], Ct);
        bootstrap.ShouldContain("The bootstrap admin cannot lose the admin role.");
        (await RoleCount(bootId, "admin")).ShouldBe(1);

        var unverified = AuthTestSupport.NewHandle();
        var signUp = await F.Factory.Browser().PostJson("/api/v1/account/sign-up",
            new { email = AuthTestSupport.NewEmail(), password = AuthTestSupport.Password, handle = unverified }, Ct);
        signUp.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var result = await admin.PostForm("/admin/admins", "/admin/admins?handler=Add", [("query", unverified), ("role", "moderator")], Ct);
        result.ShouldContain("Only users with a confirmed email can hold a role.");
    }

    [Fact]
    public async Task Admins_LastAdminCannotLoseTheRole()
    {
        var (_, onlyAdmin) = await Staff("admin");
        await using var scope = F.Factory.Services.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<AdminRoles>().SetAsync(Guid.NewGuid(), onlyAdmin, "admin", granted: false, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("user.last_admin");
        (await RoleCount(onlyAdmin, "admin")).ShouldBe(1);
    }

    [Fact]
    public async Task Users_ModeratorCanBanThroughTheForm()
    {
        var (mod, _) = await Staff("moderator");
        var (_, handle, targetId) = await F.User("T3");

        var page = await mod.PostForm($"/admin/users?q={handle}", $"/admin/users?handler=Ban&q={handle}",
            [("id", targetId.ToString()), ("permanent", "true"), ("reason", "spam"), ("q", handle)], Ct);

        page.ShouldContain("User banned.");
        (await Audit(targetId)).ShouldBe(["ban"]);
    }
}

public sealed class AdminAnonymousTests : IDisposable
{
    private readonly StudioFactory _factory = new();

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Access_AnonymousRedirectsToSignInWithReturnPath()
    {
        var anon = _factory.Browser();

        var root = await anon.GetAsync("/admin", Ct);
        root.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        root.Headers.Location!.OriginalString.ShouldBe("/account/sign-in?return=%2Fadmin");

        var deep = await anon.GetAsync("/admin/audit?actor=x", Ct);
        deep.Headers.Location!.OriginalString.ShouldBe("/account/sign-in?return=%2Fadmin%2Faudit%3Factor%3Dx");

        var css = await anon.GetAsync("/admin/admin.css", Ct);
        css.StatusCode.ShouldBe(HttpStatusCode.OK);
        css.Content.Headers.ContentType!.MediaType.ShouldBe("text/css");
    }
}

[Collection(PostgresCollection.Name)]
public sealed class AdminDistributionTests(PostgresFixture postgres) : StockTestBase(postgres, extra: b => b.UseSetting("DriftClient:Keys:0", Key))
{
    private const string Key = "integration-drift-key";
    private static readonly string ClientId = new('c', 64);

    private async Task<HttpClient> Admin()
    {
        var email = AuthTestSupport.NewEmail();
        var client = F.Factory.Browser();
        var userId = await F.Factory.SignUpAndVerify(client, email, AuthTestSupport.NewHandle(), Ct);
        await F.Sql($"insert into user_roles (user_id, role_id) select '{userId}', id from roles where name = 'admin'");
        (await client.PostJson("/api/v1/account/sign-in", new { email, password = AuthTestSupport.Password }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        return client;
    }

    private async Task<bool> Lists(string? distribution)
    {
        var client = Client();
        const string target = "/api/v1/marketplace";
        var request = new HttpRequestMessage(HttpMethod.Get, target);
        if (distribution is null)
        {
            request.Headers.Add("Sec-Fetch-Site", "same-origin");
        }
        else
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var nonce = Guid.NewGuid().ToString("N");
            request.Headers.Add("X-Cutwire-Client", ClientId);
            request.Headers.Add("X-Cutwire-Timestamp", timestamp);
            request.Headers.Add("X-Cutwire-Nonce", nonce);
            request.Headers.Add("X-Cutwire-App", "Drift/1.2.3 (linux)");
            request.Headers.Add("X-Cutwire-Distribution", distribution);
            request.Headers.Add("X-Cutwire-Signature", CutWire.Studio.Api.Http.DriftClientAuth.SignatureFor(Key, timestamp, nonce, ClientId, "GET", target, []));
        }

        var body = await Ok(await client.SendAsync(request, Ct));
        return body["sources"]!.AsObject().Where(kv => kv.Key.StartsWith("stock.")).SelectMany(kv => kv.Value!["providers"]!.AsArray()).Any(p => p!["id"]!.GetValue<string>() == "fake");
    }

    [Fact]
    public async Task HiddenFor_OmitsTheProviderOnlyForThatDistribution()
    {
        var admin = await Admin();
        var (html, _) = await admin.GetPage("/admin/distributions", Ct);
        var cells = Regex.Matches(html, "name=\"visible\" value=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
        cells.ShouldContain("fake|photo|android-play");

        await admin.PostForm("/admin/distributions", "/admin/distributions",
            cells.Where(c => c != "fake|photo|android-play").Select(c => ("visible", c)), Ct);

        (await F.Db(db => db.ProviderSettings.AsNoTracking().SingleAsync(s => s.Provider == "fake", Ct))).HiddenFor.ShouldBe(["android-play"]);
        (await Lists("android-play")).ShouldBeFalse();
        (await Lists("windows")).ShouldBeTrue();
        (await Lists(null)).ShouldBeTrue();
        (await F.Db(db => db.ModerationActions.AsNoTracking().Where(a => a.Action == "update_distributions").CountAsync(Ct))).ShouldBe(1);
    }

    [Fact]
    public async Task Stock_RejectsInvalidQuotaWindow()
    {
        var admin = await Admin();
        await admin.GetPage("/admin/stock", Ct);

        var page = await admin.PostForm("/admin/stock", "/admin/stock?handler=Save",
            [("provider", "fake"), ("type", "photo"), ("enabled", "true"), ("sort", "1"), ("quotaLimit", "5"), ("quotaWindow", "week"), ("quotaScope", "client")], Ct);
        page.ShouldContain("Quota window must be day or hour.");

        var saved = await admin.PostForm("/admin/stock", "/admin/stock?handler=Save",
            [("provider", "fake"), ("type", "photo"), ("enabled", "true"), ("label", "Renamed"), ("sort", "1"), ("quotaLimit", "5"), ("quotaWindow", "hour"), ("quotaScope", "account")], Ct);
        saved.ShouldContain("Saved fake (photo).");
        var row = await F.Db(db => db.ProviderSettings.AsNoTracking().SingleAsync(s => s.Provider == "fake", Ct));
        (row.Label, row.QuotaLimit, row.QuotaWindow, row.QuotaScope).ShouldBe(("Renamed", 5, "hour", "account"));
    }
}

[Collection(PostgresCollection.Name)]
public sealed class AdminMarketplaceTests(PostgresFixture postgres) : StockTestBase(postgres)
{
    private const string Page = """[{"id":"admin-made","title":"Admin made","sections":[{"id":"n","type":"notice","text":"hello"}]}]""";

    private async Task<(HttpClient Client, Guid Id)> Staff(string role)
    {
        var email = AuthTestSupport.NewEmail();
        var client = F.Factory.Browser();
        var userId = await F.Factory.SignUpAndVerify(client, email, AuthTestSupport.NewHandle(), Ct);
        await F.Sql($"insert into user_roles (user_id, role_id) select '{userId}', id from roles where name = '{role}'");
        (await client.PostJson("/api/v1/account/sign-in", new { email, password = AuthTestSupport.Password }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        return (client, Guid.Parse(userId));
    }

    private async Task<JsonNode> Manifest()
    {
        var web = Client();
        web.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
        return await Ok(await web.GetAsync("/api/v1/marketplace", Ct));
    }

    private static IEnumerable<(string, string)> LayoutForm(string name, string pages, params string[] targeted) =>
        [("Name", name), ("Priority", "5"), ("Enabled", "true"), ("PagesJson", pages), .. targeted.Select(t => ("Targeted", t))];

    private async Task<HttpResponseMessage> Post(HttpClient client, string page, string url, IEnumerable<(string, string)> fields)
    {
        var (_, token) = await client.GetPage(page, Ct);
        var form = fields.Select(f => new KeyValuePair<string, string>(f.Item1, f.Item2)).Append(new("__RequestVerificationToken", token));
        return await client.PostAsync(url, new FormUrlEncodedContent(form), Ct);
    }

    private Task<List<string>> Actions() => F.Db(db => db.ModerationActions.AsNoTracking()
        .Where(a => a.Action.StartsWith("marketplace.")).OrderBy(a => a.Id).Select(a => a.Action).ToListAsync(Ct));

    [Fact]
    public async Task Moderator_IsRefused()
    {
        var (mod, _) = await Staff("moderator");
        foreach (var url in new[] { "/admin/marketplace", "/admin/marketplacelayout", "/admin/marketplaceflag" })
            (await mod.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, url);
    }

    [Fact]
    public async Task Layout_CreateShowsInManifestAndAuditsThenDeletes()
    {
        var (admin, _) = await Staff("admin");
        var (form, _) = await admin.GetPage("/admin/marketplacelayout", Ct);
        WebUtility.HtmlDecode(form).ShouldContain("\"id\": \"home\"");

        var saved = await admin.PostForm("/admin/marketplacelayout", "/admin/marketplacelayout?handler=Save", LayoutForm("Spring", Page, "web"), Ct);
        saved.ShouldContain("Saved Spring.");
        saved.ShouldContain("/admin/marketplacelayout/");

        (await Manifest())["pages"]!.AsArray().Select(p => p!["id"]!.GetValue<string>()).ShouldBe(["admin-made"]);
        (await Actions()).ShouldBe(["marketplace.layout.save"]);

        var id = (await F.Db(db => db.MarketplaceLayouts.AsNoTracking().SingleAsync(Ct))).Id;
        var edit = await admin.PostForm($"/admin/marketplacelayout/{id}", $"/admin/marketplacelayout/{id}?handler=Save", LayoutForm("Spring", Page, "web"), Ct);
        edit.ShouldContain("Saved Spring.");
        (await F.Db(db => db.MarketplaceLayouts.CountAsync(Ct))).ShouldBe(1);

        var refused = await admin.PostForm($"/admin/marketplacelayout/{id}", $"/admin/marketplacelayout/{id}?handler=Delete", [], Ct);
        refused.ShouldContain("Tick the confirmation box");
        var deleted = await admin.PostForm($"/admin/marketplacelayout/{id}", $"/admin/marketplacelayout/{id}?handler=Delete", [("Confirm", "true")], Ct);
        deleted.ShouldContain("Layout deleted.");
        (await Manifest())["pages"]!.AsArray().Select(p => p!["id"]!.GetValue<string>()).ShouldNotContain("admin-made");
        (await Actions()).ShouldBe(["marketplace.layout.save", "marketplace.layout.save", "marketplace.layout.delete"]);
    }

    [Fact]
    public async Task Layout_InvalidJsonShowsErrorsAndDoesNotSave()
    {
        var (admin, _) = await Staff("admin");
        var broken = """[{"id":"x","title":"X","sections":[{"id":"n","type":"nope"}]}]""";

        var response = await Post(admin, "/admin/marketplacelayout", "/admin/marketplacelayout?handler=Save", LayoutForm("Bad", broken));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Ct));
        body.ShouldContain("pages[0].sections[0].type");
        body.ShouldContain("Unknown section type");
        body.ShouldContain("nope");
        (await F.Db(db => db.MarketplaceLayouts.CountAsync(Ct))).ShouldBe(0);
        (await Actions()).ShouldBeEmpty();

        var bad = await Post(admin, "/admin/marketplacelayout", "/admin/marketplacelayout?handler=Save", LayoutForm("Bad", "{", "appimage"));
        WebUtility.HtmlDecode(await bad.Content.ReadAsStringAsync(Ct)).ShouldContain("not valid JSON");
    }

    [Fact]
    public async Task Layout_RejectsBadVersionAndDuplicateName()
    {
        var (admin, _) = await Staff("admin");
        var form = LayoutForm("Dup", Page).Append(("MinAppVersion", "banana"));
        var response = await Post(admin, "/admin/marketplacelayout", "/admin/marketplacelayout?handler=Save", form);
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Ct)).ShouldContain("Minimum version is not a valid version.");

        await admin.PostForm("/admin/marketplacelayout", "/admin/marketplacelayout?handler=Save", LayoutForm("Dup", Page), Ct);
        var again = await Post(admin, "/admin/marketplacelayout", "/admin/marketplacelayout?handler=Save", LayoutForm("Dup", Page));
        WebUtility.HtmlDecode(await again.Content.ReadAsStringAsync(Ct)).ShouldContain("Another layout already has this name.");
    }

    [Fact]
    public async Task Flag_SaveTogglesFeatureAndValidates()
    {
        var (admin, _) = await Staff("admin");

        var badKey = await Post(admin, "/admin/marketplaceflag", "/admin/marketplaceflag?handler=Save", [("Key", "Bad Key"), ("Enabled", "true")]);
        WebUtility.HtmlDecode(await badKey.Content.ReadAsStringAsync(Ct)).ShouldContain("Key uses lowercase");
        var badJson = await Post(admin, "/admin/marketplaceflag", "/admin/marketplaceflag?handler=Save", [("Key", "ok.key"), ("ValueJson", "{nope")]);
        WebUtility.HtmlDecode(await badJson.Content.ReadAsStringAsync(Ct)).ShouldContain("Value is not valid JSON.");
        (await F.Db(db => db.FeatureFlags.CountAsync(Ct))).ShouldBe(0);

        (await admin.PostForm("/admin/marketplaceflag", "/admin/marketplaceflag?handler=Save", [("Key", "edit.tools"), ("Enabled", "true")], Ct))
            .ShouldContain("Saved edit.tools.");
        (await Manifest())["features"]!["edit.tools"]!.GetValue<bool>().ShouldBeTrue();

        var id = (await F.Db(db => db.FeatureFlags.AsNoTracking().SingleAsync(Ct))).Id;
        await admin.PostForm($"/admin/marketplaceflag/{id}", $"/admin/marketplaceflag/{id}?handler=Save", [("Key", "edit.tools")], Ct);
        (await Manifest())["features"]!["edit.tools"]!.GetValue<bool>().ShouldBeFalse();

        await admin.PostForm($"/admin/marketplaceflag/{id}", $"/admin/marketplaceflag/{id}?handler=Delete", [("Confirm", "true")], Ct);
        ((JsonObject)(await Manifest())["features"]!).ContainsKey("edit.tools").ShouldBeFalse();
        (await Actions()).ShouldBe(["marketplace.flag.save", "marketplace.flag.save", "marketplace.flag.delete"]);
    }

    [Fact]
    public async Task Preview_RendersResolvedManifestAndChosenLayout()
    {
        var (admin, _) = await Staff("admin");
        await admin.PostForm("/admin/marketplacelayout", "/admin/marketplacelayout?handler=Save", LayoutForm("Spring", Page, "web"), Ct);

        var (hit, _) = await admin.GetPage("/admin/marketplace?distribution=web&version=1.2.3", Ct);
        hit.ShouldContain("Layout: <strong>Spring</strong>");
        WebUtility.HtmlDecode(hit).ShouldContain("\"id\": \"admin-made\"");

        var (miss, _) = await admin.GetPage("/admin/marketplace?distribution=windows", Ct);
        miss.ShouldContain("Layout: <strong>built-in default</strong>");

        var (bad, _) = await admin.GetPage("/admin/marketplace?distribution=windows&version=x", Ct);
        bad.ShouldContain("The version is not a valid version.");
    }
}
