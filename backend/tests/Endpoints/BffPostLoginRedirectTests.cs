using Api.Configuration;
using Api.Endpoints;
using Api.Middleware;
using Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Unit tests for <see cref="BffAuthEndpoints.ResolvePostLoginRedirectAsync"/>.
/// Covers local dev (no BaseDomain), staging (with SubdomainPrefix), and production.
/// Regression test for the bug where local dev produced "{slug}.localhost" redirect.
/// </summary>
public class BffPostLoginRedirectTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly NullLogger Logger = NullLogger.Instance;

    private static SessionBootstrapResponse SingleTenantSession(string slug) => new()
    {
        User = new UserInfo
        {
            Id = UserId,
            Email = "test@example.com",
            DisplayName = "Test User",
            CreatedAt = DateTime.UtcNow,
        },
        Tenants = [new TenantMembershipInfo
        {
            TenantId = Guid.NewGuid(),
            Slug = slug,
            DisplayName = "Demo Corp",
            Role = "owner",
            State = "active",
            Tier = "free",
        }],
    };

    private static Mock<ISessionService> MockSessionService(SessionBootstrapResponse? response)
    {
        var mock = new Mock<ISessionService>();
        mock.Setup(s => s.BuildSessionResponseAsync(UserId))
            .ReturnsAsync(response);
        return mock;
    }

    // ── Local dev: no BaseDomain → returnTo unchanged ────────────────────────

    [Fact]
    public async Task LocalDev_NoBaseDomain_ReturnsOriginalReturnTo()
    {
        var bffOptions = new BffOptions
        {
            CookieSecure = false,
            AllowedReturnToHosts = ["localhost:5173"],
            RedirectUri = "http://localhost:8080/api/auth/bff/callback",
        };
        var tenantOptions = new TenantMiddlewareOptions { BaseDomain = null };
        var session = MockSessionService(SingleTenantSession("chiefs"));

        var result = await BffAuthEndpoints.ResolvePostLoginRedirectAsync(
            "http://localhost:5173/", UserId, false, bffOptions, session.Object, tenantOptions, Logger);

        Assert.Equal("http://localhost:5173/", result);
    }

    [Fact]
    public async Task LocalDev_EmptyBaseDomain_ReturnsOriginalReturnTo()
    {
        var bffOptions = new BffOptions
        {
            CookieSecure = false,
            AllowedReturnToHosts = ["localhost:5173"],
            RedirectUri = "http://localhost:8080/api/auth/bff/callback",
        };
        var tenantOptions = new TenantMiddlewareOptions { BaseDomain = "" };
        var session = MockSessionService(SingleTenantSession("chiefs"));

        var result = await BffAuthEndpoints.ResolvePostLoginRedirectAsync(
            "http://localhost:5173/", UserId, false, bffOptions, session.Object, tenantOptions, Logger);

        Assert.Equal("http://localhost:5173/", result);
    }

    // ── Production: BaseDomain configured → redirect to tenant subdomain ─────

    [Fact]
    public async Task Production_SingleTenant_RedirectsToTenantSubdomain()
    {
        var bffOptions = new BffOptions
        {
            CookieSecure = true,
            AllowedReturnToHosts = ["orkyo.com", "*.orkyo.com"],
            RedirectUri = "https://orkyo.com/api/auth/bff/callback",
        };
        var tenantOptions = new TenantMiddlewareOptions { BaseDomain = "orkyo.com" };
        var session = MockSessionService(SingleTenantSession("demo"));

        var result = await BffAuthEndpoints.ResolvePostLoginRedirectAsync(
            "https://orkyo.com/", UserId, false, bffOptions, session.Object, tenantOptions, Logger);

        Assert.Equal("https://demo.orkyo.com/", result);
    }

    // ── Staging: BaseDomain + SubdomainPrefix → prefixed subdomain ───────────

    [Fact]
    public async Task Staging_SingleTenant_RedirectsWithPrefix()
    {
        var bffOptions = new BffOptions
        {
            CookieSecure = true,
            AllowedReturnToHosts = ["staging.orkyo.com", "*.orkyo.com"],
            RedirectUri = "https://staging.orkyo.com/api/auth/bff/callback",
        };
        var tenantOptions = new TenantMiddlewareOptions
        {
            BaseDomain = "orkyo.com",
            SubdomainPrefix = "staging-",
        };
        var session = MockSessionService(SingleTenantSession("demo"));

        var result = await BffAuthEndpoints.ResolvePostLoginRedirectAsync(
            "https://staging.orkyo.com/", UserId, false, bffOptions, session.Object, tenantOptions, Logger);

        Assert.Equal("https://staging-demo.orkyo.com/", result);
    }

    // ── Multi-tenant user → no redirect (returns original) ──────────────────

    [Fact]
    public async Task MultiTenant_ReturnsOriginalReturnTo()
    {
        var bffOptions = new BffOptions
        {
            CookieSecure = true,
            AllowedReturnToHosts = ["orkyo.com", "*.orkyo.com"],
            RedirectUri = "https://orkyo.com/api/auth/bff/callback",
        };
        var tenantOptions = new TenantMiddlewareOptions { BaseDomain = "orkyo.com" };

        var multiTenantSession = new SessionBootstrapResponse
        {
            User = new UserInfo
            {
                Id = UserId,
                Email = "test@example.com",
                DisplayName = "Test",
                CreatedAt = DateTime.UtcNow,
            },
            Tenants =
            [
                new TenantMembershipInfo
                {
                    TenantId = Guid.NewGuid(),
                    Slug = "a",
                    DisplayName = "A",
                    Role = "owner",
                    State = "active",
                    Tier = "free"
                },
                new TenantMembershipInfo
                {
                    TenantId = Guid.NewGuid(),
                    Slug = "b",
                    DisplayName = "B",
                    Role = "member",
                    State = "active",
                    Tier = "free"
                },
            ],
        };
        var session = MockSessionService(multiTenantSession);

        var result = await BffAuthEndpoints.ResolvePostLoginRedirectAsync(
            "https://orkyo.com/", UserId, false, bffOptions, session.Object, tenantOptions, Logger);

        Assert.Equal("https://orkyo.com/", result);
    }

    // ── Site admin → no redirect (returns original) ─────────────────────────

    [Fact]
    public async Task SiteAdmin_ReturnsOriginalReturnTo()
    {
        var bffOptions = new BffOptions
        {
            CookieSecure = true,
            AllowedReturnToHosts = ["orkyo.com", "*.orkyo.com"],
            RedirectUri = "https://orkyo.com/api/auth/bff/callback",
        };
        var tenantOptions = new TenantMiddlewareOptions { BaseDomain = "orkyo.com" };
        var session = MockSessionService(SingleTenantSession("demo"));

        var result = await BffAuthEndpoints.ResolvePostLoginRedirectAsync(
            "https://orkyo.com/", UserId, true, bffOptions, session.Object, tenantOptions, Logger);

        Assert.Equal("https://orkyo.com/", result);
    }

    // ── Tenant URL not in AllowedReturnToHosts → no redirect ────────────────

    [Fact]
    public async Task TenantUrlNotAllowed_ReturnsOriginalReturnTo()
    {
        var bffOptions = new BffOptions
        {
            CookieSecure = true,
            AllowedReturnToHosts = ["orkyo.com"],  // exact, no wildcard
            RedirectUri = "https://orkyo.com/api/auth/bff/callback",
        };
        var tenantOptions = new TenantMiddlewareOptions { BaseDomain = "orkyo.com" };
        var session = MockSessionService(SingleTenantSession("demo"));

        var result = await BffAuthEndpoints.ResolvePostLoginRedirectAsync(
            "https://orkyo.com/", UserId, false, bffOptions, session.Object, tenantOptions, Logger);

        // demo.orkyo.com is not allowed (no wildcard), so returns original
        Assert.Equal("https://orkyo.com/", result);
    }
}
