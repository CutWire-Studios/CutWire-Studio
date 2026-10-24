using System.Collections.Immutable;
using Microsoft.AspNetCore.WebUtilities;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace CutWire.Studio.Infrastructure.Oidc;

public sealed record ScopeInfo(string Name, string DisplayName, string Description);

public sealed record AuthorizeRequestInfo(string ClientId, string[] Scopes, string? RedirectUri, string? State);

public sealed record ConsentInfo(string ClientId, string ClientName, ScopeInfo[] Scopes, bool AlreadyGranted);

public sealed class ConsentService(IOpenIddictApplicationManager applications, IOpenIddictAuthorizationManager authorizations)
{
    private static readonly Dictionary<string, ScopeInfo> Catalog = new[]
    {
        new ScopeInfo(Scopes.OpenId, "Sign you in", "Verify who you are."),
        new ScopeInfo(Scopes.Profile, "Your profile", "See your handle, display name and avatar."),
        new ScopeInfo(Scopes.Email, "Your email", "See your email address."),
        new ScopeInfo(Scopes.OfflineAccess, "Stay signed in", "Keep access until you revoke it."),
        new ScopeInfo("forge:read", "Read Forge documents", "Read your effects, transitions and sound effects."),
        new ScopeInfo("forge:write", "Edit Forge documents", "Create and change your effects, transitions and sound effects."),
        new ScopeInfo("community:publish", "Publish to the community", "Publish and update items on your behalf."),
        new ScopeInfo("drift", "Drift app access", "Use CutWire Studio services from the Drift app."),
    }.ToDictionary(s => s.Name);

    public async Task<object?> FindClientAsync(string? clientId, CancellationToken ct) =>
        string.IsNullOrEmpty(clientId) ? null : await applications.FindByClientIdAsync(clientId, ct);

    public async Task<object?> FindGrantAsync(Guid userId, object application, IEnumerable<string> scopes, CancellationToken ct)
    {
        var applicationId = (await applications.GetIdAsync(application, ct))!;
        await foreach (var authorization in authorizations.FindAsync(userId.ToString(), applicationId, Statuses.Valid,
                           AuthorizationTypes.Permanent, scopes.ToImmutableArray(), ct))
            return authorization;
        return null;
    }

    public async Task GrantAsync(Guid userId, object application, IEnumerable<string> scopes, CancellationToken ct)
    {
        var applicationId = (await applications.GetIdAsync(application, ct))!;
        await authorizations.CreateAsync(new OpenIddictAuthorizationDescriptor
        {
            ApplicationId = applicationId,
            Subject = userId.ToString(),
            Status = Statuses.Valid,
            Type = AuthorizationTypes.Permanent,
        }.WithScopes(scopes), ct);
    }

    public static AuthorizeRequestInfo? ParseReturn(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl) || !returnUrl.StartsWith("/connect/authorize?", StringComparison.Ordinal)) return null;
        var query = QueryHelpers.ParseQuery(returnUrl[(returnUrl.IndexOf('?') + 1)..]);
        var clientId = query.TryGetValue("client_id", out var c) ? c.ToString() : null;
        if (string.IsNullOrEmpty(clientId)) return null;
        var scopes = (query.TryGetValue("scope", out var sc) ? sc.ToString() : "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new AuthorizeRequestInfo(clientId, scopes,
            query.TryGetValue("redirect_uri", out var r) ? r.ToString() : null,
            query.TryGetValue("state", out var st) ? st.ToString() : null);
    }

    public async Task<string?> BuildDeniedRedirectAsync(object application, AuthorizeRequestInfo request, string issuer, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.RedirectUri) ||
            !await applications.ValidateRedirectUriAsync(application, request.RedirectUri, ct))
            return null;

        var parameters = new Dictionary<string, string?> { ["error"] = "access_denied", ["iss"] = issuer };
        if (!string.IsNullOrEmpty(request.State)) parameters["state"] = request.State;
        return QueryHelpers.AddQueryString(request.RedirectUri, parameters);
    }

    public async Task<ConsentInfo> DescribeAsync(Guid userId, object application, IEnumerable<string> scopes, CancellationToken ct)
    {
        var list = scopes.ToArray();
        return new ConsentInfo(
            (await applications.GetClientIdAsync(application, ct))!,
            await applications.GetDisplayNameAsync(application, ct) ?? "Application",
            list.Select(s => Catalog.TryGetValue(s, out var info) ? info : new ScopeInfo(s, s, "")).ToArray(),
            await FindGrantAsync(userId, application, list, ct) is not null);
    }
}

internal static class AuthorizationDescriptorExtensions
{
    public static OpenIddictAuthorizationDescriptor WithScopes(this OpenIddictAuthorizationDescriptor descriptor, IEnumerable<string> scopes)
    {
        foreach (var scope in scopes) descriptor.Scopes.Add(scope);
        return descriptor;
    }
}
