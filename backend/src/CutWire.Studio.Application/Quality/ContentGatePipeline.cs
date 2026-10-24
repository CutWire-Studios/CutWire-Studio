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
        Error.Validation("content_rejected", "The submission does not meet the quality rules.",
            blocking.GroupBy(v => v.Field).ToDictionary(g => g.Key, g => g.Select(v => v.Message).ToArray()));
}
