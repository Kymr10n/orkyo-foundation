using Api.Services;
using FluentAssertions;
using Xunit;

namespace Orkyo.Foundation.Tests.Services;

public class EmailTokenLinkBuilderTests
{
    [Fact]
    public void Build_ProducesCanonicalLinkShape() =>
        EmailTokenLinkBuilder
            .Build("https://app.example.com", "verify-email", "token", "abc123")
            .Should().Be("https://app.example.com/verify-email?token=abc123");

    [Fact]
    public void Build_UrlEscapesTokenValueOnly()
    {
        var link = EmailTokenLinkBuilder.Build(
            "https://app.example.com", "reset-password", "token", "a b/c+d=e?f&g");

        // EscapeDataString percent-encodes every reserved character.
        link.Should().Be(
            "https://app.example.com/reset-password?token=a%20b%2Fc%2Bd%3De%3Ff%26g");
    }

    [Fact]
    public void Build_DoesNotEscapePathSegment() =>
        EmailTokenLinkBuilder
            .Build("https://app.example.com", "api/account/confirm-activity", "token", "t")
            .Should().Be("https://app.example.com/api/account/confirm-activity?token=t");

    [Fact]
    public void Build_DoesNotEscapeQueryParamName() =>
        EmailTokenLinkBuilder
            .Build("https://app.example.com", "signup", "invitation", "tok")
            .Should().Be("https://app.example.com/signup?invitation=tok");

    [Fact]
    public void Build_PreservesEmptyTokenAsEmptyValue() =>
        EmailTokenLinkBuilder
            .Build("https://app.example.com", "p", "q", string.Empty)
            .Should().Be("https://app.example.com/p?q=");
}
