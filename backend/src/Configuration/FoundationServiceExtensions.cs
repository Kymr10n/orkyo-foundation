using Api.Helpers;
using Api.Integrations.Keycloak;
using Api.Repositories;
using Api.Security;
using Api.Security.Encryption;
using Api.Security.Features;
using Api.Security.Quotas;
using Api.Services;
using Api.Services.AutoSchedule;
using Api.Services.Reporting;
using Api.Validators;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
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

        // ── Commercial-neutrality defaults ────────────────────────────────────
        // Foundation is edition-agnostic: it gates features and quotas through these
        // abstractions. Defaults allow everything; products register overrides AFTER
        // AddFoundationServices (last registration wins). SaaS swaps in tier-aware
        // implementations; Community keeps these allow-all defaults.
        services.AddScoped<IFeatureGate, AllFeaturesEnabledGate>();
        services.AddScoped<ITenantPlanInfoProvider, SinglePlanInfoProvider>();
        services.AddScoped<IQuotaUsageRollup, NoOpQuotaUsageRollup>();

        // ── Encryption (at-rest field/blob protection) ───────────────────────
        // Singleton, stateless. Reuses DeploymentConfig's validated master key so the
        // base64/length check lives in one place; resolved lazily on first use.
        services.AddSingleton<IEncryptionService>(sp =>
            new AesGcmEncryptionService(
                sp.GetRequiredService<DeploymentConfig>().DecodeMasterEncryptionKey()));

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
        services.AddScoped<IConflictService, ConflictService>();
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
        // Insights is wrapped in a short-TTL read-through cache (dashboard hot path).
        services.AddScoped<Api.Services.Insights.InsightsService>();
        services.AddScoped<Api.Services.Insights.IInsightsService>(sp =>
            new Api.Services.Insights.CachingInsightsService(
                sp.GetRequiredService<Api.Services.Insights.InsightsService>(),
                sp.GetRequiredService<OrgContext>()));

        // ── Reporting ─────────────────────────────────────────────────────────
        services.AddScoped<IReportingTokenService, ReportingTokenService>();
        services.AddScoped<IReportingQueryService, ReportingQueryService>();

        // ── Scheduling solver (singleton — stateless and thread-safe) ─────────
        services.AddScoped<SchedulingProblemBuilder>();
        services.AddSingleton<SchedulingFeasibilityAnalyzer>();
        services.AddSingleton<ISchedulingSolver, OrToolsSchedulingSolver>();
        services.AddSingleton<ISchedulingSolver, GreedySchedulingSolver>();
        services.AddScoped<IAutoScheduleService, AutoScheduleService>();

        return services;
    }
}
