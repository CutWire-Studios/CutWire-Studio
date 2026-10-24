using CutWire.Studio.Api.Http;
using CutWire.Studio.Domain.Common;
using Shouldly;

namespace CutWire.Studio.UnitTests;

public class ProblemMappingTests
{
    [Theory]
    [InlineData(ErrorKind.Validation, 400)]
    [InlineData(ErrorKind.Unprocessable, 422)]
    [InlineData(ErrorKind.Unauthorized, 401)]
    [InlineData(ErrorKind.Forbidden, 403)]
    [InlineData(ErrorKind.NotFound, 404)]
    [InlineData(ErrorKind.Conflict, 409)]
    [InlineData(ErrorKind.TooLarge, 413)]
    [InlineData(ErrorKind.Unavailable, 503)]
    [InlineData(ErrorKind.Unexpected, 500)]
    public void ErrorKind_MapsToStatus(ErrorKind kind, int status) =>
        new Error("x.y", "msg", kind).ToProblem().Status.ShouldBe(status);

    [Fact]
    public void Problem_CarriesCodeDetailAndFieldErrors()
    {
        var error = Error.Validation("title.invalid", "Bad title.",
            new Dictionary<string, string[]> { ["title"] = ["Too short."] });

        var problem = error.ToProblem();

        problem.Detail.ShouldBe("Bad title.");
        problem.Extensions["code"].ShouldBe("title.invalid");
        problem.Extensions["errors"].ShouldBe(error.Errors);
    }

    [Fact]
    public void Problem_CarriesViolationsWithStableCodes_AndDerivedErrors()
    {
        var error = Error.Unprocessable("content_rejected", "No.",
        [
            new FieldViolation("title", "too_short", "Too short.", new Dictionary<string, object> { ["min"] = 6 }),
            new FieldViolation("title", "all_caps", "Caps."),
        ]);

        var problem = error.ToProblem();

        problem.Status.ShouldBe(422);
        problem.Extensions["violations"].ShouldBe(error.Violations);
        error.Errors!["title"].ShouldBe(["Too short.", "Caps."]);
    }

    [Fact]
    public void Problem_WithoutFieldErrors_OmitsErrorsExtension() =>
        Error.NotFound("a.b", "m").ToProblem().Extensions.ShouldNotContainKey("errors");

    [Theory]
    [InlineData(404, "not_found")]
    [InlineData(429, "rate_limited")]
    [InlineData(418, "http_418")]
    public void DefaultCode_IsStable(int status, string code) =>
        ProblemMapping.DefaultCodeFor(status).ShouldBe(code);
}
