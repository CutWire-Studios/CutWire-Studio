using CutWire.Studio.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace CutWire.Studio.Api.Http;

public static class ProblemMapping
{
    public const string CodeKey = "code";
    public const string ErrorsKey = "errors";

    public static int StatusFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.TooLarge => StatusCodes.Status413PayloadTooLarge,
        ErrorKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };

    public static string DefaultCodeFor(int status) => status switch
    {
        400 => "bad_request",
        401 => "unauthorized",
        403 => "forbidden",
        404 => "not_found",
        405 => "method_not_allowed",
        409 => "conflict",
        413 => "payload_too_large",
        415 => "unsupported_media_type",
        429 => "rate_limited",
        500 => "internal_error",
        503 => "unavailable",
        _ => $"http_{status}",
    };

    public static ProblemDetails ToProblem(this Error error)
    {
        var problem = new ProblemDetails
        {
            Status = StatusFor(error.Kind),
            Detail = error.Message,
        };
        problem.Extensions[CodeKey] = error.Code;
        if (error.Errors is { Count: > 0 }) problem.Extensions[ErrorsKey] = error.Errors;
        return problem;
    }

    public static IResult ToHttpResult(this Error error)
    {
        var problem = error.ToProblem();
        return Results.Problem(problem);
    }

    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value) : result.Error.ToHttpResult();
}
