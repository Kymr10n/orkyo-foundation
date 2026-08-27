namespace Orkyo.Migrator;

/// <summary>
/// The one definition of the <c>orkyo_schema_migrations</c> table. Both creators —
/// <see cref="MigrationHistory"/> (runs early, before checksum refresh) and
/// <see cref="OrkyoDbUpJournal"/> (runs when DbUp ensures its journal) — execute this
/// same DDL, so they cannot drift apart. Everything is IF NOT EXISTS / ADD COLUMN IF
/// NOT EXISTS: running it twice per deploy is the design, not an accident.
/// </summary>
internal static class MigrationSchema
{
    public const string TableName = "orkyo_schema_migrations";

    public const string EnsureTableSql = $@"
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
        -- Provenance for a migration whose text was deliberately replaced after it ran
        -- (see the @supersedes-checksum directive). Added after the table shipped, so the
        -- ALTERs carry their own guards rather than living in the CREATE above.
        ALTER TABLE {TableName} ADD COLUMN IF NOT EXISTS superseded_checksum text NULL;
        ALTER TABLE {TableName} ADD COLUMN IF NOT EXISTS superseded_at timestamptz NULL;
        CREATE INDEX IF NOT EXISTS idx_{TableName}_target_database
            ON {TableName} (target_database);
    ";
}
