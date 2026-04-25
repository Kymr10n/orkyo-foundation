using System.Data;
using DbUp.Engine;
using DbUp.Engine.Output;
using DbUp.Engine.Transactions;
using Orkyo.Migrations.Abstractions;

namespace Orkyo.Migrator;

/// <summary>
/// DbUp <see cref="IJournal"/> backed by Orkyo's <c>orkyo_schema_migrations</c> table.
/// Augments DbUp's "did this run?" tracking with the Orkyo metadata required by the
/// migration spec: module, target database, checksum, applied_by_version, execution_ms.
/// </summary>
/// <remarks>
/// Each pending script must be present in <paramref name="byId"/> so the journal can
/// look up the Orkyo metadata when DbUp asks it to record a successful apply.
/// </remarks>
internal sealed class OrkyoDbUpJournal : IJournal
{
    public const string TableName = "orkyo_schema_migrations";

    private readonly Func<IConnectionManager> _connectionManager;
    private readonly Func<IUpgradeLog> _log;
    private readonly IReadOnlyDictionary<string, MigrationScript> _byId;
    private readonly string? _appliedByVersion;

    public OrkyoDbUpJournal(
        Func<IConnectionManager> connectionManager,
        Func<IUpgradeLog> log,
        IReadOnlyDictionary<string, MigrationScript> byId,
        string? appliedByVersion)
    {
        _connectionManager = connectionManager;
        _log = log;
        _byId = byId;
        _appliedByVersion = appliedByVersion;
    }

    public string[] GetExecutedScripts()
    {
        return _connectionManager().ExecuteCommandsWithManagedConnection(dbCommandFactory =>
        {
            using var cmd = dbCommandFactory();
            cmd.CommandText = $"SELECT id FROM {TableName} ORDER BY id";
            using var reader = cmd.ExecuteReader();
            var ids = new List<string>();
            while (reader.Read()) ids.Add(reader.GetString(0));
            return ids.ToArray();
        });
    }

    public void EnsureTableExistsAndIsLatestVersion(Func<IDbCommand> dbCommandFactory)
    {
        using var cmd = dbCommandFactory();
        cmd.CommandText = $@"
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
        cmd.ExecuteNonQuery();
    }

    public void StoreExecutedScript(SqlScript script, Func<IDbCommand> dbCommandFactory)
    {
        if (!_byId.TryGetValue(script.Name, out var orkyoScript))
        {
            throw new InvalidOperationException(
                $"DbUp executed script '{script.Name}' but no Orkyo MigrationScript metadata " +
                $"was registered for it. This is a runner bug — every script handed to DbUp " +
                $"must have a corresponding entry in the Orkyo journal map.");
        }

        using var cmd = dbCommandFactory();
        cmd.CommandText = $@"
            INSERT INTO {TableName}
                (id, module, target_database, checksum, applied_by_version, execution_ms, success)
            VALUES
                (@id, @module, @target, @checksum, @version, NULL, true)
        ";
        AddParam(cmd, "id", orkyoScript.Id);
        AddParam(cmd, "module", orkyoScript.Module);
        AddParam(cmd, "target", orkyoScript.TargetDatabase.ToString());
        AddParam(cmd, "checksum", orkyoScript.Checksum);
        AddParam(cmd, "version", (object?)_appliedByVersion ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        _log().LogInformation("Recorded migration {0} (module={1}, target={2})",
            orkyoScript.Id, orkyoScript.Module, orkyoScript.TargetDatabase);
    }

    private static void AddParam(IDbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
