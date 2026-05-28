using Api.Helpers;
using Api.Integrations.Keycloak;
using Api.Repositories;
using Api.Security;
using Api.Services;
using Api.Services.AutoSchedule;
using Api.Services.Reporting;
using Api.Validators;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Orkyo.Shared.Keycloak;

namespace Api.Configuration;

public static class FoundationServiceExtensions
{
    /// <summary>
    /// Registers all foundation-owned services, repositories, Keycloak, auth, and
    /// core ASP.NET infrastructure. Products call this once; adding a new service
    /// or repository to foundation is immediately available in all products.
    ///
    /// Product-specific registrations (IDbConnectionFactory, IQuotaEnforcer,
    /// IAdminAuditService, IBreakGlassSessionStore, etc.) remain in each product's
    /// Program.cs.
    /// </summary>
    public static IServiceCollection AddFoundationServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        // ── Core ASP.NET infrastructure ───────────────────────────────────────
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            options.SerializerOptions.Converters.Add(
                new System.Text.Json.Serialization.JsonStringEnumConverter(allowIntegerValues: false));
        });
        services.AddExceptionHandler<AppExceptionHandler>();
        services.AddProblemDetails();
        services.AddEndpointsApiExplorer();
        services.AddHttpContextAccessor();
        services.AddHttpClient();
        services.AddValidatorsFromAssemblyContaining<CreateCriterionRequestValidator>(ServiceLifetime.Scoped);

        // ── Keycloak ──────────────────────────────────────────────────────────
        services.AddSingleton(KeycloakOptions.FromConfiguration(configuration));
        services.AddHttpClient<IKeycloakAdminService, KeycloakAdminService>();

        // ── Auth ──────────────────────────────────────────────────────────────
        services.AddOrkyoAuthentication(configuration);
        services.AddBffAuthentication(configuration);

        // ── Security context ──────────────────────────────────────────────────
        services.AddScoped<CurrentPrincipal>();
        services.AddScoped<ICurrentPrincipal>(sp => sp.GetRequiredService<CurrentPrincipal>());
        services.AddScoped<CurrentTenant>();
        services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<CurrentTenant>());
        services.AddScoped<CurrentAuthorizationContext>();
        services.AddScoped<IAuthorizationContext>(sp => sp.GetRequiredService<CurrentAuthorizationContext>());

        // ── Repositories ──────────────────────────────────────────────────────
        services.AddScoped<IAnnouncementRepository, AnnouncementRepository>();
        services.AddScoped<ICriteriaRepository, CriteriaRepository>();
        services.AddScoped<ICriterionApplicabilityRepository, CriterionApplicabilityRepository>();
        services.AddScoped<IDepartmentRepository, DepartmentRepository>();
        services.AddScoped<IFeedbackRepository, FeedbackRepository>();
        services.AddScoped<IGroupCapabilityRepository, GroupCapabilityRepository>();
        services.AddScoped<IJobTitleRepository, JobTitleRepository>();
        services.AddScoped<IPersonProfileRepository, PersonProfileRepository>();
        services.AddScoped<IRequestRepository, RequestRepository>();
        services.AddScoped<IAssetRepository, AssetRepository>();
        services.AddScoped<IResourceAssignmentRepository, ResourceAssignmentRepository>();
        services.AddScoped<IResourceCapabilityRepository, ResourceCapabilityRepository>();
        services.AddScoped<IResourceGroupMemberRepository, ResourceGroupMemberRepository>();
        services.AddScoped<IResourceGroupRepository, ResourceGroupRepository>();
        services.AddScoped<IResourceRepository, ResourceRepository>();
        services.AddScoped<IResourceTypeRepository, ResourceTypeRepository>();
        services.AddScoped<ISchedulingRepository, SchedulingRepository>();
        services.AddScoped<ISearchRepository, SearchRepository>();
        services.AddScoped<ISiteRepository, SiteRepository>();
        services.AddScoped<ISiteSettingsRepository, SiteSettingsRepository>();
        services.AddScoped<ISpaceRepository, SpaceRepository>();
        services.AddScoped<ITemplateRepository, TemplateRepository>();
        services.AddScoped<ITenantSettingsRepository, TenantSettingsRepository>();
        services.AddScoped<IUserPreferencesRepository, UserPreferencesRepository>();

        // ── Domain services ───────────────────────────────────────────────────
        services.AddScoped<IAnnouncementService, AnnouncementService>();
        services.AddScoped<ICapabilityMatcher, CapabilityMatcher>();
        services.AddScoped<ICriteriaService, CriteriaService>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<IExportService, ExportService>();
        services.AddScoped<IAssetStorageService, AssetStorageService>();
        services.AddScoped<IIdentityLinkService, KeycloakIdentityLinkService>();
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddScoped<IAvailabilityEventRepository, AvailabilityEventRepository>();
        services.AddScoped<IResourceAbsenceRepository, ResourceAbsenceRepository>();
        services.AddScoped<IAvailabilityResolver, AvailabilityResolver>();
        services.AddScoped<IPresetService, PresetService>();
        services.AddScoped<IRequestService, RequestService>();
        services.AddScoped<IResourceAssignmentService, ResourceAssignmentService>();
        services.AddScoped<IResourceAssignmentValidator, ResourceAssignmentValidator>();
        services.AddScoped<IResourceService, ResourceService>();
        services.AddScoped<ISchedulingService, SchedulingService>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<ISiteService, SiteService>();
        services.AddScoped<ISiteSettingsService, SiteSettingsService>();
        services.AddScoped<ISpaceService, SpaceService>();
        services.AddScoped<IStarterTemplateService, StarterTemplateService>();
        services.AddScoped<ITenantSettingsService, TenantSettingsService>();
        services.AddScoped<ITenantUserService, TenantUserService>();
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<IUtilizationService, UtilizationService>();

        // ── Reporting ─────────────────────────────────────────────────────────
        services.AddScoped<IReportingTokenService, ReportingTokenService>();
        services.AddScoped<IReportingQueryService, ReportingQueryService>();

        // ── OpenAPI / Swagger ─────────────────────────────────────────────────
        // Reporting-only document at /swagger/reporting-v1/swagger.json
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("reporting-v1", new OpenApiInfo
            {
                Title = "Orkyo Reporting API",
                Version = "v1",
                Description = "Read-only, tenant-scoped reporting endpoints for Power BI, Excel, Metabase, Superset, and custom integrations. " +
                              "Authenticate with an orkyo_rpt_* reporting token in the Authorization header. " +
                              "Tenant isolation is enforced server-side — no tenantId parameter accepted.",
            });

            c.AddSecurityDefinition("ReportingToken", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "orkyo_rpt_<prefix>_<secret>",
                In = ParameterLocation.Header,
                Description = "Reporting API token in the format: Bearer orkyo_rpt_<prefix>_<secret>",
            });

            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ReportingToken" }
                    },
                    Array.Empty<string>()
                }
            });

            // Include only reporting endpoints (by path prefix)
            c.DocInclusionPredicate((docName, apiDesc) =>
                docName == "reporting-v1" &&
                (apiDesc.RelativePath?.StartsWith("api/reporting", StringComparison.OrdinalIgnoreCase) ?? false));
        });

        // ── Scheduling solver (singleton — stateless and thread-safe) ─────────
        services.AddScoped<SchedulingProblemBuilder>();
        services.AddSingleton<SchedulingFeasibilityAnalyzer>();
        services.AddSingleton<ISchedulingSolver, OrToolsSchedulingSolver>();
        services.AddSingleton<ISchedulingSolver, GreedySchedulingSolver>();
        services.AddScoped<IAutoScheduleService, AutoScheduleService>();

        return services;
    }
}
