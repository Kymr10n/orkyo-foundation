namespace Orkyo.Migrations.Abstractions;

/// <summary>
/// Runtime options for a migration execution. Composition layers populate this from
/// environment variables (see <c>Orkyo.Migrator</c> CLI / DI bootstrap).
/// </summary>
public sealed record MigrationOptions
{
    /// <summary>Apply / DryRun / ValidateOnly. Defaults to <see cref="MigrationExecutionMode.Apply"/>.</summary>
    public MigrationExecutionMode Mode { get; init; } = MigrationExecutionMode.Apply;

    /// <summary>
    /// Optional filter — when set, only migrations targeting this database are
    /// executed. Useful for splitting CI jobs by target.
    /// </summary>
    public MigrationTargetDatabase? TargetFilter { get; init; }

    /// <summary>
    /// Per-attempt timeout (in seconds) for acquiring the per-database advisory lock
    /// that prevents concurrent runners. Defaults to 60 s.
    /// </summary>
    public int LockTimeoutSeconds { get; init; } = 60;

    /// <summary>
    /// Free-form version string recorded against each row in the history table.
    /// Typically the deploying release tag or commit SHA.
    /// </summary>
    public string? AppliedByVersion { get; init; }
}
