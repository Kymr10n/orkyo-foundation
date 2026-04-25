namespace Orkyo.Migrations.Abstractions;

/// <summary>
/// Execution modes supported by the migration runner.
/// </summary>
public enum MigrationExecutionMode
{
    /// <summary>Apply pending migrations against the target database (default).</summary>
    Apply,

    /// <summary>
    /// Discover and order pending migrations + run a transactional rollback after each
    /// migration. Verifies the apply path without committing changes — used by CI.
    /// </summary>
    DryRun,

    /// <summary>
    /// Validate migration ordering, dependencies, and checksum stability against an
    /// already-migrated database. Reports drift without applying anything.
    /// </summary>
    ValidateOnly,
}
