namespace Api.Reporting;

public interface ITenantReportingProvisioner
{
    /// <summary>
    /// Idempotently provisions reporting for a tenant:
    /// sets the reader role password, creates the Superset datasource,
    /// creates per-tenant dashboard copies from templates, and writes
    /// <c>tenant_reporting_state</c> + <c>tenant_report_bindings</c>.
    ///
    /// Failures are logged and recorded in <c>tenant_reporting_state.status = 'failed'</c>
    /// without propagating — reporting is non-critical to the scheduler.
    /// The admin reprovision endpoint retries.
    /// </summary>
    Task ProvisionAsync(Guid tenantId, string dbIdentifier, CancellationToken ct = default);
}
