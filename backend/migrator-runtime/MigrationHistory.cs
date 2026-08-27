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
    public const string TableName = MigrationSchema.TableName;

    private readonly NpgsqlConnection _connection;

    public MigrationHistory(NpgsqlConnection connection)
    {
        _connection = connection;
    }

    public async Task EnsureTableExistsAsync(CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(MigrationSchema.EnsureTableSql, _connection);
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

    /// <summary>
    /// Idempotent insert used by the legacy-adoption flow: marks <paramref name="script"/>
    /// as already-applied without executing its SQL. <c>execution_ms</c> is left NULL and
    /// <c>applied_by_version</c> is set to the supplied value (typically a marker like
    /// <c>"legacy-adoption-2026-04-25"</c>). Re-running is a no-op via
    /// <c>ON CONFLICT (id) DO NOTHING</c>.
    /// </summary>
    public async Task<bool> AdoptAppliedAsync(
        MigrationScript script,
        string? appliedByVersion,
        CancellationToken ct = default)
    {
        const string sql = $@"
            INSERT INTO {TableName}
                (id, module, target_database, checksum, applied_by_version, execution_ms, success)
            VALUES
                (@id, @module, @target, @checksum, @version, NULL, true)
            ON CONFLICT (id) DO NOTHING
        ";
        await using var cmd = new NpgsqlCommand(sql, _connection);
        cmd.Parameters.AddWithValue("id", script.Id);
        cmd.Parameters.AddWithValue("module", script.Module);
        cmd.Parameters.AddWithValue("target", script.TargetDatabase.ToString());
        cmd.Parameters.AddWithValue("checksum", script.Checksum);
        cmd.Parameters.AddWithValue("version", (object?)appliedByVersion ?? DBNull.Value);
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows == 1;
    }

    /// <summary>
    /// Rewrites an applied migration's stored checksum to the current text's, for a file that
    /// declared the old hash as superseded. Leaves applied_at and applied_by_version alone: the
    /// migration really did run then, and only its recorded text has changed.
    /// </summary>
    /// <remarks>
    /// Records what it replaced. The mechanism's justification is that it is explicit and
    /// reviewable, and a row whose checksum was rewritten in place is indistinguishable from
    /// one that was never touched — the log line that announced it is gone by the time anyone
    /// asks. These two columns are that record.
    /// </remarks>
    public async Task RefreshChecksumAsync(
        MigrationScript script, string supersededChecksum, CancellationToken ct = default)
    {
        const string sql = $@"
            UPDATE {TableName}
               SET checksum = @checksum,
                   superseded_checksum = @superseded,
                   superseded_at = now()
             WHERE id = @id";
        await using var cmd = new NpgsqlCommand(sql, _connection);
        cmd.Parameters.AddWithValue("id", script.Id);
        cmd.Parameters.AddWithValue("checksum", script.Checksum);
        cmd.Parameters.AddWithValue("superseded", supersededChecksum);
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
