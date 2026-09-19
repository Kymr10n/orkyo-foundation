using Api.Configuration;
using Api.Integrations.Keycloak;
using Api.Middleware;
using Api.PlatformApi.Auth;
using Api.Security;
using Api.Services;
using Api.Services.PlatformApi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkyo.Foundation.Tests.Integration;
using Orkyo.Foundation.Tests.Mocks;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests;

/// <summary>
/// Builds an in-process <see cref="WebApplication"/> against a real test database by composing
/// over the same <c>AddFoundationServices</c> / <c>AddFoundationRateLimiting</c> /
/// <c>MapFoundationEndpoints</c> extensions the products call from their <c>Program.cs</c>.
/// Only the edition-owned registrations (connection factory, tenant/org context, quota, audit)
/// and the test doubles for external systems are declared here, the way a product's
/// <c>ApiWebApplicationFactory</c> layers them over production DI.
///
/// Security context (principal, tenant, authorization) is populated by a lightweight
/// test middleware rather than <c>ContextEnrichmentMiddleware</c>, which requires Keycloak.
/// </summary>
public sealed class FoundationWebApplicationFactory : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly MockKeycloakAdminService _mockKeycloak;
    private readonly MockEmailService _mockEmail;
    private readonly Mocks.TestAccountMutationGuard _accountGuard;

    /// <summary>Shared Keycloak mock — tests can inspect calls and configure responses.</summary>
    public MockKeycloakAdminService MockKeycloakAdminService => _mockKeycloak;

    /// <summary>Shared email mock — tests can inspect calls and configure responses.</summary>
    public MockEmailService MockEmailService => _mockEmail;

    /// <summary>Toggleable account-lock guard — flip <c>Locked</c> to exercise shared/demo-account paths.</summary>
    public Mocks.TestAccountMutationGuard AccountGuard => _accountGuard;

    /// <summary>Exposes the application's service provider for advanced test scenarios.</summary>
    public IServiceProvider Services => ((WebApplication)_host).Services;

    private FoundationWebApplicationFactory(IHost host, MockKeycloakAdminService mockKeycloak, MockEmailService mockEmail, Mocks.TestAccountMutationGuard accountGuard)
    {
        _host = host;
        _mockKeycloak = mockKeycloak;
        _mockEmail = mockEmail;
        _accountGuard = accountGuard;
    }

    // ── Public surface ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a bare <see cref="HttpClient"/> for the test server: no tenant or authorization
    /// headers (see <see cref="DatabaseFixture.CreateAuthorizedClient"/> for those). TestServer
    /// clients never follow redirects, so a test can assert on a <c>Location</c> header directly.
    /// </summary>
    public HttpClient CreateClient()
    {
        return _host.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    // ── Factory ───────────────────────────────────────────────────────────────

    public static async Task<FoundationWebApplicationFactory> CreateAsync(
        string tenantConnectionString,
        string controlPlaneConnectionString)
    {
        var mockKeycloak = new MockKeycloakAdminService();
        var mockEmail = new MockEmailService();
        var accountGuard = new Mocks.TestAccountMutationGuard();
        var app = BuildWebApplication(tenantConnectionString, controlPlaneConnectionString, mockKeycloak, mockEmail, accountGuard);
        await app.StartAsync();
        return new FoundationWebApplicationFactory(app, mockKeycloak, mockEmail, accountGuard);
    }

    // ── App bootstrap ─────────────────────────────────────────────────────────

    private static WebApplication BuildWebApplication(
        string tenantCs,
        string controlPlaneCs,
        MockKeycloakAdminService mockKeycloak,
        MockEmailService mockEmail,
        Mocks.TestAccountMutationGuard accountGuard)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
        });
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        // In-memory config so services that read IConfiguration get sensible values
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["APP_BASE_URL"] = "http://localhost:5173",
            ["SMTP_HOST"] = "localhost",
            ["SMTP_PORT"] = "1025",
            ["SMTP_USE_SSL"] = "false",
            ["SMTP_FROM_EMAIL"] = "test@test.local",
            ["SMTP_FROM_NAME"] = "Test",
            ["FEEDBACK_NOTIFICATION_EMAIL"] = "feedback@test.local",
            // Keycloak / OIDC — AddFoundationServices reads these for KeycloakOptions and the
            // JWT bearer scheme. The values match the DeploymentConfig singleton below; no
            // test ever reaches Keycloak (the admin client is mocked and the default auth
            // scheme is the test handler), so the host only has to be well-formed.
            [ConfigKeys.OidcAuthority] = "http://localhost:8080/realms/orkyo",
            [ConfigKeys.KeycloakUrl] = "http://localhost:8080",
            [ConfigKeys.KeycloakRealm] = "orkyo",
            [ConfigKeys.KeycloakBackendClientId] = "test-backend",
            // ReportingTokenService refuses to start without a pepper source (no compiled
            // fallback — fail-early rule); tests use the same stand-in secret as the
            // DeploymentConfig singleton below.
            ["KEYCLOAK_BACKEND_CLIENT_SECRET"] = "test-secret",
            // BFF auth — enabled with test-friendly settings so BFF endpoints register.
            ["BFF_ENABLED"] = "true",
            ["BFF_COOKIE_SECURE"] = "false",
            ["BFF_REDIRECT_URI"] = "http://localhost:5173/api/auth/bff/callback",
            ["BFF_ALLOWED_HOSTS"] = "demo.orkyo.com,localhost:5173,orkyo.com,*.orkyo.com",
            // ToS — current required version. Tests assert against "2026-02".
            ["ToS:RequiredVersion"] = "2026-02",
            // Reporting token pepper — fixed for test repeatability.
            ["REPORTING_TOKEN_PEPPER"] = "test-reporting-pepper-do-not-use-in-prod",
            // API access (MCP) token pepper — distinct from the reporting one, as in production.
            ["API_ACCESS_TOKEN_PEPPER"] = "test-api-access-pepper-do-not-use-in-prod",
            // Rate limiting disabled in tests to avoid spurious 429s.
            [ConfigKeys.DisableRateLimiting] = "true",
        });

        // ── Production wiring ─────────────────────────────────────────────────
        builder.Services.AddFoundationServices(builder.Configuration);
        builder.Services.AddFoundationRateLimiting();

        // ── Test auth — AFTER the extension so the defaults land on the test scheme ─
        // AddOrkyoAuthentication has already registered the JWT / policy / reporting /
        // API-token schemes; this only adds the test handler and makes it the default.
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = TestConstants.AuthScheme;
            options.DefaultChallengeScheme = TestConstants.AuthScheme;
            options.DefaultScheme = TestConstants.AuthScheme;
        })
        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestConstants.AuthScheme, _ => { });

        // ── Edition-owned registrations (what a product's Program.cs supplies) ─
        var orgId = new Guid("00000000-0000-0000-0000-000000000001");
        var tenantId = new Guid("00000000-0000-0000-0000-000000000001");

        builder.Services.AddSingleton(new DeploymentConfig
        {
            PublicUrl = "http://localhost:5000",
            AuthPublicUrl = "http://localhost:8080",
            AppBaseUrl = "http://localhost:5173",
            CorsAllowedOrigins = "http://localhost:5173",
            SmtpHost = "localhost",
            SmtpPort = 1025,
            SmtpUseSsl = false,
            SmtpFromEmail = "test@test.local",
            SmtpFromName = "Test",
            OidcAuthority = "http://localhost:8080/realms/orkyo",
            KeycloakUrl = "http://localhost:8080",
            KeycloakRealm = "orkyo",
            KeycloakBackendClientId = "test-backend",
            KeycloakBackendClientSecret = "test-secret",
            PostgresConnectionString = controlPlaneCs,
            MasterEncryptionKey = TestConstants.MasterEncryptionKey,
        });

        builder.Services.AddScoped(_ => new TenantContext
        {
            TenantId = tenantId,
            TenantSlug = TestConstants.TenantSlug,
            TenantDbConnectionString = tenantCs,
            Status = "active",
        });
        builder.Services.AddScoped(_ => new OrgContext
        {
            OrgId = orgId,
            OrgSlug = TestConstants.TenantSlug,
            DbConnectionString = tenantCs,
        });

        var dbFactory = new TestDbConnectionFactory(controlPlaneCs, controlPlaneCs);
        builder.Services.AddSingleton<IDbConnectionFactory>(dbFactory);
        builder.Services.AddSingleton<IOrgDbConnectionFactory>(dbFactory);

        // The platform audit writer resolves the target tenant's DB via ITenantResolver. Tests run a
        // single tenant, so a stub that returns the ambient TenantContext for any slug is sufficient.
        builder.Services.AddScoped<ITenantResolver>(sp => new TestTenantResolver(sp.GetRequiredService<TenantContext>()));
        builder.Services.AddScoped<Api.Security.Quotas.IQuotaEnforcer, Api.Security.Quotas.NoOpQuotaEnforcer>();
        builder.Services.AddScoped<IAdminAuditService, AdminAuditService>();
        // Break-glass is a multi-tenant concept; the single-tenant null object is what
        // Community registers, and it is exactly the no-session behaviour these tests expect.
        builder.Services.AddSingleton<IBreakGlassSessionStore, NullBreakGlassSessionStore>();
        builder.Services.AddScoped<UserLifecycleService>();

        // Editions set this in their own composition root, so the test host keeps the
        // permissive default. The options object is a mutable singleton, resolved per
        // scope, so a test can flip AllowSelfRegistration to reach the invitation-only
        // branch of /api/auth/create-account and restore it afterwards.
        builder.Services.AddSingleton<IdentityProvisioningOptions>();
        builder.Services.AddScoped<IOptions<IdentityProvisioningOptions>>(
            sp => Microsoft.Extensions.Options.Options.Create(
                sp.GetRequiredService<IdentityProvisioningOptions>()));

        // ── Test doubles for external systems (replace the production registration) ─
        builder.Services.RemoveAll<IKeycloakAdminService>();
        builder.Services.AddSingleton<IKeycloakAdminService>(mockKeycloak);

        builder.Services.RemoveAll<IEmailService>();
        builder.Services.AddSingleton<IEmailService>(mockEmail);

        // Programmable, not Mock.Of: with every call returning default, the revoke and resend
        // SUCCESS paths were unreachable and only their not-found halves could be tested.
        // Tests resolve this Mock from the factory, Setup the call, and Reset afterwards.
        var invitations = new Mock<IInvitationService>();
        builder.Services.AddSingleton(invitations);
        builder.Services.RemoveAll<IInvitationService>();
        builder.Services.AddScoped<IInvitationService>(sp => invitations.Object);

        // Singleton, not Mock.Of: tests drive the /api/session/bootstrap branches by
        // setting LinkResult on the instance they resolve from the factory.
        builder.Services.AddSingleton<StubIdentityLinkService>();
        builder.Services.RemoveAll<IIdentityLinkService>();
        builder.Services.AddSingleton<IIdentityLinkService>(
            sp => sp.GetRequiredService<StubIdentityLinkService>());

        // Stubbed, not real: the Anthropic gateway makes an outbound HTTPS call.
        builder.Services.AddSingleton<StubAnthropicGateway>();
        builder.Services.RemoveAll<Api.Services.Ai.IAnthropicGateway>();
        builder.Services.AddSingleton<Api.Services.Ai.IAnthropicGateway>(
            sp => sp.GetRequiredService<StubAnthropicGateway>());

        // Same all-enabled behaviour as AllFeaturesEnabledGate, but a test can disable one
        // key to reach an entitlement refusal that only SaaS would otherwise produce.
        builder.Services.AddSingleton<StubFeatureGate>();
        builder.Services.RemoveAll<Api.Security.Features.IFeatureGate>();
        builder.Services.AddSingleton<Api.Security.Features.IFeatureGate>(
            sp => sp.GetRequiredService<StubFeatureGate>());

        // Toggleable guard (singleton) so tests can flip the account-locked paths (session privacy) on;
        // defaults to unlocked, matching the foundation allow-all behavior so other tests are unaffected.
        builder.Services.RemoveAll<IAccountMutationGuard>();
        builder.Services.AddSingleton<IAccountMutationGuard>(accountGuard);

        // NB: integration tests bind IInsightsService directly to the real service (no caching
        // decorator) so seed→assert stays deterministic — a process-wide 60s cache keyed on the
        // shared test OrgId would leak data between tests. The decorator is covered by its own unit
        // test (CachingInsightsServiceTests); production wiring lives in FoundationServiceExtensions.
        builder.Services.RemoveAll<Api.Services.Insights.IInsightsService>();
        builder.Services.AddScoped<Api.Services.Insights.IInsightsService, Api.Services.Insights.InsightsService>();
        // Uncached here too, for the same reason: an integration test asserting on freshly written
        // data must not be answered from a 60-second-old timeline.
        builder.Services.RemoveAll<Api.Services.Insights.IConflictTimelineProvider>();
        builder.Services.AddScoped<
            Api.Services.Insights.IConflictTimelineProvider,
            Api.Services.Insights.ConflictTimelineProvider>();

        // Every scope in this host is the one test tenant, HTTP or not — the same contract
        // the fixed OrgContext above has always given the services under test. The real
        // HttpContext-backed accessor is covered by OrgContextServiceExtensionsTests.
        builder.Services.RemoveAll<IOrgContextAccessor>();
        builder.Services.AddScoped<IOrgContextAccessor>(sp => new FixedOrgContextAccessor(sp.GetRequiredService<OrgContext>()));

        var app = builder.Build();

        // Run routing before our test middleware so endpoint metadata
        // (e.g. SkipTenantResolutionAttribute) is available.
        app.UseExceptionHandler();
        app.UseRouting();
        app.UseAuthentication();
        if (!builder.Configuration.GetValue<bool>(ConfigKeys.DisableRateLimiting))
            app.UseRateLimiter();

        // Test context enrichment: populate security context for authenticated requests.
        // Mirrors saas tenant resolution: endpoints without [SkipTenantResolution] require
        // an X-Tenant-Slug header (or fall back to the shared test tenant); endpoints
        // marked skip-tenant are always allowed through with the default test context.
        //
        // Reporting tokens (orkyo_rpt_*) are authenticated by ReportingTokenAuthHandler
        // during UseAuthorization(), NOT UseAuthentication(). That means context.User is
        // not yet authenticated when this middleware runs. Pre-populate the TenantContext
        // for those requests so the endpoint filter's HasTenant check passes.
        app.Use(async (context, next) =>
        {
            var isReportingToken = context.Request.Headers.Authorization
                .FirstOrDefault()?.StartsWith("Bearer orkyo_rpt_") == true;
            if (isReportingToken && context.Request.Headers.ContainsKey(HeaderConstants.TenantSlug))
            {
                var tenantCtx = context.RequestServices.GetRequiredService<TenantContext>();
                var tenant = context.RequestServices.GetRequiredService<CurrentTenant>();
                tenant.SetContext(tenantCtx);
                context.Items["TenantContext"] = tenantCtx;
                context.Items["OrgContext"] = context.RequestServices.GetRequiredService<OrgContext>();
                await next(context);
                return;
            }

            if (context.User.Identity?.IsAuthenticated == true)
            {
                var userId = new Guid("11111111-1111-1111-1111-111111111111");

                // Derive IsSiteAdmin from the token's realm_access claim so tests that
                // create site-admin tokens (with RealmRoles=["site-admin"]) are authorized
                // (AuditEndpointsTests). The production derivation is covered by
                // ContextEnrichmentMiddlewareTests.
                var isSiteAdmin = false;
                var realmAccessClaim = context.User.FindFirst("realm_access")?.Value;
                if (realmAccessClaim != null)
                {
                    try
                    {
                        var doc = System.Text.Json.JsonDocument.Parse(realmAccessClaim);
                        if (doc.RootElement.TryGetProperty("roles", out var rolesEl))
                            isSiteAdmin = rolesEl.EnumerateArray().Any(r => r.GetString() == "site-admin");
                    }
                    catch { /* malformed claim — leave isSiteAdmin=false */ }
                }

                // Also honour an explicit UserId claim when a test token embeds one
                // (site-admin tests create distinct users, not the shared test user).
                var userIdClaim = context.User.FindFirst("user_id")?.Value;
                if (userIdClaim != null && Guid.TryParse(userIdClaim, out var claimedUserId))
                    userId = claimedUserId;

                var principal = context.RequestServices.GetRequiredService<CurrentPrincipal>();
                principal.SetContext(new PrincipalContext
                {
                    UserId = userId,
                    Email = "test@orkyo.example",
                    DisplayName = "Test User",
                    AuthProvider = AuthProvider.Keycloak,
                    IsSiteAdmin = isSiteAdmin,
                    ExternalSubject = userId.ToString(),
                });

                // Tenant gate: endpoints without [SkipTenantResolution] require an
                // X-Tenant-Slug header (or accept the absence and use the default test
                // tenant). Saas's TenantMiddleware returns 404 when neither subdomain
                // nor header resolves to a tenant; mirror that here so contract tests
                // for "no tenant header" can run.
                var endpoint = context.GetEndpoint();
                var skipTenant = endpoint?.Metadata.GetMetadata<SkipTenantResolutionAttribute>() != null;
                var hasSlugHeader = context.Request.Headers.ContainsKey(HeaderConstants.TenantSlug);

                if (!skipTenant && !hasSlugHeader)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await context.Response.WriteAsJsonAsync(new { error = "Tenant not found" });
                    return;
                }

                var tenantCtx = context.RequestServices.GetRequiredService<TenantContext>();
                var tenant = context.RequestServices.GetRequiredService<CurrentTenant>();
                tenant.SetContext(tenantCtx);

                // Populate HttpContext.Items so endpoints using GetTenantContext() / GetOrgContext()
                // extension methods find the context they need.
                context.Items["TenantContext"] = tenantCtx;
                context.Items["OrgContext"] = context.RequestServices.GetRequiredService<OrgContext>();

                // Honour the role claim from the test token so tests can exercise
                // role-based authorisation (admin vs editor vs viewer). Defaults to
                // Admin when no role is supplied — matches the legacy hard-coded behaviour.
                var roleClaim = context.User.FindFirst("role")?.Value;
                var role = string.IsNullOrEmpty(roleClaim)
                    ? TenantRole.Admin
                    : Api.Constants.RoleConstants.ParseRoleString(roleClaim) is var parsed && parsed != TenantRole.None
                        ? parsed
                        : TenantRole.Admin;

                var authCtx = context.RequestServices.GetRequiredService<CurrentAuthorizationContext>();
                authCtx.SetContext(new AuthorizationContext
                {
                    TenantId = new Guid("00000000-0000-0000-0000-000000000001"),
                    TenantSlug = TestConstants.TenantSlug,
                    Role = role,
                });
            }
            await next(context);
        });

        app.UseAuthorization();

        // API access tokens (orkyo_api_*) authenticate during UseAuthorization, like reporting
        // tokens, so this runs after it — the same position ContextEnrichmentMiddleware occupies
        // in the real pipeline. It mirrors that middleware's API-token branch: scopes become a
        // tenant role, and a token presented against another tenant gets none. The production
        // branch itself is covered directly by ContextEnrichmentApiTokenTests; this exists so the
        // endpoint wiring and per-tool scope checks can be exercised over real HTTP.
        app.Use(async (context, next) =>
        {
            if (context.Items[ApiAccessTokenContextKeys.TokenRecord] is ApiAccessTokenRecord token)
            {
                var tenantCtx = context.RequestServices.GetRequiredService<TenantContext>();
                context.RequestServices.GetRequiredService<CurrentTenant>().SetContext(tenantCtx);
                context.Items["TenantContext"] = tenantCtx;
                context.Items["OrgContext"] = context.RequestServices.GetRequiredService<OrgContext>();

                context.RequestServices.GetRequiredService<CurrentPrincipal>().SetContext(new PrincipalContext
                {
                    UserId = token.Id,
                    Email = $"token-{token.TokenPrefix}@api-tokens.invalid",
                    DisplayName = $"API token: {token.Name}",
                    AuthProvider = AuthProvider.ApiToken,
                    IsSiteAdmin = false,
                });
                context.RequestServices.GetRequiredService<CurrentAuthorizationContext>().SetContext(
                    new AuthorizationContext
                    {
                        TenantId = tenantCtx.TenantId,
                        TenantSlug = tenantCtx.TenantSlug,
                        Role = token.TenantId == tenantCtx.TenantId
                            ? token.EffectiveRole
                            : TenantRole.None,
                    });
            }

            await next(context);
        });

        // Map every foundation endpoint through the SAME extension production uses, so the
        // test surface can never silently drift from FoundationEndpointExtensions (F1).
        app.MapFoundationEndpoints();

        return app;
    }

    // ── Test tenant resolver (single test tenant) ─────────────────────────────
    private sealed class TestTenantResolver : ITenantResolver
    {
        private readonly TenantContext _tenant;
        public TestTenantResolver(TenantContext tenant) => _tenant = tenant;

        public Task<TenantContext?> ResolveTenantAsync(string? subdomain, string? tenantHeader, CancellationToken ct = default)
            => Task.FromResult<TenantContext?>(_tenant);

        public void InvalidateCache(string slug) { }
    }
}

/// <summary>Always the one test tenant; see the registration in the factory.</summary>
internal sealed class FixedOrgContextAccessor(OrgContext orgContext) : IOrgContextAccessor
{
    public OrgContext? Current => orgContext;
}
