using CutWire.Studio.Api.Auth;
using CutWire.Studio.Application.Moderation;
using Microsoft.AspNetCore.Mvc;

namespace CutWire.Studio.Api.Pages.Admin;

public sealed class QueueModel(ModerationQueries queries, ModerationActions actions) : AdminPageModel
{
    public static readonly string[] Types = ["reported", "hidden", "flagged"];

    [BindProperty(SupportsGet = true)]
    public string Type { get; set; } = "reported";

    [BindProperty(SupportsGet = true)]
    public string? Cursor { get; set; }

    public ModQueuePageDto Data { get; private set; } = new([], null);

    public async Task OnGetAsync(CancellationToken ct)
    {
        var result = await queries.QueueAsync(Type, Cursor, null, ct);
        if (result.IsSuccess) Data = result.Value;
        else TempData[ErrorKey] = result.Error.Message;
    }

    private object Route => new { type = Type, cursor = Cursor };

    public async Task<IActionResult> OnPostRestoreAsync(Guid id, bool dismissReports, string? reason, CancellationToken ct) =>
        Back(await actions.RestoreAsync(User.GetUserId(), id, dismissReports, reason, ct), "Item restored.", Route);

    public async Task<IActionResult> OnPostRemoveAsync(Guid id, string? reason, CancellationToken ct) =>
        Back(await actions.RemoveAsync(User.GetUserId(), id, reason, ct), "Item removed.", Route);

    public async Task<IActionResult> OnPostPurgeAsync(Guid id, int? days, string? reason, CancellationToken ct) =>
        Back(await actions.SchedulePurgeAsync(User.GetUserId(), id, days, reason, ct), "Purge scheduled.", Route);

    public async Task<IActionResult> OnPostDismissReportAsync(Guid id, string? note, CancellationToken ct) =>
        Back(await actions.ResolveReportAsync(User.GetUserId(), id, uphold: false, note, ct), "Report dismissed.", Route);

    public async Task<IActionResult> OnPostUpholdReportAsync(Guid id, string? note, CancellationToken ct) =>
        Back(await actions.ResolveReportAsync(User.GetUserId(), id, uphold: true, note, ct), "Report upheld.", Route);

    public async Task<IActionResult> OnPostResolveFlagAsync(Guid id, string? note, CancellationToken ct) =>
        Back(await actions.ResolveFlagAsync(User.GetUserId(), id, note, ct), "Flag resolved.", Route);

    public async Task<IActionResult> OnPostBulkAsync(Guid[]? ids, string? action, string? reason, int? days, bool dismissReports, CancellationToken ct)
    {
        var result = await actions.BulkAsync(User.GetUserId(), ids, action, reason, days, dismissReports, ct);
        if (result.IsFailure) return Back(result, "", Route);
        var bulk = result.Value;
        if (bulk.Failed.Count == 0) TempData[OkKey] = $"{bulk.Succeeded.Count} item(s) updated.";
        else TempData[ErrorKey] = $"{bulk.Succeeded.Count} updated, {bulk.Failed.Count} failed: " +
            string.Join("; ", bulk.Failed.Take(5).Select(f => $"{f.Id} ({f.Message})"));
        return RedirectToPage(Route);
    }
}
