using System.Text;
using Api.Services.PlatformApi;
using AwesomeAssertions;
using Xunit;

namespace Orkyo.Foundation.Tests.Services.PlatformApi;

/// <summary>
/// The token format and crypto shared by every bearer-credential class. These used to live inside
/// ReportingTokenService with no direct coverage; extracting them for the API-access token made
/// them testable, which matters more now that one of the classes can write.
/// </summary>
public class TokenCredentialHelperTests
{
    private static readonly byte[] Pepper = Encoding.UTF8.GetBytes("a-test-pepper");

    [Fact]
    public void Generate_ProducesATokenThatParsesBackToTheSamePrefix()
    {
        var generated = TokenCredentialHelper.Generate("orkyo_api", Pepper);

        TokenCredentialHelper.TryParse(generated.RawToken, "orkyo_api", out var prefix, out var secret)
            .Should().BeTrue();
        prefix.Should().Be(generated.Prefix);
        secret.Should().HaveCount(32);
    }

    [Fact]
    public void Generate_ProducesAHashThatMatchesTheSecretItReturned()
    {
        var generated = TokenCredentialHelper.Generate("orkyo_api", Pepper);
        TokenCredentialHelper.TryParse(generated.RawToken, "orkyo_api", out _, out var secret);

        var recomputed = TokenCredentialHelper.ComputeHash(secret, Pepper);

        TokenCredentialHelper.HashesMatch(recomputed, generated.Hash).Should().BeTrue();
    }

    [Fact]
    public void ComputeHash_WithADifferentPepper_DoesNotMatch()
    {
        // Why the two credential classes get separate peppers: a leak of one must not make the
        // other's stored hashes forgeable.
        var generated = TokenCredentialHelper.Generate("orkyo_api", Pepper);
        TokenCredentialHelper.TryParse(generated.RawToken, "orkyo_api", out _, out var secret);

        var otherPepper = TokenCredentialHelper.ComputeHash(secret, Encoding.UTF8.GetBytes("other"));

        TokenCredentialHelper.HashesMatch(otherPepper, generated.Hash).Should().BeFalse();
    }

    [Fact]
    public void Generate_IsUniquePerCall()
    {
        var a = TokenCredentialHelper.Generate("orkyo_api", Pepper);
        var b = TokenCredentialHelper.Generate("orkyo_api", Pepper);

        a.RawToken.Should().NotBe(b.RawToken);
        a.Prefix.Should().NotBe(b.Prefix);
        a.Hash.Should().NotBe(b.Hash);
    }

    [Fact]
    public void TryParse_RejectsATokenOfAnotherScheme()
    {
        // This is how each auth handler cheaply ignores the other class's credential instead of
        // doing a wasted database lookup for a token that could never be its own.
        var reportingToken = TokenCredentialHelper.Generate("orkyo_rpt", Pepper);

        TokenCredentialHelper.TryParse(reportingToken.RawToken, "orkyo_api", out _, out _)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("orkyo_api")]
    [InlineData("orkyo_api_noSecretSeparator")]
    [InlineData("orkyo_api_prefix_!!!not-base64!!!")]
    public void TryParse_RejectsMalformedTokens(string raw)
    {
        TokenCredentialHelper.TryParse(raw, "orkyo_api", out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_RejectsASecretOfTheWrongLength()
    {
        // A short secret is a truncated or hand-made token; accepting one would shrink the
        // brute-force space without anything else noticing.
        var shortSecret = Convert.ToBase64String(new byte[8]).TrimEnd('=');

        TokenCredentialHelper.TryParse($"orkyo_api_abcdefgh_{shortSecret}", "orkyo_api", out _, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void ExtractPrefix_ReturnsThePrefixWithoutTheSecret()
    {
        var generated = TokenCredentialHelper.Generate("orkyo_api", Pepper);

        var prefix = TokenCredentialHelper.ExtractPrefix(generated.RawToken, "orkyo_api");

        prefix.Should().Be(generated.Prefix);
        generated.RawToken.Should().Contain(prefix!);
        // The point of the helper: a failure can be logged by prefix without logging the secret.
        prefix!.Length.Should().BeLessThan(generated.RawToken.Length);
    }

    [Fact]
    public void ExtractPrefix_ReturnsNullForAnotherScheme()
    {
        TokenCredentialHelper.ExtractPrefix("orkyo_rpt_abcdefgh_secret", "orkyo_api")
            .Should().BeNull();
    }

    [Fact]
    public void ResolvePepper_PrefersThePrimaryValue()
    {
        var pepper = TokenCredentialHelper.ResolvePepper("primary", "fallback", "ctx");

        pepper.Should().Equal(Encoding.UTF8.GetBytes("primary"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ResolvePepper_FallsBackWhenThePrimaryIsAbsentOrEmpty(string? primary)
    {
        // "Present but empty" is the shape an unset secret actually arrives in: the deploy
        // pipeline writes KEY= for keys nobody set.
        var pepper = TokenCredentialHelper.ResolvePepper(primary, "fallback", "ctx");

        pepper.Should().Equal(Encoding.UTF8.GetBytes("fallback"));
    }

    [Fact]
    public void ResolvePepper_WithNeitherValue_RefusesRatherThanHashingWithAKnownPepper()
    {
        var thrown = Assert.Throws<InvalidOperationException>(
            () => TokenCredentialHelper.ResolvePepper(null, null, "ApiAccessTokenService: nothing set"));

        thrown.Message.Should().Contain("ApiAccessTokenService: nothing set");
    }
}
