namespace CutWire.Studio.Application.Quality;

public interface IContentGate
{
    Task<IReadOnlyList<GateViolation>> CheckAsync(SubmissionDraft draft, CancellationToken ct);
}
