using CutWire.Studio.Domain.Common;

namespace CutWire.Studio.Api.Http;

public static class ResultExtensions
{
    public static IResult ToHttpResult(this Result result, Func<IResult> onSuccess) =>
        result.IsSuccess ? onSuccess() : result.Error.ToHttpResult();

    /// <summary>A same-origin path. Browsers drop tabs and newlines from URLs, so "/&lt;tab&gt;/evil.com" would become "//evil.com".</summary>
    public static string SafeLocalPath(string? value, string fallback = "/") =>
        !string.IsNullOrEmpty(value) && value[0] == '/' && (value.Length == 1 || (value[1] != '/' && value[1] != '\\'))
        && !value.Any(c => char.IsControl(c) || c == '\\')
            ? value
            : fallback;
}
