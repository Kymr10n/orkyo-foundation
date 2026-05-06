using System.Text.Json.Serialization;
using Api.Endpoints;
using Api.Endpoints.Admin;
using Api.Integrations.Keycloak;
using Api.Services.BffSession;
using Api.Middleware;
using Api.Repositories;
using Api.Security;
using Api.Services;
using Api.Services.AutoSchedule;
using Api.Validators;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orkyo.Foundation.Tests.Mocks;

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

    /// <summary>Shared Keycloak mock — tests can inspect calls and configure responses.</summary>
    public MockKeycloakAdminService MockKeycloakAdminService { get; } = new();

    /// <summary>Exposes the application's service provider for advanced test scenarios.</summary>
    public IServiceProvider Services => ((WebApplication)_host).Services;

    private FoundationWebApplicationFactory(IHost host) => _host = host;

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
        var client = _host.GetTestClient();
        if (!options.AllowAutoRedirect)
        {
            // GetTestClient() follows redirects by default; wrap with a non-redirecting handler
            var handler = new HttpClientHandler { AllowAutoRedirect = false };
            return new HttpClient(handler)
            {
                BaseAddress = client.BaseAddress,
            };
        }
        return client;
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
        var factory = new FoundationWebApplicationFactory(null!); // built below
        var app = BuildWebApplication(tenantConnectionString, controlPlaneConnectionString, factory.MockKeycloakAdminService);
        await app.StartAsync();

        // Rebuild with the real host
        return new FoundationWebApplicationFactory(app);
    }

    // ── App bootstrap ─────────────────────────────────────────────────────────

    private static WebApplication BuildWebApplication(
        string tenantCs,
        string controlPlaneCs,
        MockKeycloakAdminService mockKeycloak)
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
            ["FILE_STORAGE_PATH"] = Path.Combine(Path.GetTempPath(), "orkyo-test-storage"),
            ["APP_BASE_URL"] = "http://localhost:5173",
            ["SMTP_HOST"] = "localhost",
            ["SMTP_PORT"] = "1025",
            ["SMTP_USE_SSL"] = "false",
            ["SMTP_FROM_EMAIL"] = "test@test.local",
            ["SMTP_FROM_NAME"] = "Test",
        });

        // ── Auth ──────────────────────────────────────────────────────────────
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = "TestScheme";
            options.DefaultChallengeScheme = "TestScheme";
            options.DefaultScheme = "TestScheme";
        }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("TestScheme", _ => { });

        builder.Services.AddAuthorization();

        // ── Security context (real scoped impls populated by test middleware) ─
        builder.Services.AddScoped<CurrentPrincipal>();
        builder.Services.AddScoped<ICurrentPrincipal>(sp => sp.GetRequiredService<CurrentPrincipal>());
        builder.Services.AddScoped<CurrentTenant>();
        builder.Services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<CurrentTenant>());
        builder.Services.AddScoped<CurrentAuthorizationContext>();
        builder.Services.AddScoped<IAuthorizationContext>(sp => sp.GetRequiredService<CurrentAuthorizationContext>());

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
            Tier = Api.Models.ServiceTier.Enterprise,
            Status = "active",
        });

        var dbFactory = new TestDbConnectionFactory(controlPlaneCs, tenantCs);
        builder.Services.AddSingleton<IDbConnectionFactory>(dbFactory);
        builder.Services.AddSingleton<IOrgDbConnectionFactory>(dbFactory);

        // ── Repositories ──────────────────────────────────────────────────────
        builder.Services.AddScoped<ISiteRepository, SiteRepository>();
        builder.Services.AddScoped<ISpaceRepository, SpaceRepository>();
        builder.Services.AddScoped<ISpaceGroupRepository, SpaceGroupRepository>();
        builder.Services.AddScoped<ISpaceCapabilityRepository, SpaceCapabilityRepository>();
        builder.Services.AddScoped<IGroupCapabilityRepository, GroupCapabilityRepository>();
        builder.Services.AddScoped<ICriteriaRepository, CriteriaRepository>();
        builder.Services.AddScoped<IRequestRepository, RequestRepository>();
        builder.Services.AddScoped<ISchedulingRepository, SchedulingRepository>();
        builder.Services.AddScoped<ITemplateRepository, TemplateRepository>();
        builder.Services.AddScoped<ISearchRepository, SearchRepository>();
        builder.Services.AddScoped<IFeedbackRepository, FeedbackRepository>();
        builder.Services.AddScoped<IAnnouncementRepository, AnnouncementRepository>();
        builder.Services.AddScoped<IUserPreferencesRepository, UserPreferencesRepository>();
        builder.Services.AddScoped<ISiteSettingsRepository, SiteSettingsRepository>();
        builder.Services.AddScoped<ITenantSettingsRepository, TenantSettingsRepository>();

        // ── Services ──────────────────────────────────────────────────────────
        builder.Services.AddScoped<ISiteService, SiteService>();
        builder.Services.AddScoped<ISpaceService, SpaceService>();
        builder.Services.AddScoped<ICriteriaService, CriteriaService>();
        builder.Services.AddScoped<IRequestService, RequestService>();
        builder.Services.AddScoped<ISchedulingService, SchedulingService>();
        builder.Services.AddScoped<IAutoScheduleService, AutoScheduleService>();
        builder.Services.AddScoped<IExportService, ExportService>();
        builder.Services.AddScoped<IPresetService, PresetService>();
        builder.Services.AddScoped<IStarterTemplateService, StarterTemplateService>();
        builder.Services.AddScoped<IAnnouncementService, AnnouncementService>();
        builder.Services.AddScoped<ISessionService, SessionService>();
        builder.Services.AddScoped<ISiteSettingsService, SiteSettingsService>();
        builder.Services.AddScoped<ITenantSettingsService, TenantSettingsService>();
        builder.Services.AddScoped<IStarterTemplateService, StarterTemplateService>();
        builder.Services.AddScoped<ITenantUserService, TenantUserService>();
        builder.Services.AddScoped<IUserManagementService, UserManagementService>();
        builder.Services.AddScoped<UserLifecycleService>();

        // BFF session services (in-memory for tests)
        builder.Services.AddSingleton<IBffPkceStateStore, InMemoryBffPkceStateStore>();
        builder.Services.AddSingleton<IBffSessionStore, InMemoryBffSessionStore>();

        // Services backed by external systems → mock
        builder.Services.AddSingleton<IKeycloakAdminService>(mockKeycloak);
        builder.Services.AddScoped<IEmailService>(sp => Mock.Of<IEmailService>());
        builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();
        builder.Services.AddScoped<IInvitationService>(sp => Mock.Of<IInvitationService>());
        builder.Services.AddScoped<IAdminAuditService>(sp => Mock.Of<IAdminAuditService>());
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

        var app = builder.Build();

        app.UseAuthentication();

        // Test context enrichment: populate security context for authenticated requests
        app.Use(async (context, next) =>
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var userId = new Guid("11111111-1111-1111-1111-111111111111");

                var principal = context.RequestServices.GetRequiredService<CurrentPrincipal>();
                principal.SetContext(new PrincipalContext
                {
                    UserId = userId,
                    Email = "test@orkyo.example",
                    DisplayName = "Test User",
                    AuthProvider = AuthProvider.Keycloak,
                    IsSiteAdmin = false,
                    ExternalSubject = userId.ToString(),
                });

                var tenant = context.RequestServices.GetRequiredService<CurrentTenant>();
                tenant.SetContext(context.RequestServices.GetRequiredService<TenantContext>());

                var authCtx = context.RequestServices.GetRequiredService<CurrentAuthorizationContext>();
                authCtx.SetContext(new AuthorizationContext
                {
                    TenantId = new Guid("00000000-0000-0000-0000-000000000001"),
                    TenantSlug = TestConstants.TenantSlug,
                    Role = TenantRole.Admin,
                });
            }
            await next(context);
        });

        app.UseAuthorization();

        // ── Map all foundation endpoints ──────────────────────────────────────
        app.MapSiteEndpoints();
        app.MapSpaceEndpoints();
        app.MapSpaceGroupEndpoints();
        app.MapSpaceCapabilityEndpoints();
        app.MapGroupCapabilityEndpoints();
        app.MapCriteriaEndpoints();
        app.MapRequestEndpoints();
        app.MapSchedulingEndpoints();
        app.MapAutoScheduleEndpoints();
        app.MapExportEndpoints();
        app.MapPresetEndpoints();
        app.MapTemplateEndpoints();
        app.MapAnnouncementEndpoints();
        app.MapUserAnnouncementEndpoints();
        app.MapSessionEndpoints();
        app.MapSearchEndpoints();
        app.MapFeedbackEndpoints();
        app.MapSecurityEndpoints();
        app.MapSettingsEndpoints();
        app.MapUserManagementEndpoints();
        app.MapUserPreferencesEndpoints();
        app.MapContactEndpoints();
        app.MapAccountLifecycleEndpoints();
        app.MapBffAuthEndpoints();
        // Admin endpoints
        app.MapFloorplanEndpoints();
        app.MapTemplateEndpoints();
        // Admin endpoints
        app.MapUserAdminEndpoints();
        app.MapSettingsAdminEndpoints();
        app.MapDiagnosticsAdminEndpoints();
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
}
