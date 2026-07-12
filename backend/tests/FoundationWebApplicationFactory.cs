using System.Text.Json.Serialization;
using Api.Configuration;
using Api.Endpoints;
using Api.Endpoints.Admin;
using Api.Endpoints.Reporting;
using Api.Integrations.Keycloak;
using Api.Middleware;
using Api.Reporting.Auth;
using Api.Repositories;
using Api.Security;
using Api.Services;
using Api.Services.AutoSchedule;
using Api.Services.BffSession;
using Api.Services.Reporting;
using Api.Validators;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orkyo.Foundation.Tests.Mocks;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests;

/// <summary>
/// Builds an in-process <see cref="WebApplication"/> with all foundation services registered
/// against a real test database. This mirrors orkyo-core's <c>ApiWebApplicationFactory</c>
/// but works without a <c>Program.cs</c> entry point.
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
    /// Creates an <see cref="HttpClient"/> with the standard test tenant slug and
    /// bearer-token authorization headers preset.
    /// </summary>
    public HttpClient CreateClient()
    {
        return _host.GetTestClient();
    }

    /// <summary>
    /// Creates an <see cref="HttpClient"/> with the given options.
    /// Supports <c>AllowAutoRedirect = false</c> for redirect-assertion tests.
    /// </summary>
    public HttpClient CreateClient(WebApplicationFactoryClientOptions options)
    {
        var testServer = _host.GetTestServer();
        if (!options.AllowAutoRedirect)
        {
            var noRedirectClient = testServer.CreateClient();
            noRedirectClient.DefaultRequestHeaders.Clear();
            // Disable redirect following by replacing the inner handler
            var client = new HttpClient(new NoRedirectDelegatingHandler(testServer.CreateHandler()))
            {
                BaseAddress = new Uri("http://localhost"),
            };
            return client;
        }
        return testServer.CreateClient();
    }

    private sealed class NoRedirectDelegatingHandler : DelegatingHandler
    {
        public NoRedirectDelegatingHandler(HttpMessageHandler inner) : base(inner) { }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            return response;
        }
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
            // BFF auth — enabled with test-friendly settings so BFF endpoints register.
            ["BFF_ENABLED"] = "true",
            ["BFF_COOKIE_SECURE"] = "false",
            ["BFF_REDIRECT_URI"] = "http://localhost:5173/api/auth/bff/callback",
            ["BFF_ALLOWED_HOSTS"] = "demo.orkyo.com,localhost:5173,orkyo.com,*.orkyo.com",
            // ToS — current required version. Tests assert against "2026-02".
            ["ToS:RequiredVersion"] = "2026-02",
            // Reporting token pepper — fixed for test repeatability.
            ["REPORTING_TOKEN_PEPPER"] = "test-reporting-pepper-do-not-use-in-prod",
            // Rate limiting disabled in tests to avoid spurious 429s.
            [ConfigKeys.DisableRateLimiting] = "true",
        });

        // ── Auth ──────────────────────────────────────────────────────────────
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = "TestScheme";
            options.DefaultChallengeScheme = "TestScheme";
            options.DefaultScheme = "TestScheme";
        })
        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("TestScheme", _ => { })
        .AddScheme<AuthenticationSchemeOptions, ReportingTokenAuthHandler>(
            ReportingTokenAuthHandler.SchemeName, _ => { });

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("ReportingToken", policy =>
            {
                policy.AddAuthenticationSchemes(ReportingTokenAuthHandler.SchemeName);
                policy.RequireAuthenticatedUser();
            });
        });

        // Rate limiting — disabled by DISABLE_RATE_LIMITING config in tests
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("reporting-api", ctx =>
                System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                    ctx.User?.Identity?.Name ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                    }));
        });

        // ── Security context (real scoped impls populated by test middleware) ─
        builder.Services.AddScoped<CurrentPrincipal>();
        builder.Services.AddScoped<ICurrentPrincipal>(sp => sp.GetRequiredService<CurrentPrincipal>());
        builder.Services.AddScoped<CurrentTenant>();
        builder.Services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<CurrentTenant>());
        builder.Services.AddScoped<CurrentAuthorizationContext>();
        builder.Services.AddScoped<IAuthorizationContext>(sp => sp.GetRequiredService<CurrentAuthorizationContext>());
        // Toggleable guard (singleton) so tests can flip the account-locked paths (session privacy) on;
        // defaults to unlocked, matching the foundation allow-all behavior so other tests are unaffected.
        builder.Services.AddSingleton<IAccountMutationGuard>(accountGuard);

        // ── DB connectivity ───────────────────────────────────────────────────
        var orgId = new Guid("00000000-0000-0000-0000-000000000001");
        var tenantId = new Guid("00000000-0000-0000-0000-000000000001");

        builder.Services.AddScoped(_ => new OrgContext
        {
            OrgId = orgId,
            OrgSlug = TestConstants.TenantSlug,
            DbConnectionString = tenantCs,
        });

        builder.Services.AddScoped(_ => new TenantContext
        {
            TenantId = tenantId,
            TenantSlug = TestConstants.TenantSlug,
            TenantDbConnectionString = tenantCs,
            Status = "active",
        });

        var dbFactory = new TestDbConnectionFactory(controlPlaneCs, tenantCs);
        builder.Services.AddSingleton<IDbConnectionFactory>(dbFactory);
        builder.Services.AddSingleton<IOrgDbConnectionFactory>(dbFactory);

        // ── Repositories ──────────────────────────────────────────────────────
        builder.Services.AddScoped<ISiteRepository, SiteRepository>();
        builder.Services.AddScoped<ISpaceRepository, SpaceRepository>();
        // ISpaceCapabilityRepository is served by IResourceCapabilityRepository (Phase 2)
        builder.Services.AddScoped<IGroupCapabilityRepository, GroupCapabilityRepository>();
        builder.Services.AddScoped<ICriteriaRepository, CriteriaRepository>();
        builder.Services.AddScoped<IRequestRepository, RequestRepository>();
        builder.Services.AddScoped<ISchedulingRepository, SchedulingRepository>();
        builder.Services.AddScoped<ITemplateRepository, TemplateRepository>();
        builder.Services.AddScoped<ISearchRepository, SearchRepository>();
        builder.Services.AddScoped<IFeedbackRepository, FeedbackRepository>();
        builder.Services.AddScoped<IAnnouncementRepository, AnnouncementRepository>();
        builder.Services.AddScoped<IPlatformUserRepository, PlatformUserRepository>();
        builder.Services.AddScoped<IUserPreferencesRepository, UserPreferencesRepository>();
        builder.Services.AddScoped<ISiteSettingsRepository, SiteSettingsRepository>();
        builder.Services.AddScoped<ITenantSettingsRepository, TenantSettingsRepository>();

        // ── Resource model (Phase 1) ──────────────────────────────────────────
        builder.Services.AddScoped<IResourceTypeRepository, ResourceTypeRepository>();
        builder.Services.AddScoped<IResourceRepository, ResourceRepository>();
        builder.Services.AddScoped<IResourceAssignmentRepository, ResourceAssignmentRepository>();
        builder.Services.AddScoped<IResourceCapabilityRepository, ResourceCapabilityRepository>();
        builder.Services.AddScoped<ICriterionApplicabilityRepository, CriterionApplicabilityRepository>();
        builder.Services.AddScoped<IResourceGroupMemberRepository, ResourceGroupMemberRepository>();
        builder.Services.AddScoped<IResourceGroupRepository, ResourceGroupRepository>();

        // ── Availability model ────────────────────────────────────────────────
        builder.Services.AddScoped<IAvailabilityEventRepository, AvailabilityEventRepository>();
        builder.Services.AddScoped<IResourceAbsenceRepository, ResourceAbsenceRepository>();
        builder.Services.AddScoped<IAvailabilityResolver, AvailabilityResolver>();

        // ── Security + quota ─────────────────────────────────────────────────
        builder.Services.AddScoped<Api.Security.Quotas.IQuotaEnforcer, Api.Security.Quotas.NoOpQuotaEnforcer>();
        builder.Services.AddScoped<Api.Security.Quotas.IQuotaUsageRollup, Api.Security.Quotas.NoOpQuotaUsageRollup>();
        builder.Services.AddScoped<Api.Security.Features.IFeatureGate, Api.Security.Features.AllFeaturesEnabledGate>();
        builder.Services.AddScoped<Api.Security.Features.ITenantPlanInfoProvider, Api.Security.Features.SinglePlanInfoProvider>();

        // ── HTTP client factory (required by UserLifecycleService) ────────────
        builder.Services.AddHttpClient();

        // ── Keycloak options (test stub — not used for real Keycloak calls) ───
        builder.Services.AddSingleton(new Orkyo.Shared.Keycloak.KeycloakOptions
        {
            BaseUrl = "http://localhost:8080",
            Realm = "orkyo",
            BackendClientId = "test-backend",
            BackendClientSecret = "test-secret",
        });

        // ── DeploymentConfig (test stub — required by diagnostic endpoints) ──
        builder.Services.AddSingleton(new Api.Configuration.DeploymentConfig
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

        // ── Encryption (mirrors FoundationServiceExtensions registration) ──
        builder.Services.AddSingleton<Api.Security.Encryption.IEncryptionService>(sp =>
            new Api.Security.Encryption.AesGcmEncryptionService(
                sp.GetRequiredService<Api.Configuration.DeploymentConfig>().DecodeMasterEncryptionKey()));

        // ── Services ──────────────────────────────────────────────────────────
        builder.Services.AddScoped<Api.Services.AutoSchedule.SchedulingProblemBuilder>();
        builder.Services.AddScoped<Api.Services.AutoSchedule.SchedulingFeasibilityAnalyzer>();
        builder.Services.AddScoped<Api.Services.AutoSchedule.ISchedulingSolver, Api.Services.AutoSchedule.GreedySchedulingSolver>();
        builder.Services.AddScoped<IAssetRepository, AssetRepository>();
        builder.Services.AddScoped<ISiteService, SiteService>();
        builder.Services.AddScoped<ISpaceService, SpaceService>();
        // SpaceService now needs resource repos for Phase 2 coordination (already registered above)
        builder.Services.AddScoped<ICriteriaService, CriteriaService>();
        builder.Services.AddScoped<IRequestService, RequestService>();
        builder.Services.AddScoped<ISchedulingService, SchedulingService>();
        builder.Services.AddScoped<IAutoScheduleService, AutoScheduleService>();
        builder.Services.AddScoped<IExportService, ExportService>();
        builder.Services.AddScoped<IPresetService, PresetService>();
        builder.Services.AddScoped<IStarterTemplateService, StarterTemplateService>();
        builder.Services.AddScoped<ICapabilityMatcher, CapabilityMatcher>();
        builder.Services.AddScoped<IResourceService, ResourceService>();
        builder.Services.AddScoped<IResourceAssignmentValidator, ResourceAssignmentValidator>();
        builder.Services.AddScoped<IConflictService, ConflictService>();
        builder.Services.AddScoped<IPersonProfileRepository, PersonProfileRepository>();
        builder.Services.AddScoped<IJobTitleRepository, JobTitleRepository>();
        builder.Services.AddScoped<IDepartmentRepository, DepartmentRepository>();
        builder.Services.AddScoped<IResourceAssignmentService, ResourceAssignmentService>();
        builder.Services.AddScoped<IUtilizationService, UtilizationService>();
        // NB: integration tests bind IInsightsService directly to the real service (no caching
        // decorator) so seed→assert stays deterministic — a process-wide 60s cache keyed on the
        // shared test OrgId would leak data between tests. The decorator is covered by its own unit
        // test (CachingInsightsServiceTests); production wiring lives in FoundationServiceExtensions.
        builder.Services.AddScoped<Api.Services.Insights.IInsightsService, Api.Services.Insights.InsightsService>();
        builder.Services.AddScoped<IAnnouncementService, AnnouncementService>();
        builder.Services.AddScoped<ISessionService, SessionService>();
        builder.Services.AddScoped<IUserSessionService, UserSessionService>();
        builder.Services.AddSingleton<Api.Security.IClientIpAccessor, Api.Security.ClientIpAccessor>();
        builder.Services.AddScoped<Api.Security.IBffSessionEstablisher, Api.Security.BffSessionEstablisher>();
        builder.Services.AddScoped<ISiteSettingsService, SiteSettingsService>();
        builder.Services.AddScoped<ITenantSettingsService, TenantSettingsService>();
        builder.Services.AddScoped<IStarterTemplateService, StarterTemplateService>();
        builder.Services.AddScoped<ITenantUserService, TenantUserService>();
        builder.Services.AddScoped<IPlatformTenantAuditWriter, PlatformTenantAuditWriter>();
        builder.Services.AddScoped<ISignInAuditRecorder, SignInAuditRecorder>();
        // The platform audit writer resolves the target tenant's DB via ITenantResolver. Tests run a
        // single tenant, so a stub that returns the ambient TenantContext for any slug is sufficient.
        builder.Services.AddScoped<ITenantResolver>(sp => new TestTenantResolver(sp.GetRequiredService<TenantContext>()));
        builder.Services.AddScoped<IUserManagementService, UserManagementService>();
        builder.Services.AddScoped<IAssetStorageService, AssetStorageService>();
        builder.Services.AddScoped<UserLifecycleService>();

        // ── Reporting services ────────────────────────────────────────────────
        builder.Services.AddScoped<IReportingTokenService, ReportingTokenService>();
        builder.Services.AddScoped<IReportingQueryService, ReportingQueryService>();

        // BFF auth — wires BffOptions binding, in-memory PKCE/session stores, DataProtection,
        // TenantMiddlewareOptions, and (when BFF_ENABLED=true) the cookie auth scheme.
        builder.Services.AddBffAuthentication(builder.Configuration);

        // Services backed by external systems → mock
        builder.Services.AddSingleton<IKeycloakAdminService>(mockKeycloak);
        builder.Services.AddSingleton<IEmailService>(mockEmail);
        builder.Services.AddScoped<IInvitationService>(sp => Mock.Of<IInvitationService>());
        builder.Services.AddScoped<IAdminAuditService, AdminAuditService>();
        builder.Services.AddScoped<IBreakGlassSessionStore>(sp => Mock.Of<IBreakGlassSessionStore>());
        builder.Services.AddScoped<IIdentityLinkService>(sp => Mock.Of<IIdentityLinkService>());

        // Validators — register all from the foundation assembly
        var foundationAssembly = typeof(SiteRequestValidator<>).Assembly;
        foreach (var type in foundationAssembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition &&
                        t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>))))
        {
            foreach (var iface in type.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>)))
            {
                builder.Services.AddScoped(iface, type);
            }
        }

        // ── JSON options ──────────────────────────────────────────────────────
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNameCaseInsensitive = true;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        });

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddLogging();
        builder.Services.AddExceptionHandler<Api.Helpers.AppExceptionHandler>();
        builder.Services.AddProblemDetails();

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
                // create site-admin tokens (with RealmRoles=["site-admin"]) are authorized.
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

        // ── Map all foundation endpoints ──────────────────────────────────────
        app.MapSiteEndpoints();
        app.MapSpaceEndpoints();
        app.MapResourceGroupEndpoints();
        app.MapGroupCapabilityEndpoints();
        app.MapInsightsEndpoints();
        app.MapCriteriaEndpoints();
        app.MapRequestEndpoints();
        app.MapConflictsEndpoints();
        app.MapSchedulingEndpoints();
        app.MapAvailabilityEventEndpoints();
        app.MapAutoScheduleEndpoints();
        app.MapExportEndpoints();
        app.MapPresetEndpoints();
        app.MapTemplateEndpoints();
        app.MapAnnouncementEndpoints();
        app.MapUserAnnouncementEndpoints();
        app.MapTenantAuditEndpoints();
        app.MapSessionEndpoints();
        app.MapSearchEndpoints();
        app.MapFeedbackEndpoints();
        app.MapSecurityEndpoints();
        app.MapSettingsEndpoints();
        app.MapUserManagementEndpoints();
        app.MapUserPreferencesEndpoints();
        app.MapContactEndpoints();
        app.MapAccountLifecycleEndpoints();
        app.MapAccountEmailChangeEndpoints();
        app.MapBffAuthEndpoints();
        app.MapResourceTypeEndpoints();
        app.MapResourceEndpoints();
        app.MapResourceAssignmentEndpoints();
        app.MapCriterionApplicabilityEndpoints();
        app.MapResourceGroupMemberEndpoints();
        app.MapUtilizationEndpoints();
        app.MapPersonProfileEndpoints();
        app.MapJobTitleEndpoints();
        app.MapDepartmentEndpoints();
        // Reporting endpoints
        app.MapReportingEndpoints();
        app.MapReportingTokenEndpoints();

        // Admin endpoints
        app.MapFloorplanEndpoints();
        app.MapUserAdminEndpoints();
        app.MapSettingsAdminEndpoints();
        app.MapConfigurationAdminEndpoints();
        app.MapDiagnosticsAdminEndpoints();
        app.MapFeedbackAdminEndpoints();
        app.MapAuditEndpoints();

        return app;
    }

    // ── Test DB connection factory ────────────────────────────────────────────

    private sealed class TestDbConnectionFactory : IDbConnectionFactory
    {
        private readonly string _controlPlaneCs;
        private readonly string _tenantCs;

        public TestDbConnectionFactory(string controlPlaneCs, string tenantCs)
        {
            _controlPlaneCs = controlPlaneCs;
            _tenantCs = tenantCs;
        }

        public NpgsqlConnection CreateControlPlaneConnection() => new(_controlPlaneCs);
        public NpgsqlConnection CreateTenantConnection(TenantContext tenant) => new(tenant.TenantDbConnectionString);
        public NpgsqlConnection CreateConnectionForDatabase(string dbIdentifier) => new(_tenantCs);
        public NpgsqlConnection CreateOrgConnection(OrgContext org) => new(org.DbConnectionString);
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
