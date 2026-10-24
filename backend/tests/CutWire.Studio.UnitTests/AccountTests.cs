using CutWire.Studio.Domain.Accounts;
using CutWire.Studio.Infrastructure.Oidc;
using Shouldly;

namespace CutWire.Studio.UnitTests;

public class UserHandleTests
{
    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("  Alice_01  ", "alice_01")]
    [InlineData("a-b-c", "a-b-c")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWX", "abcdefghijklmnopqrstuvwx")]
    public void Normalize_AcceptsValidHandlesAndLowercases(string input, string expected)
    {
        var result = UserHandle.Normalize(input);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("abcdefghijklmnopqrstuvwxy")]
    [InlineData("has space")]
    [InlineData("dot.ted")]
    [InlineData("émile")]
    [InlineData("a@b")]
    public void Normalize_RejectsInvalidHandles(string? input)
    {
        var result = UserHandle.Normalize(input);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("profile.invalid_handle");
        result.Error.Errors!.ShouldContainKey("handle");
    }
}

public class PatTokenTests
{
    [Fact]
    public void Generate_ProducesPrefixedBase32OfThirtyTwoBytes()
    {
        var token = PatToken.Generate();

        token.ShouldStartWith("cwpat_");
        token["cwpat_".Length..].Length.ShouldBe(52);
        token["cwpat_".Length..].ShouldAllBe(c => char.IsAsciiLetterLower(c) || (c >= '2' && c <= '7'));
        PatToken.Generate().ShouldNotBe(token);
    }

    [Fact]
    public void Hash_IsDeterministicSha256HexAndDoesNotContainTheToken()
    {
        var token = PatToken.Generate();

        PatToken.Hash(token).ShouldBe(PatToken.Hash(token));
        PatToken.Hash(token).Length.ShouldBe(64);
        PatToken.Hash(token).ShouldNotContain(token);
        PatToken.Hash("").ShouldBe("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }

    [Fact]
    public void Base32_MatchesRfc4648Vectors()
    {
        PatToken.Base32("foobar"u8).ShouldBe("mzxw6ytboi");
        PatToken.Base32("fo"u8).ShouldBe("mzxq");
    }

    [Theory]
    [InlineData("cwpat_x", true)]
    [InlineData("eyJhbGciOi", false)]
    [InlineData(null, false)]
    public void HasPrefix_DetectsPatShapedTokens(string? token, bool expected) =>
        PatToken.HasPrefix(token).ShouldBe(expected);
}

public class RedirectUriMatcherTests
{
    private static readonly string[] Registered = ["http://127.0.0.1/callback", "cutwire://auth/callback", "https://app.example/cb"];

    [Theory]
    [InlineData("http://127.0.0.1/callback", true)]
    [InlineData("http://127.0.0.1:51234/callback", true)]
    [InlineData("cutwire://auth/callback", true)]
    [InlineData("https://app.example/cb", true)]
    [InlineData("http://127.0.0.1:51234/other", false)]
    [InlineData("http://127.0.0.1:51234/callback?x=1", false)]
    [InlineData("http://localhost:51234/callback", false)]
    [InlineData("https://127.0.0.1:51234/callback", false)]
    [InlineData("https://app.example:8443/cb", false)]
    [InlineData("cutwire://auth/other", false)]
    public void Matches_IsPortAgnosticOnlyForRegisteredHttpLoopback(string candidate, bool expected) =>
        RedirectUriMatcher.Matches(Registered, candidate).ShouldBe(expected);

    [Fact]
    public void Matches_SupportsIpv6Loopback() =>
        RedirectUriMatcher.Matches(["http://[::1]/callback"], "http://[::1]:4000/callback").ShouldBeTrue();
}
