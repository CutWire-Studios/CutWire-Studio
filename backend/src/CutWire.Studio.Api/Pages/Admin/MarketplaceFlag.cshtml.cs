using CutWire.Studio.Api.Auth;
using CutWire.Studio.Application.Admin;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Domain.Stock;
using Microsoft.AspNetCore.Mvc;

namespace CutWire.Studio.Api.Pages.Admin;

public sealed class MarketplaceFlagModel(MarketplaceAdmin admin) : AdminPageModel
{
    [BindProperty(SupportsGet = true)] public Guid? Id { get; set; }
    [BindProperty] public string? Key { get; set; }
    [BindProperty] public bool Enabled { get; set; }
    [BindProperty] public string? ValueJson { get; set; }
    [BindProperty] public string[] Targeted { get; set; } = [];
    [BindProperty] public string? MinAppVersion { get; set; }
    [BindProperty] public string? MaxAppVersion { get; set; }
    [BindProperty] public string? Note { get; set; }
    [BindProperty] public bool Confirm { get; set; }

    public IReadOnlyList<FieldViolation> Violations { get; private set; } = [];

    public static string[] Columns => Distributions.All;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Id is not { } id)
        {
            Enabled = true;
            return Page();
        }


        var flag = await admin.FlagAsync(id, ct);
        if (flag is null)
        {
            TempData[ErrorKey] = "Flag not found.";
            return RedirectToPage("Marketplace");
        }

        (Key, Enabled, ValueJson, Targeted, MinAppVersion, MaxAppVersion, Note) =
            (flag.Key, flag.Enabled, flag.ValueJson, [.. flag.Distributions], flag.MinAppVersion, flag.MaxAppVersion, flag.Note);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        var result = await admin.SaveFlagAsync(User.GetUserId(), Id,
            new FlagEdit(Key, Enabled, ValueJson, Targeted ?? [], MinAppVersion, MaxAppVersion, Note), ct);
        if (result.IsSuccess)
        {
            TempData[OkKey] = $"Saved {Key!.Trim()}.";
            return RedirectToPage("Marketplace");
        }

        if (result.Error.Violations is not { Count: > 0 } violations) return Fail(result.Error.Message, new { id = Id });
        Violations = violations;
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken ct)
    {
        if (Id is not { } id || !Confirm) return Fail("Tick the confirmation box to delete.", new { id = Id });
        var result = await admin.DeleteFlagAsync(User.GetUserId(), id, ct);
        if (result.IsFailure) return Fail(result.Error.Message, new { id });
        TempData[OkKey] = "Flag deleted.";
        return RedirectToPage("Marketplace");
    }
}
