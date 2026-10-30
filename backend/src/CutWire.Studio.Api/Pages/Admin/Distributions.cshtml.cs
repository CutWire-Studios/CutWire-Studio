using CutWire.Studio.Api.Auth;
using CutWire.Studio.Application.Admin;
using CutWire.Studio.Domain.Stock;
using Microsoft.AspNetCore.Mvc;

namespace CutWire.Studio.Api.Pages.Admin;

public sealed class DistributionsModel(StockAdmin admin) : AdminPageModel
{
    public const char Separator = '|';

    public IReadOnlyList<ProviderRowDto> Rows { get; private set; } = [];

    public static string[] Columns => Distributions.All;

    public async Task OnGetAsync(CancellationToken ct) => Rows = await admin.ProvidersAsync(ct);

    public async Task<IActionResult> OnPostAsync(string[]? visible, CancellationToken ct)
    {
        var cells = new HashSet<(string, string, string)>();
        foreach (var value in visible ?? [])
        {
            var parts = value.Split(Separator);
            if (parts.Length != 3) return Fail("The form contains an invalid cell.");
            cells.Add((parts[0], parts[1], parts[2]));
        }

        return Back(await admin.SaveDistributionsAsync(User.GetUserId(), cells, ct), "Distribution visibility saved.");
    }
}
