using Api.Security;

namespace Orkyo.Foundation.Tests.Security;

public class UserAgentParserTests
{
    [Theory]
    // Chrome on Windows desktop
    [InlineData(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36",
        "Chrome", "Windows", UserAgentParser.DeviceDesktop)]
    // Safari on macOS desktop
    [InlineData(
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Safari/605.1.15",
        "Safari", "macOS", UserAgentParser.DeviceDesktop)]
    // Safari on iPhone → mobile
    [InlineData(
        "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1",
        "Safari", "iOS", UserAgentParser.DeviceMobile)]
    // Chrome on Android phone → mobile
    [InlineData(
        "Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Mobile Safari/537.36",
        "Chrome", "Android", UserAgentParser.DeviceMobile)]
    // iPad → tablet
    [InlineData(
        "Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Safari/604.1",
        "Safari", "iOS", UserAgentParser.DeviceTablet)]
    // Android tablet (no "Mobile") → tablet
    [InlineData(
        "Mozilla/5.0 (Linux; Android 13; SM-X700) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36",
        "Chrome", "Android", UserAgentParser.DeviceTablet)]
    // Edge takes precedence over the Chrome token it also carries
    [InlineData(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36 Edg/120.0",
        "Edge", "Windows", UserAgentParser.DeviceDesktop)]
    // Firefox on Linux
    [InlineData(
        "Mozilla/5.0 (X11; Linux x86_64; rv:121.0) Gecko/20100101 Firefox/121.0",
        "Firefox", "Linux", UserAgentParser.DeviceDesktop)]
    public void Parse_KnownAgents_ReturnsExpected(string ua, string browser, string os, string deviceType)
    {
        var result = UserAgentParser.Parse(ua);
        result.Browser.Should().Be(browser);
        result.OperatingSystem.Should().Be(os);
        result.DeviceType.Should().Be(deviceType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_EmptyOrNull_ReturnsUnknown(string? ua)
    {
        var result = UserAgentParser.Parse(ua);
        result.Browser.Should().BeNull();
        result.OperatingSystem.Should().BeNull();
        result.DeviceType.Should().Be(UserAgentParser.DeviceUnknown);
    }

    [Fact]
    public void Parse_UnrecognizedAgent_ReturnsDesktopWithNullFields()
    {
        var result = UserAgentParser.Parse("curl/8.4.0");
        result.Browser.Should().BeNull();
        result.OperatingSystem.Should().BeNull();
        // No mobile markers → defaults to desktop.
        result.DeviceType.Should().Be(UserAgentParser.DeviceDesktop);
    }
}
