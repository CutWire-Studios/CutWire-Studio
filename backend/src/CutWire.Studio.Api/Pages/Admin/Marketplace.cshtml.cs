using CutWire.Studio.Application.Admin;
using CutWire.Studio.Domain.Stock;
using Microsoft.AspNetCore.Mvc;

namespace CutWire.Studio.Api.Pages.Admin;

public sealed class MarketplaceModel(MarketplaceAdmin admin) : AdminPageModel
{
    public IReadOnlyList<LayoutRowDto> Layouts { get; private set; } = [];
    public IReadOnlyList<FlagRowDto> Flags { get; private set; } = [];
    public ManifestPreview? Preview { get; private set; }
    public string PreviewDistribution { get; private set; } = "";
    public string? PreviewVersion { get; private set; }
    public string? PreviewError { get; private set; }

    public static string[] Columns => Distributions.All;

    public async Task OnGetAsync(string? distribution, string? version, CancellationToken ct)
    {
        Layouts = await admin.LayoutsAsync(ct);
        Flags = await admin.FlagsAsync(ct);
        if (distribution is null) return;

        PreviewDistribution = distribution;
        PreviewVersion = version;
        var result = await admin.PreviewAsync(distribution, version, ct);
        if (result.IsSuccess) Preview = result.Value;
        else PreviewError = result.Error.Message;
    }
}
