using Api.Helpers;
using Api.Integrations.Reporting;
using Api.Security;
using Api.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Api.Reporting;

public sealed class ReportEmbedService : IReportEmbedService
{
    private readonly IAuthorizationContext _authCtx;
    private readonly ICurrentTenant _tenant;
    private readonly ICurrentPrincipal _principal;
    private readonly IReportBindingRepository _bindings;
    private readonly IReportingEngineClient _engine;
    private readonly IAdminAuditService _audit;
    private readonly ReportingOptions _opts;
    private readonly ILogger<ReportEmbedService> _logger;

    public ReportEmbedService(
        IAuthorizationContext authCtx,
        ICurrentTenant tenant,
        ICurrentPrincipal principal,
        IReportBindingRepository bindings,
        IReportingEngineClient engine,
        IAdminAuditService audit,
        IOptions<ReportingOptions> opts,
        ILogger<ReportEmbedService> logger)
    {
        _authCtx = authCtx;
        _tenant = tenant;
        _principal = principal;
        _bindings = bindings;
        _engine = engine;
        _audit = audit;
        _opts = opts.Value;
        _logger = logger;
    }

    public async Task<ReportEmbedTokenResult> CreateEmbedTokenAsync(
        string reportKey, CancellationToken ct = default)
    {
        if (!_opts.Enabled)
            throw new FeatureNotAvailableException("Reports", "Reporting is not enabled on this instance");

        // Role is already enforced by .RequireRole(TenantRole.Viewer) on the endpoint,
        // but check the per-report minimum here in case the catalogue evolves.
        var report = ReportCatalogue.Find(reportKey)
            ?? throw new NotFoundException($"Report '{reportKey}' not found");

        _authCtx.RequireRole(report.MinimumRole);

        // Tenant comes from the session — never from the request body.
        var tenantId = _tenant.RequireTenantId();

        var dashboardUuid = await _bindings.GetDashboardUuidAsync(tenantId, reportKey, ct)
            ?? throw new FeatureNotAvailableException(
                "Reports",
                $"Report '{reportKey}' is not yet provisioned for this tenant. " +
                "An admin can trigger provisioning via the admin panel.");

        var token = await _engine.CreateGuestTokenAsync(dashboardUuid, ct);
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(_opts.EmbedTokenTtlSeconds);
        var publicBase = string.IsNullOrWhiteSpace(_opts.PublicBaseUrl) ? _opts.BaseUrl : _opts.PublicBaseUrl;
        var embedUrl = $"{publicBase.TrimEnd('/')}/embedded/{dashboardUuid}";

        await _audit.RecordEventAsync(
            _principal.UserId,
            "report.embed_token.issued",
            "report",
            reportKey,
            new { tenantId },
            ct);

        _logger.LogInformation(
            "Issued embed token for report {ReportKey} to user {UserId} in tenant {TenantId}",
            reportKey, _principal.UserId, tenantId);

        return new ReportEmbedTokenResult(reportKey, embedUrl, token, expiresAt);
    }

}
