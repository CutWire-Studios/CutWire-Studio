using CutWire.Studio.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace CutWire.Studio.Api.Http;

public static class ProblemMapping
{
    public const string CodeKey = "code";
    public const string ErrorsKey = "errors";
    public const string ViolationsKey = "violations";

    public static int StatusFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.Unprocessable => StatusCodes.Status422UnprocessableEntity,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.TooLarge => StatusCodes.Status413PayloadTooLarge,
        ErrorKind.RateLimited => StatusCodes.Status429TooManyRequests,
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
        422 => "unprocessable",
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
        if (error.Violations is { Count: > 0 }) problem.Extensions[ViolationsKey] = error.Violations;
        if (error.Extensions is not null)
            foreach (var (key, value) in error.Extensions) problem.Extensions[key] = value;
        return problem;
    }

    public static IResult ToHttpResult(this Error error)
    {
        var problem = error.ToProblem();
        if (error.Kind == ErrorKind.RateLimited && error.Extensions?.GetValueOrDefault("retryAfter") is int seconds)
            return new RetryAfterResult(seconds, Results.Problem(problem));
        return Results.Problem(problem);
    }

    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value) : result.Error.ToHttpResult();
}

internal sealed class RetryAfterResult(int seconds, IResult inner) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.Headers.RetryAfter = seconds.ToString();
        return inner.ExecuteAsync(httpContext);
    }
}
