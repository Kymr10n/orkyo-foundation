namespace Api.Services;

/// <summary>
/// Shared control-plane database health probe used by the admin diagnostics and settings
/// endpoints. A lightweight "open + SELECT 1" reachability check returning a stable status
/// string. Structurally identical in multi-tenant SaaS and single-tenant Community deployments
/// (both resolve the control-plane connection through <see cref="IDbConnectionFactory"/>).
/// </summary>
public static class DbHealthProbe
{
    /// <summary>Stable status string emitted when the control-plane DB is reachable.</summary>
    public const string HealthyStatus = "healthy";

    /// <summary>Stable status string emitted when the control-plane DB cannot be reached.</summary>
    public const string UnreachableStatus = "unreachable";

    /// <summary>
    /// Opens a control-plane connection and runs a trivial query. Returns
    /// <see cref="HealthyStatus"/> on success and <see cref="UnreachableStatus"/> on any failure
    /// (connection errors are intentionally swallowed — the probe never throws).
    /// </summary>
    public static async Task<string> ProbeControlPlaneAsync(
        IDbConnectionFactory connectionFactory, CancellationToken ct = default)
    {
        try
        {
            await using var conn = connectionFactory.CreateControlPlaneConnection();
            await conn.OpenAsync(ct);
            await using var cmd = new Npgsql.NpgsqlCommand("SELECT 1", conn);
            await cmd.ExecuteScalarAsync(ct);
            return HealthyStatus;
        }
        catch
        {
            return UnreachableStatus;
        }
    }
}
