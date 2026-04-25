namespace Api.Services;

/// <summary>
/// SQL contract for platform diagnostics scalars that are structurally
/// identical in multi-tenant SaaS and single-tenant Community deployments:
/// applied-migration count from <c>_migrations</c> and most-recent
/// <c>audit_events</c> activity timestamp within a fixed lookback window.
/// </summary>
public static class DiagnosticsQueryContract
{
    /// <summary>
    /// Lookback window used to consider audit-event activity "recent" for
    /// worker liveness detection. Locked here so both deployments and tests
    /// agree on the exact policy value.
    /// </summary>
    public const string RecentAuditActivityInterval = "2 hours";

    /// <summary>
    /// COUNT of rows in the migration tracking table.
    /// </summary>
    public static string BuildMigrationCountSql() =>
        "SELECT COUNT(*) FROM _migrations";

    /// <summary>
    /// MAX(created_at) over <c>audit_events</c> rows newer than
    /// <see cref="RecentAuditActivityInterval"/>. Returns <c>NULL</c> when no
    /// rows fall in the window.
    /// </summary>
    public static string BuildRecentAuditActivitySql() =>
        $"SELECT MAX(created_at) FROM audit_events WHERE created_at > NOW() - INTERVAL '{RecentAuditActivityInterval}'";
}
