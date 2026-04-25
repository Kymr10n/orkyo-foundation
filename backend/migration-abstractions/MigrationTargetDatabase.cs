namespace Orkyo.Migrations.Abstractions;

/// <summary>
/// Identifies which logical database a migration targets. The runner uses this to
/// group and order migrations across SaaS's two databases (control plane + tenant)
/// while remaining a no-op distinction in Community's single-DB topology.
/// </summary>
public enum MigrationTargetDatabase
{
    /// <summary>
    /// SaaS control-plane database (auth, tenants, cross-tenant audit).
    /// In Community deployments this maps to the single deployment database.
    /// </summary>
    ControlPlane,

    /// <summary>
    /// Per-tenant database (domain data: sites, spaces, requests, scheduling).
    /// In Community deployments this also maps to the single deployment database.
    /// </summary>
    Tenant,
}
