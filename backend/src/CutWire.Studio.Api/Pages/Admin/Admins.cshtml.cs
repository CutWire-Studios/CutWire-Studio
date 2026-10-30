using CutWire.Studio.Api.Auth;
using CutWire.Studio.Application.Admin;
using CutWire.Studio.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace CutWire.Studio.Api.Pages.Admin;

public sealed class AdminsModel(AdminRoles roles) : AdminPageModel
{
    public IReadOnlyList<StaffMemberDto> Staff { get; private set; } = [];

    public Guid Me => User.GetUserId();

    public async Task OnGetAsync(CancellationToken ct) => Staff = await roles.ListAsync(ct);

    public async Task<IActionResult> OnPostAddAsync(string? query, string? role, CancellationToken ct)
    {
        var found = await roles.FindAsync(query, ct);
        if (found.IsFailure) return Back(found, "");
        return Back(await roles.SetAsync(User.GetUserId(), found.Value, role, granted: true, ct), $"Granted {role}.");
    }

    public async Task<IActionResult> OnPostSetAsync(Guid id, string? role, bool granted, CancellationToken ct) =>
        Back(await roles.SetAsync(User.GetUserId(), id, role, granted, ct), granted ? $"Granted {role}." : $"Revoked {role}.");
}
