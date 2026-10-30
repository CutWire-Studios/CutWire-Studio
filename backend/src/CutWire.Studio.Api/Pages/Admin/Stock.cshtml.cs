using CutWire.Studio.Api.Auth;
using CutWire.Studio.Application.Admin;
using Microsoft.AspNetCore.Mvc;

namespace CutWire.Studio.Api.Pages.Admin;

public sealed class StockModel(StockAdmin admin) : AdminPageModel
{
    public IReadOnlyList<ProviderRowDto> Rows { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct) => Rows = await admin.ProvidersAsync(ct);

    public async Task<IActionResult> OnPostSaveAsync(string provider, string type, bool enabled, string? label, int sort, int quotaLimit,
        string? quotaWindow, string? quotaScope, CancellationToken ct)
    {
        if (!ModelState.IsValid) return Fail("The form contains an invalid number.");
        var edit = new ProviderEdit(enabled, label, sort, quotaLimit, quotaWindow, quotaScope);
        return Back(await admin.SaveProviderAsync(User.GetUserId(), provider, type, edit, ct), $"Saved {provider} ({type}).");
    }
}
