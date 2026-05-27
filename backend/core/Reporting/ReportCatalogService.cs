using Api.Security;
using Microsoft.Extensions.Options;

namespace Api.Reporting;

public sealed class ReportCatalogService : IReportCatalogService
{
    private readonly IAuthorizationContext _authCtx;
    private readonly ReportingOptions _opts;

    public ReportCatalogService(IAuthorizationContext authCtx, IOptions<ReportingOptions> opts)
    {
        _authCtx = authCtx;
        _opts = opts.Value;
    }

    public IReadOnlyList<ReportDefinition> GetVisibleReports()
    {
        if (!_opts.Enabled) return Array.Empty<ReportDefinition>();

        var role = _authCtx.Role;
        return ReportCatalogue.All
            .Where(r => role >= r.MinimumRole)
            .ToList();
    }
}
