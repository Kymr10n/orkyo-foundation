using Api.Services;
using Api.Services.Reporting;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orkyo.Shared;
using Xunit;

namespace Orkyo.Foundation.Tests.Services.Reporting;

/// <summary>
/// The pepper keys every reporting token hash. The chain that resolves it used to end in a
/// literal from the service file — a pepper anybody could read in the repository — so the
/// constructor now refuses to start rather than hash with a known value.
/// </summary>
public class ReportingTokenServiceTests
{
    private static ReportingTokenService Create(params (string Key, string? Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s =>
                new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

        return new ReportingTokenService(
            Mock.Of<IDbConnectionFactory>(),
            configuration,
            NullLogger<ReportingTokenService>.Instance);
    }

    [Fact]
    public void WithNeitherKeySet_RefusesToStart()
    {
        var thrown = Assert.Throws<InvalidOperationException>(() => Create());

        thrown.Message.Should().Contain(ConfigKeys.ReportingTokenPepper);
        thrown.Message.Should().Contain(ConfigKeys.KeycloakBackendClientSecret);
    }

    [Fact]
    public void WithBothKeysPresentButEmpty_RefusesToStart()
    {
        // An empty value counts as absent: the deploy pipeline writes KEY= for keys nobody
        // set, so "present but empty" is the shape an unset secret actually arrives in.
        var thrown = Assert.Throws<InvalidOperationException>(() => Create(
            (ConfigKeys.ReportingTokenPepper, ""),
            (ConfigKeys.KeycloakBackendClientSecret, "")));

        thrown.Message.Should().Contain(ConfigKeys.ReportingTokenPepper);
    }

    [Fact]
    public void WithOnlyTheKeycloakSecretSet_FallsBackToItAndStarts()
    {
        var service = Create((ConfigKeys.KeycloakBackendClientSecret, "a-real-client-secret"));

        service.Should().NotBeNull();
    }

    [Fact]
    public void WithTheDedicatedPepperSet_Starts()
    {
        var service = Create(
            (ConfigKeys.ReportingTokenPepper, "a-dedicated-pepper"),
            (ConfigKeys.KeycloakBackendClientSecret, "a-real-client-secret"));

        service.Should().NotBeNull();
    }
}
