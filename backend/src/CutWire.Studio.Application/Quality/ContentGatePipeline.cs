using CutWire.Studio.Domain.Common;

namespace CutWire.Studio.Application.Quality;

public sealed record GateReport(IReadOnlyList<GateViolation> Flags);

public sealed class ContentGatePipeline(IEnumerable<IContentGate> gates)
{
    private readonly IContentGate[] _gates = gates.ToArray();

    public async Task<IReadOnlyList<GateViolation>> CheckAsync(SubmissionDraft draft, CancellationToken ct)
    {
        var all = new List<GateViolation>();
        foreach (var gate in _gates)
            all.AddRange(await gate.CheckAsync(draft, ct));
        return all;
    }

    /// <summary>Runs only the text and taxonomy gates (no media, package, duplicate or quota checks).</summary>
    public async Task<IReadOnlyList<GateViolation>> CheckMetadataAsync(SubmissionDraft draft, CancellationToken ct)
    {
        var all = new List<GateViolation>();
        foreach (var gate in _gates.OfType<IMetadataGate>())
            all.AddRange(await gate.CheckAsync(draft, ct));
        return all;
    }

    /// <summary>Runs the media rules for one uploaded image against its kind's spec for that role.</summary>
    public IReadOnlyList<GateViolation> CheckMedia(SubmissionDraft draft, MediaUpload upload) =>
        _gates.OfType<MediaGate>().Single().CheckUpload(draft.Kind, upload);

    /// <summary>Fails with field errors when any violation blocks; otherwise succeeds carrying the non-blocking flags.</summary>
    public async Task<Result<GateReport>> ValidateAsync(SubmissionDraft draft, CancellationToken ct)
    {
        var violations = await CheckAsync(draft, ct);
        var blocking = violations.Where(v => v.Severity == GateSeverity.Block).ToList();
        if (blocking.Count == 0)
        {
            return new GateReport(violations.Where(v => v.Severity == GateSeverity.Flag).ToList());
        }

        return ToError(blocking);
    }

    public static Error ToError(IEnumerable<GateViolation> blocking) =>
        Error.Unprocessable("content_rejected", "The submission does not meet the quality rules.",
            blocking.Select(v => new FieldViolation(v.Field, v.Code, v.Message, v.Params)).ToList());
}
