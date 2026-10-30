using CutWire.Studio.Api.Auth;
using CutWire.Studio.Application.Admin;
using CutWire.Studio.Application.Marketplace;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Domain.Stock;
using Microsoft.AspNetCore.Mvc;

namespace CutWire.Studio.Api.Pages.Admin;

public sealed class MarketplaceLayoutModel(MarketplaceAdmin admin) : AdminPageModel
{
    [BindProperty(SupportsGet = true)] public Guid? Id { get; set; }
    [BindProperty] public string? Name { get; set; }
    [BindProperty] public int Priority { get; set; }
    [BindProperty] public bool Enabled { get; set; }
    [BindProperty] public string[] Targeted { get; set; } = [];
    [BindProperty] public string? MinAppVersion { get; set; }
    [BindProperty] public string? MaxAppVersion { get; set; }
    [BindProperty] public string? PagesJson { get; set; }
    [BindProperty] public bool Confirm { get; set; }

    public IReadOnlyList<FieldViolation> Violations { get; private set; } = [];

    public static string[] Columns => Distributions.All;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Id is not { } id)
        {
            Enabled = true;
            PagesJson = MarketplaceManifest.DefaultLayoutJson;
            return Page();
        }

        var layout = await admin.LayoutAsync(id, ct);
        if (layout is null)
        {
            TempData[ErrorKey] = "Layout not found.";
            return RedirectToPage("Marketplace");
        }

        (Name, Priority, Enabled, Targeted, MinAppVersion, MaxAppVersion, PagesJson) =
            (layout.Name, layout.Priority, layout.Enabled, [.. layout.Distributions], layout.MinAppVersion, layout.MaxAppVersion, layout.PagesJson);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        var result = await admin.SaveLayoutAsync(User.GetUserId(), Id,
            new LayoutEdit(Name, Priority, Enabled, Targeted ?? [], MinAppVersion, MaxAppVersion, PagesJson), ct);
        if (result.IsSuccess)
        {
            TempData[OkKey] = $"Saved {Name!.Trim()}.";
            return RedirectToPage("Marketplace");
        }

        if (result.Error.Violations is not { Count: > 0 } violations) return Fail(result.Error.Message, new { id = Id });
        Violations = violations;
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken ct)
    {
        if (Id is not { } id || !Confirm) return Fail("Tick the confirmation box to delete.", new { id = Id });
        var result = await admin.DeleteLayoutAsync(User.GetUserId(), id, ct);
        if (result.IsFailure) return Fail(result.Error.Message, new { id });
        TempData[OkKey] = "Layout deleted.";
        return RedirectToPage("Marketplace");
    }
}
