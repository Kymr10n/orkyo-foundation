namespace Api.Reporting;

public interface IReportBindingRepository
{
    /// <summary>Returns the Superset dashboard UUID bound to this tenant+report, or null if unprovisioned.</summary>
    Task<Guid?> GetDashboardUuidAsync(Guid tenantId, string reportKey, CancellationToken ct = default);
}
