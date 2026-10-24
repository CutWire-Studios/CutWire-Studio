using System.Net;
using System.Net.Http.Headers;
using CutWire.Studio.Domain.Accounts;
using CutWire.Studio.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace CutWire.Studio.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class PatAndPolicyTests(PostgresFixture postgres) : IDisposable
{
    private readonly StudioFactory _factory = new(postgres.ConnectionString,
        configure: b => b.ConfigureTestServices(s => s.AddSingleton<CutWire.Studio.Api.Endpoints.IApiContributor, TestEndpoints>()));

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _factory.Dispose();

    private HttpClient Bearer(string token)
    {
        var client = _factory.Browser(cookies: false);
        client.DefaultRequestHeaders.Remove("X-Requested-With");
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<(string Secret, string Id)> CreateToken(HttpClient session, string[] scopes)
    {
        var response = await session.PostJson("/api/v1/account/tokens", new { name = "ci", scopes }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var json = await response.Json(Ct);
        return (json["secret"]!.GetValue<string>(), json["token"]!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task Pat_CreateUseRevoke_WithScopeEnforcement()
    {
        var session = await _factory.SignedIn(Ct);
        var (secret, id) = await CreateToken(session, [AuthScopes.ForgeRead]);
        secret.ShouldStartWith("cwpat_");

        var list = await (await session.GetAsync("/api/v1/account/tokens", Ct)).Json(Ct);
        list.AsArray().Count.ShouldBe(1);
        list[0]!["secret"].ShouldBeNull();
        list[0]!["scopes"]![0]!.GetValue<string>().ShouldBe("forge:read");

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var stored = await scope.ServiceProvider.GetRequiredService<StudioDbContext>().PersonalAccessTokens.SingleAsync(t => t.Id == Guid.Parse(id), Ct);
            stored.TokenHash.ShouldBe(PatToken.Hash(secret));
            stored.TokenHash.ShouldNotContain(secret);
        }

        var bearer = Bearer(secret);
        (await bearer.GetAsync("/api/v1/_test/forge-read", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var denied = await bearer.GetAsync("/api/v1/_test/forge-write", Ct);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await denied.Json(Ct))["code"]!.GetValue<string>().ShouldBe("insufficient_scope");
        (await bearer.GetAsync("/api/v1/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await bearer.PostAsync("/api/v1/_test/forge-write", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await bearer.PostJson("/api/v1/account/change-password", new { newPassword = "whatever whatever" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await session.GetAsync("/api/v1/_test/forge-write", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await session.DeleteAsync($"/api/v1/account/tokens/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await bearer.GetAsync("/api/v1/_test/forge-read", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Bearer("cwpat_unknown").GetAsync("/api/v1/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await _factory.Browser(cookies: false).GetAsync("/api/v1/_test/forge-read", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Pat_Validation()
    {
        var session = await _factory.SignedIn(Ct);

        (await session.PostJson("/api/v1/account/tokens", new { name = "x", scopes = new[] { "drift" } }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await session.PostJson("/api/v1/account/tokens", new { name = "", scopes = new[] { "forge:read" } }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await session.PostJson("/api/v1/account/tokens",
            new { name = "long", scopes = new[] { "forge:read" }, expiresAt = DateTimeOffset.UtcNow.AddDays(400) }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await session.PostJson("/api/v1/account/tokens",
            new { name = "ok", scopes = new[] { "forge:read" }, expiresAt = DateTimeOffset.UtcNow.AddDays(30) }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Pat_Expired_IsRejected()
    {
        var session = await _factory.SignedIn(Ct);
        var (secret, id) = await CreateToken(session, [AuthScopes.ForgeRead]);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StudioDbContext>();
            await db.Database.ExecuteSqlAsync($"update personal_access_tokens set expires_at = now() - interval '1 minute' where id = {Guid.Parse(id)}", Ct);
        }

        (await Bearer(secret).GetAsync("/api/v1/_test/forge-read", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task NotBanned_And_RolePolicies()
    {
        var session = await _factory.SignedIn(Ct);
        var me = await (await session.GetAsync("/api/v1/me", Ct)).Json(Ct);
        var userId = Guid.Parse(me["id"]!.GetValue<string>());

        (await session.GetAsync("/api/v1/_test/not-banned", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await session.GetAsync("/api/v1/_test/moderator", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StudioDbContext>();
            (await db.UserProfiles.SingleAsync(p => p.Id == userId, Ct)).SetBan(DateTimeOffset.UtcNow.AddDays(1), permanent: false);
            await db.SaveChangesAsync(Ct);
        }

        var banned = await session.GetAsync("/api/v1/_test/not-banned", Ct);
        banned.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await banned.Json(Ct))["code"]!.GetValue<string>().ShouldBe("banned");
        (await (await session.GetAsync("/api/v1/me", Ct)).Json(Ct))["banned"]!.GetValue<bool>().ShouldBeTrue();
    }
}
