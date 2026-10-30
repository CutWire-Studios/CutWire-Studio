using CutWire.Studio.Application.Moderation;
using Microsoft.AspNetCore.Mvc;

namespace CutWire.Studio.Api.Pages.Admin;

public sealed class AuditModel(ModerationQueries queries) : AdminPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Actor { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Target { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Cursor { get; set; }

    public AuditPageDto Data { get; private set; } = new([], null);

    public async Task OnGetAsync(CancellationToken ct)
    {
        Guid? target = null;
        if (!string.IsNullOrWhiteSpace(Target))
        {
            if (!Guid.TryParse(Target.Trim(), out var parsed))
            {
                TempData[ErrorKey] = "The target must be a GUID.";
                return;
            }

            target = parsed;
        }

        var result = await queries.AuditAsync(Actor, target, Cursor, null, ct);
        if (result.IsSuccess) Data = result.Value;
        else TempData[ErrorKey] = result.Error.Message;
    }
}
