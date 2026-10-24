using CutWire.Studio.Domain.Common;
using Microsoft.AspNetCore.Identity;

namespace CutWire.Studio.Infrastructure.Identity;

internal static class IdentityErrors
{
    public static Error ToError(this IdentityResult result, string code = "identity.invalid")
    {
        var errors = result.Errors
            .GroupBy(e => FieldFor(e.Code))
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
        return Error.Validation(code, "Request is invalid.", errors);
    }

    private static string FieldFor(string code) =>
        code.StartsWith("Password", StringComparison.Ordinal) ? "password"
        : code.Contains("Email", StringComparison.Ordinal) || code.Contains("UserName", StringComparison.Ordinal) ? "email"
        : "_";

    public static readonly Error InvalidCredentials =
        Error.Unauthorized("auth.invalid_credentials", "Email or password is incorrect.");

    public static readonly Error LockedOut =
        Error.Forbidden("auth.locked_out", "Too many failed attempts. Try again later.");

    public static readonly Error InvalidToken =
        Error.Validation("auth.invalid_token", "The link is invalid or has expired.");

    public static readonly Error UserNotFound = Error.NotFound("account.not_found", "Account not found.");
}
