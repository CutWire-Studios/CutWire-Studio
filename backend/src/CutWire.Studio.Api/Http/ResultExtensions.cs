using CutWire.Studio.Domain.Common;

namespace CutWire.Studio.Api.Http;

public static class ResultExtensions
{
    public static IResult ToHttpResult(this Result result, Func<IResult> onSuccess) =>
        result.IsSuccess ? onSuccess() : result.Error.ToHttpResult();

    public static string SafeLocalPath(string? value, string fallback = "/") =>
        !string.IsNullOrEmpty(value) && value[0] == '/' && (value.Length == 1 || (value[1] != '/' && value[1] != '\\'))
            ? value
            : fallback;
}
