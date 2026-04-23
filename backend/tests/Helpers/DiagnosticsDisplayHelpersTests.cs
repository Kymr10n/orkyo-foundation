using Api.Helpers;
using FluentAssertions;
using Xunit;

namespace Orkyo.Foundation.Tests.Helpers;

public class DiagnosticsDisplayHelpersTests
{
    [Fact]
    public void MaskHost_MasksMiddleLabelOfThreeLabelHost() =>
        DiagnosticsDisplayHelpers.MaskHost("smtp.example.com").Should().Be("smtp.*****.com");

    [Fact]
    public void MaskHost_MasksAllMiddleLabelsOfDeepHost() =>
        DiagnosticsDisplayHelpers.MaskHost("smtp.mail.eu.example.com")
            .Should().Be("smtp.*****.*****.*****.com");

    [Theory]
    [InlineData("example.com")]
    [InlineData("localhost")]
    public void MaskHost_LeavesTwoOrFewerLabelHostsUnchanged(string host) =>
        DiagnosticsDisplayHelpers.MaskHost(host).Should().Be(host);

    [Fact]
    public void MaskHost_PreservesLeafLabel() =>
        DiagnosticsDisplayHelpers.MaskHost("smtp.example.com").Should().StartWith("smtp.");

    [Fact]
    public void MaskHost_PreservesTld() =>
        DiagnosticsDisplayHelpers.MaskHost("smtp.example.com").Should().EndWith(".com");

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void MaskHost_HandlesNullOrEmptyByPassThrough(string? host) =>
        DiagnosticsDisplayHelpers.MaskHost(host!).Should().Be(host);
}
