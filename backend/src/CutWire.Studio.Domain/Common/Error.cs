namespace CutWire.Studio.Domain.Common;

public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden,
    TooLarge,
    Unavailable,
    Unexpected,
}

public sealed record Error(
    string Code,
    string Message,
    ErrorKind Kind,
    IReadOnlyDictionary<string, string[]>? Errors = null)
{
    public static Error Validation(string code, string message, IReadOnlyDictionary<string, string[]>? errors = null) =>
        new(code, message, ErrorKind.Validation, errors);

    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);
    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorKind.Unauthorized);
    public static Error Forbidden(string code, string message) => new(code, message, ErrorKind.Forbidden);
    public static Error TooLarge(string code, string message) => new(code, message, ErrorKind.TooLarge);
    public static Error Unavailable(string code, string message) => new(code, message, ErrorKind.Unavailable);
}
