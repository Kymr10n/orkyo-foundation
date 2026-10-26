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

    [Theory]
    [InlineData("https://evil.com/")]              // unrelated host
    [InlineData("https://evil-orkyo.com/")]        // suffix attack
    [InlineData("javascript:alert(1)")]            // not an http(s) URL
    [InlineData("/login")]                         // relative
    public void IsReturnToAllowed_RejectsAnUnlistedTarget(string returnTo)
    {
        var options = new BffOptions { AllowedReturnToHosts = ["orkyo.com", "*.orkyo.com"] };
        options.IsReturnToAllowed(returnTo).Should().BeFalse();
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

    // ── GetDefaultReturnToBase ────────────────────────────────────────────────

    [Fact]
    public void GetDefaultReturnToBase_PrefersAppBaseUrl_PreservingPort()
    {
        // Regression: the host-only AllowedReturnToHosts list drops the port, producing
        // http://localhost (a dead port-80 URL). AppBaseUrl carries the port.
        var options = new BffOptions
        {
            AppBaseUrl = "http://localhost:5174",
            AllowedReturnToHosts = ["localhost", "*.localhost"],
            RedirectUri = "http://localhost:5002/api/auth/bff/callback",
        };

        options.GetDefaultReturnToBase().Should().Be("http://localhost:5174");
    }

    [Fact]
    public void GetDefaultReturnToBase_TrimsTrailingSlashFromAppBaseUrl()
    {
        var options = new BffOptions { AppBaseUrl = "https://orkyo.com/" };

        options.GetDefaultReturnToBase().Should().Be("https://orkyo.com");
    }

    [Fact]
    public void GetDefaultReturnToBase_FallsBackToHttpsHost_WhenAppBaseUrlEmpty()
    {
        var options = new BffOptions
        {
            AllowedReturnToHosts = ["orkyo.com", "*.orkyo.com"],
            RedirectUri = "https://orkyo.com/api/auth/bff/callback",
        };

        options.GetDefaultReturnToBase().Should().Be("https://orkyo.com");
    }

    [Fact]
    public void GetDefaultReturnToBase_FallbackDerivesHttpScheme_InDev()
    {
        var options = new BffOptions
        {
            AllowedReturnToHosts = ["localhost:5173"],
            RedirectUri = "http://localhost:5002/api/auth/bff/callback",
        };

        options.GetDefaultReturnToBase().Should().Be("http://localhost:5173");
    }

    [Fact]
    public void GetDefaultReturnToBase_ReturnsNull_WhenOnlyWildcardHostsAndNoAppBaseUrl()
    {
        var options = new BffOptions
        {
            AllowedReturnToHosts = ["*.orkyo.com"],
            RedirectUri = "https://orkyo.com/api/auth/bff/callback",
        };

        options.GetDefaultReturnToBase().Should().BeNull();
    }

    [Fact]
    public void GetDefaultReturnToBase_ReturnsNull_WhenNoHostsAndNoAppBaseUrl()
    {
        var options = new BffOptions { AllowedReturnToHosts = [] };

        options.GetDefaultReturnToBase().Should().BeNull();
    }
}
