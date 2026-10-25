namespace CutWire.Studio.Domain.Common;

public enum ErrorKind
{
    Validation,
    Unprocessable,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden,
    TooLarge,
    RateLimited,
    Unavailable,
    Unexpected,
}

/// <summary>One failed rule: a stable machine-readable <paramref name="Code"/> for a <paramref name="Field"/>.</summary>
public sealed record FieldViolation(
    string Field,
    string Code,
    string Message,
    IReadOnlyDictionary<string, object>? Params = null,
    string Severity = "block");

public sealed record Error(
    string Code,
    string Message,
    ErrorKind Kind,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    IReadOnlyList<FieldViolation>? Violations = null,
    IReadOnlyDictionary<string, object?>? Extensions = null)
{
    /// <summary>Adds top-level problem members (e.g. the current server copy of a conflicting document).</summary>
    public Error With(string key, object? value) =>
        this with { Extensions = new Dictionary<string, object?>(Extensions ?? new Dictionary<string, object?>()) { [key] = value } };

    public static Error Validation(string code, string message, IReadOnlyDictionary<string, string[]>? errors = null) =>
        new(code, message, ErrorKind.Validation, errors);

    /// <summary>Validation error carrying stable per-field codes; <c>Errors</c> is derived from the violations.</summary>
    public static Error Validation(string code, string message, IReadOnlyList<FieldViolation> violations) =>
        new(code, message, ErrorKind.Validation, GroupMessages(violations), violations);

    /// <summary>Well-formed input that breaks content rules (HTTP 422).</summary>
    public static Error Unprocessable(string code, string message, IReadOnlyList<FieldViolation> violations) =>
        new(code, message, ErrorKind.Unprocessable, GroupMessages(violations), violations);

    public static Error Unprocessable(string code, string message) => new(code, message, ErrorKind.Unprocessable);

    private static Dictionary<string, string[]> GroupMessages(IReadOnlyList<FieldViolation> violations) =>
        violations.GroupBy(v => v.Field).ToDictionary(g => g.Key, g => g.Select(v => v.Message).ToArray());

    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);
    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorKind.Unauthorized);
    public static Error Forbidden(string code, string message) => new(code, message, ErrorKind.Forbidden);
    public static Error TooLarge(string code, string message) => new(code, message, ErrorKind.TooLarge);
    public static Error RateLimited(string code, string message, int retryAfterSeconds) =>
        new Error(code, message, ErrorKind.RateLimited).With("retryAfter", Math.Max(1, retryAfterSeconds));

    public static Error Unavailable(string code, string message) => new(code, message, ErrorKind.Unavailable);
}
