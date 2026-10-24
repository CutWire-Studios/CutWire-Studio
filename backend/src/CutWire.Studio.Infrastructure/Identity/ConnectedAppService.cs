using CutWire.Studio.Domain.Common;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace CutWire.Studio.Infrastructure.Identity;

public sealed record ConnectedAppDto(string Id, string ClientId, string ClientName, string[] Scopes, DateTimeOffset? CreatedAt);

public sealed class ConnectedAppService(
    IOpenIddictAuthorizationManager authorizations,
    IOpenIddictApplicationManager applications,
    IOpenIddictTokenManager tokens)
{
    public async Task<IReadOnlyList<ConnectedAppDto>> ListAsync(Guid userId, CancellationToken ct)
    {
        var result = new List<ConnectedAppDto>();
        var names = new Dictionary<string, (string ClientId, string Name)>();

        await foreach (var authorization in authorizations.FindAsync(
                           subject: userId.ToString(), client: null, status: Statuses.Valid,
                           type: AuthorizationTypes.Permanent, scopes: null, ct))
        {
            var applicationId = await authorizations.GetApplicationIdAsync(authorization, ct);
            if (applicationId is null) continue;
            if (!names.TryGetValue(applicationId, out var app))
            {
                var application = await applications.FindByIdAsync(applicationId, ct);
                if (application is null) continue;
                var clientId = await applications.GetClientIdAsync(application, ct) ?? applicationId;
                app = (clientId, await applications.GetDisplayNameAsync(application, ct) ?? clientId);
                names[applicationId] = app;
            }

            result.Add(new ConnectedAppDto(
                (await authorizations.GetIdAsync(authorization, ct))!,
                app.ClientId,
                app.Name,
                [.. await authorizations.GetScopesAsync(authorization, ct)],
                await authorizations.GetCreationDateAsync(authorization, ct)));
        }

        return result.OrderBy(a => a.ClientName).ToList();
    }

    public async Task<Result> RevokeAsync(Guid userId, string authorizationId, CancellationToken ct)
    {
        var authorization = await authorizations.FindByIdAsync(authorizationId, ct);
        if (authorization is null || await authorizations.GetSubjectAsync(authorization, ct) != userId.ToString())
            return Error.NotFound("connected_app.not_found", "Connected app not found.");

        await tokens.RevokeByAuthorizationIdAsync(authorizationId, ct);
        await authorizations.TryRevokeAsync(authorization, ct);
        return Result.Success();
    }
}
