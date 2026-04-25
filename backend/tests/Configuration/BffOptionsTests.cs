using Api.Configuration;

namespace Orkyo.Foundation.Tests.Configuration;

public class BffOptionsTests
{
    [Fact]
    public void IsReturnToAllowed_ExactMatch()
    {
        var options = new BffOptions { AllowedReturnToHosts = ["orkyo.com"] };
        options.IsReturnToAllowed("https://orkyo.com/").Should().BeTrue();
        options.IsReturnToAllowed("https://orkyo.com/dashboard").Should().BeTrue();
    }

    [Fact]
    public void IsReturnToAllowed_WildcardMatch()
    {
        var options = new BffOptions { AllowedReturnToHosts = ["*.orkyo.com"] };
        options.IsReturnToAllowed("https://demo.orkyo.com/").Should().BeTrue();
        options.IsReturnToAllowed("https://staging.orkyo.com/login").Should().BeTrue();
    }

    [Fact]
    public void IsReturnToAllowed_WildcardAlsoMatchesApex()
    {
        var options = new BffOptions { AllowedReturnToHosts = ["*.orkyo.com"] };
        options.IsReturnToAllowed("https://orkyo.com/").Should().BeTrue();
    }

    [Fact]
    public void IsReturnToAllowed_RejectsUnrelatedHost()
    {
        var options = new BffOptions { AllowedReturnToHosts = ["orkyo.com", "*.orkyo.com"] };
        options.IsReturnToAllowed("https://evil.com/").Should().BeFalse();
    }

    [Fact]
    public void IsReturnToAllowed_RejectsSuffixAttack()
    {
        var options = new BffOptions { AllowedReturnToHosts = ["orkyo.com"] };
        options.IsReturnToAllowed("https://evil-orkyo.com/").Should().BeFalse();
    }

    [Fact]
    public void IsReturnToAllowed_RejectsJavascriptScheme()
    {
        var options = new BffOptions { AllowedReturnToHosts = ["orkyo.com"] };
        options.IsReturnToAllowed("javascript:alert(1)").Should().BeFalse();
    }

    [Fact]
    public void IsReturnToAllowed_RejectsRelativeUrl()
    {
        var options = new BffOptions { AllowedReturnToHosts = ["orkyo.com"] };
        options.IsReturnToAllowed("/login").Should().BeFalse();
    }

    [Fact]
    public void IsReturnToAllowed_AllowsHttpInDev()
    {
        var options = new BffOptions { AllowedReturnToHosts = ["localhost"], CookieSecure = false };
        options.IsReturnToAllowed("http://localhost:5173/").Should().BeTrue();
    }

    [Fact]
    public void IsReturnToAllowed_RejectsHttpWhenCookieSecure()
    {
        var options = new BffOptions { AllowedReturnToHosts = ["orkyo.com"], CookieSecure = true };
        options.IsReturnToAllowed("http://orkyo.com/").Should().BeFalse();
        options.IsReturnToAllowed("https://orkyo.com/").Should().BeTrue();
    }

    [Fact]
    public void IsReturnToAllowed_CaseInsensitive()
    {
        var options = new BffOptions { AllowedReturnToHosts = ["orkyo.com"] };
        options.IsReturnToAllowed("https://ORKYO.COM/").Should().BeTrue();
    }

    [Fact]
    public void IsReturnToAllowed_EmptyAllowList_RejectsAll()
    {
        var options = new BffOptions { AllowedReturnToHosts = [] };
        options.IsReturnToAllowed("https://orkyo.com/").Should().BeFalse();
    }
}
