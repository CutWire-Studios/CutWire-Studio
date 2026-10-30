using CutWire.Studio.Api.Auth;
using CutWire.Studio.Application.Moderation;
using Microsoft.AspNetCore.Mvc;

namespace CutWire.Studio.Api.Pages.Admin;

public sealed class UsersModel(ModerationQueries queries, ModerationActions actions) : AdminPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Cursor { get; set; }

    public ModUserPageDto Data { get; private set; } = new([], null);

    public async Task OnGetAsync(CancellationToken ct)
    {
        var result = await queries.UsersAsync(Q, Cursor, null, ct);
        if (result.IsSuccess) Data = result.Value;
        else TempData[ErrorKey] = result.Error.Message;
    }

    private object Route => new { q = Q, cursor = Cursor };

    public async Task<IActionResult> OnPostBanAsync(Guid id, DateOnly? until, bool permanent, string? reason, CancellationToken ct)
    {
        DateTimeOffset? end = until is { } day ? new DateTimeOffset(day.ToDateTime(new TimeOnly(23, 59, 59)), TimeSpan.Zero) : null;
        return Back(await actions.BanAsync(User.GetUserId(), User.IsInRole("admin"), id, end, permanent, reason, ct), "User banned.", Route);
    }

    public async Task<IActionResult> OnPostUnbanAsync(Guid id, string? reason, CancellationToken ct) =>
        Back(await actions.UnbanAsync(User.GetUserId(), id, reason, ct), "User unbanned.", Route);

    public async Task<IActionResult> OnPostTrustAsync(Guid id, string? level, string? reason, CancellationToken ct) =>
        Back(await actions.SetTrustAsync(User.GetUserId(), id, level, reason, ct), "Trust level updated.", Route);
}
