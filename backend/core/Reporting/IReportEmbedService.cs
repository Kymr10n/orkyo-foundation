namespace Api.Reporting;

public interface IReportEmbedService
{
    /// <summary>
    /// Issues a short-lived Superset guest token for the given report key.
    /// Tenant is sourced from <see cref="Api.Security.ICurrentTenant"/> — any
    /// tenant ID in the request body is ignored to prevent cross-tenant access.
    /// </summary>
    /// <exception cref="Api.Helpers.NotFoundException">Unknown report key.</exception>
    /// <exception cref="System.UnauthorizedAccessException">Caller's role is below the report's minimum role.</exception>
    /// <exception cref="Api.Helpers.FeatureNotAvailableException">Reporting disabled or tenant not provisioned.</exception>
    Task<ReportEmbedTokenResult> CreateEmbedTokenAsync(string reportKey, CancellationToken ct = default);
}
