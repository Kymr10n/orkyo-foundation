using Api.Integrations.Keycloak;

namespace Orkyo.Foundation.Tests.Integrations.Keycloak;

public class KeycloakInternalProxyPolicyTests
{
    [Fact]
    public void ForwardedHeaderConstants_AreLockedAgainstDrift()
    {
        KeycloakInternalProxyPolicy.ForwardedProtoHeader.Should().Be("X-Forwarded-Proto");
        KeycloakInternalProxyPolicy.ForwardedHostHeader.Should().Be("X-Forwarded-Host");
    }

    [Fact]
    public void ShouldSetForwardedHeaders_ReturnsFalse_WhenInternalUrlMissing()
    {
        KeycloakInternalProxyPolicy.ShouldSetForwardedHeaders("https://auth.example.com", null).Should().BeFalse();
        KeycloakInternalProxyPolicy.ShouldSetForwardedHeaders("https://auth.example.com", "").Should().BeFalse();
    }

    [Fact]
    public void ShouldSetForwardedHeaders_ReturnsFalse_WhenInternalEqualsPublic()
    {
        KeycloakInternalProxyPolicy
            .ShouldSetForwardedHeaders("https://auth.example.com", "https://auth.example.com")
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldSetForwardedHeaders_ReturnsTrue_WhenInternalDiffersFromPublic()
    {
        KeycloakInternalProxyPolicy
            .ShouldSetForwardedHeaders("https://auth.example.com", "http://keycloak:8080")
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("https://auth.example.com", "auth.example.com")]
    [InlineData("https://auth.example.com/", "auth.example.com")]
    [InlineData("http://localhost", "localhost")]
    public void BuildForwardedHost_OmitsDefaultPort(string publicBaseUrl, string expected)
    {
        KeycloakInternalProxyPolicy.BuildForwardedHost(publicBaseUrl).Should().Be(expected);
    }

    [Theory]
    [InlineData("https://auth.example.com:8443", "auth.example.com:8443")]
    [InlineData("http://localhost:8080", "localhost:8080")]
    public void BuildForwardedHost_IncludesNonDefaultPort(string publicBaseUrl, string expected)
    {
        KeycloakInternalProxyPolicy.BuildForwardedHost(publicBaseUrl).Should().Be(expected);
    }

    [Theory]
    [InlineData("https://auth.example.com", "https")]
    [InlineData("http://localhost:8080", "http")]
    public void BuildForwardedProto_ReturnsUrlScheme(string publicBaseUrl, string expected)
    {
        KeycloakInternalProxyPolicy.BuildForwardedProto(publicBaseUrl).Should().Be(expected);
    }
}
