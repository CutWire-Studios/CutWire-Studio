using CutWire.Studio.Api.Auth;
using CutWire.Studio.Application.Admin;
using Microsoft.AspNetCore.Mvc;

namespace CutWire.Studio.Api.Pages.Admin;

public sealed class CategoriesModel(StockAdmin admin) : AdminPageModel
{
    public IReadOnlyList<CategoryRowDto> Assets { get; private set; } = [];
    public IReadOnlyList<CategoryRowDto> Sfx { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Assets = await admin.CategoriesAsync(CategoryKinds.Asset, ct);
        Sfx = await admin.CategoriesAsync(CategoryKinds.Sfx, ct);
    }

    public async Task<IActionResult> OnPostSaveAsync(string kind, string category, bool enabled, string? label, int sort, CancellationToken ct)
    {
        if (!ModelState.IsValid) return Fail("The form contains an invalid number.");
        return Back(await admin.SaveCategoryAsync(User.GetUserId(), kind, category, new CategoryEdit(enabled, label, sort), ct),
            $"Saved {category}.");
    }
}
