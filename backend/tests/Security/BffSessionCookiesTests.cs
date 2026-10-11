using Api.Configuration;
using Api.Security;
using Microsoft.AspNetCore.Http;

namespace Orkyo.Foundation.Tests.Security;

/// <summary>
/// The login binding cookie follows the session cookie's Secure and Domain settings, so it
/// reaches the callback wherever the session cookie would.
/// </summary>
public class BffSessionCookiesTests
{
    [Fact]
    public void LoginBindingCookie_IsSecureWhenTheSessionCookieIs()
    {
        var ctx = new DefaultHttpContext();
        var options = new BffOptions { CookieSecure = true, CookieDomain = ".orkyo.com" };

        BffSessionCookies.WriteLoginBindingCookie(ctx, options, "value", TimeSpan.FromMinutes(10));

        var cookie = ctx.Response.Headers.SetCookie.ToString();
        cookie.Should().StartWith("orkyo-bff-login=value");
        cookie.Should().Contain("secure");
        cookie.Should().Contain("domain=.orkyo.com");
        cookie.Should().Contain("path=/api/auth/bff");
        cookie.Should().Contain("samesite=lax");
        cookie.Should().Contain("httponly");
    }
}
