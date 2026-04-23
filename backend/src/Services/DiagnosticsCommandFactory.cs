using Npgsql;

namespace Api.Services;

public static class DiagnosticsCommandFactory
{
    public static NpgsqlCommand CreateMigrationCountCommand(NpgsqlConnection connection) =>
        new(DiagnosticsQueryContract.BuildMigrationCountSql(), connection);

    public static NpgsqlCommand CreateRecentAuditActivityCommand(NpgsqlConnection connection) =>
        new(DiagnosticsQueryContract.BuildRecentAuditActivitySql(), connection);
}

public static class DiagnosticsScalarFlow
{
    /// <summary>
    /// Read the migration count scalar. Treats <c>null</c> as <c>0</c>.
    /// Tolerates Postgres returning either <c>long</c> (COUNT(*)) or <c>int</c>.
    /// </summary>
    public static int ReadMigrationCount(object? scalarResult) =>
        scalarResult is null or DBNull ? 0 : Convert.ToInt32(scalarResult);

    /// <summary>
    /// Read the most-recent audit activity timestamp. Returns <c>null</c> when
    /// the lookback window contained no rows (MAX returned NULL).
    /// </summary>
    public static DateTime? ReadRecentAuditActivity(object? scalarResult) =>
        scalarResult is DateTime dt ? dt : null;
}
