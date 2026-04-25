using Npgsql;
using Orkyo.Migrations.Abstractions;

namespace Orkyo.Migrator;

/// <summary>
/// Owns the <c>orkyo_schema_migrations</c> table — creation, reads, writes, and
/// checksum-drift detection. Replaces the legacy single-column <c>_migrations</c>
/// table from <c>Orkyo.Migrations.MigrationEngine</c>; both can coexist during the
/// transition since the names differ.
/// </summary>
internal sealed class MigrationHistory
{
    public const string TableName = "orkyo_schema_migrations";

    private readonly NpgsqlConnection _connection;

    public MigrationHistory(NpgsqlConnection connection)
    {
        _connection = connection;
    }

    public async Task EnsureTableExistsAsync(CancellationToken ct = default)
    {
        const string ddl = $@"
            CREATE TABLE IF NOT EXISTS {TableName} (
                id                 text        PRIMARY KEY,
                module             text        NOT NULL,
                target_database    text        NOT NULL,
                checksum           text        NOT NULL,
                script_hash_algo   text        NOT NULL DEFAULT 'SHA256',
                applied_at         timestamptz NOT NULL DEFAULT now(),
                applied_by_version text        NULL,
                execution_ms       integer     NULL,
                success            boolean     NOT NULL DEFAULT true,
                error_message      text        NULL
            );
            CREATE INDEX IF NOT EXISTS idx_{TableName}_target_database
                ON {TableName} (target_database);
        ";
        await using var cmd = new NpgsqlCommand(ddl, _connection);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string, AppliedMigration>> LoadAppliedAsync(CancellationToken ct = default)
    {
        var result = new Dictionary<string, AppliedMigration>(StringComparer.Ordinal);
        await using var cmd = new NpgsqlCommand(
            $"SELECT id, module, target_database, checksum FROM {TableName}", _connection);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetString(0);
            result[id] = new AppliedMigration(
                Id: id,
                Module: reader.GetString(1),
                TargetDatabase: ParseTarget(reader.GetString(2)),
                Checksum: reader.GetString(3));
        }
        return result;
    }

    public async Task RecordAppliedAsync(
        MigrationScript script,
        int executionMs,
        string? appliedByVersion,
        NpgsqlTransaction? transaction = null,
        CancellationToken ct = default)
    {
        const string sql = $@"
            INSERT INTO {TableName}
                (id, module, target_database, checksum, applied_by_version, execution_ms, success)
            VALUES
                (@id, @module, @target, @checksum, @version, @ms, true)
        ";
        await using var cmd = new NpgsqlCommand(sql, _connection, transaction);
        cmd.Parameters.AddWithValue("id", script.Id);
        cmd.Parameters.AddWithValue("module", script.Module);
        cmd.Parameters.AddWithValue("target", script.TargetDatabase.ToString());
        cmd.Parameters.AddWithValue("checksum", script.Checksum);
        cmd.Parameters.AddWithValue("version", (object?)appliedByVersion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("ms", executionMs);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static MigrationTargetDatabase ParseTarget(string raw) =>
        Enum.TryParse<MigrationTargetDatabase>(raw, ignoreCase: false, out var v)
            ? v
            : throw new InvalidOperationException(
                $"History row contains unknown target_database '{raw}'. " +
                $"Did the {nameof(MigrationTargetDatabase)} enum change shape?");
}

internal sealed record AppliedMigration(
    string Id,
    string Module,
    MigrationTargetDatabase TargetDatabase,
    string Checksum);
