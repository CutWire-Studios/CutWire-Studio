namespace CutWire.Studio.Application.Quality;

public enum GateSeverity
{
    Block,
    Flag,
}

public sealed record GateViolation(
    string Field,
    string Code,
    string Message,
    IReadOnlyDictionary<string, object>? Params = null,
    GateSeverity Severity = GateSeverity.Block);
