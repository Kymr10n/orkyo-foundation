using Api.Services;
using FluentAssertions;
using Xunit;

namespace Orkyo.Foundation.Tests.Services;

public class OidcDiscoveryUrlPolicyTests
{
    [Fact]
    public void BuildDiscoveryUrl_AppendsWellKnownSuffix() =>
        OidcDiscoveryUrlPolicy.BuildDiscoveryUrl("https://kc.example.com/realms/orkyo")
            .Should().Be("https://kc.example.com/realms/orkyo/.well-known/openid-configuration");

    [Fact]
    public void BuildDiscoveryUrl_TrimsSingleTrailingSlashToAvoidDoubleSlash() =>
        OidcDiscoveryUrlPolicy.BuildDiscoveryUrl("https://kc.example.com/realms/orkyo/")
            .Should().Be("https://kc.example.com/realms/orkyo/.well-known/openid-configuration");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildDiscoveryUrl_ReturnsNullForNullOrWhitespaceAuthority(string? authority) =>
        OidcDiscoveryUrlPolicy.BuildDiscoveryUrl(authority).Should().BeNull();

    [Fact]
    public void InterpretProbeOutcome_TrueMapsToConnected() =>
        OidcDiscoveryUrlPolicy.InterpretProbeOutcome(true).Should().Be("connected");

    [Fact]
    public void InterpretProbeOutcome_FalseMapsToUnreachable() =>
        OidcDiscoveryUrlPolicy.InterpretProbeOutcome(false).Should().Be("unreachable");

    [Fact]
    public void StatusConstants_AreStable()
    {
        OidcDiscoveryUrlPolicy.NotConfiguredStatus.Should().Be("not-configured");
        OidcDiscoveryUrlPolicy.ConnectedStatus.Should().Be("connected");
        OidcDiscoveryUrlPolicy.UnreachableStatus.Should().Be("unreachable");
    }
}
