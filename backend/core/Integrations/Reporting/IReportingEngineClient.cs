namespace Api.Integrations.Reporting;

public interface IReportingEngineClient
{
    /// <summary>
    /// Issues a short-lived Superset guest token scoped to a single dashboard.
    /// The token is signed by Superset and passed directly to the embedded SDK.
    /// </summary>
    Task<string> CreateGuestTokenAsync(Guid dashboardUuid, CancellationToken ct = default);

    /// <summary>
    /// Creates or retrieves the Superset Database (datasource) for a tenant's reporting schema.
    /// Returns the Superset database UUID. Idempotent — calling twice with the same name
    /// returns the existing record.
    /// </summary>
    Task<Guid> EnsureDatabaseAsync(
        string databaseName,
        string sqlAlchemyUri,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a copy of the given template dashboard with the supplied datasource UUID
    /// substituted. Returns the UUID of the newly created dashboard.
    /// </summary>
    Task<Guid> CopyDashboardAsync(
        Guid templateDashboardUuid,
        string newTitle,
        CancellationToken ct = default);
}
