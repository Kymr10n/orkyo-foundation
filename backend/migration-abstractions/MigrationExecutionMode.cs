namespace Orkyo.Migrations.Abstractions;

/// <summary>
/// Execution modes supported by the migration runner.
/// </summary>
public enum MigrationExecutionMode
{
    /// <summary>Apply pending migrations against the target database (default).</summary>
    Apply,

    /// <summary>
    /// Validate migration ordering and checksum stability against an
    /// already-migrated database. Reports drift without applying anything.
    /// </summary>
    ValidateOnly,
}
