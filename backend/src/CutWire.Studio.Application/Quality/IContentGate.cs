namespace CutWire.Studio.Application.Quality;

/// <summary>Marks gates that only look at text and taxonomy, so they can run before any package or media exists.</summary>
public interface IMetadataGate : IContentGate;

public interface IContentGate
{
    Task<IReadOnlyList<GateViolation>> CheckAsync(SubmissionDraft draft, CancellationToken ct);
}
