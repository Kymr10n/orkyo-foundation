namespace Orkyo.Migrations.Abstractions;

/// <summary>
/// Outcome of a single migration's execution.
/// </summary>
/// <param name="Script">The migration that was processed.</param>
/// <param name="Outcome">Applied / Skipped (already present) / Failed / DryRunSucceeded / Validated.</param>
/// <param name="ExecutionMs">Wall-clock execution time, or <c>null</c> for skipped/validate-only outcomes.</param>
/// <param name="ErrorMessage">Populated when <see cref="Outcome"/> is <see cref="MigrationOutcome.Failed"/>.</param>
public sealed record MigrationResult(
    MigrationScript Script,
    MigrationOutcome Outcome,
    int? ExecutionMs,
    string? ErrorMessage);

/// <summary>Discrete outcomes a single migration can produce.</summary>
public enum MigrationOutcome
{
    /// <summary>SQL applied + history row written.</summary>
    Applied,

    /// <summary>Already in the history table with a matching checksum — no-op.</summary>
    Skipped,

    /// <summary>Apply failed; <see cref="MigrationResult.ErrorMessage"/> describes why.</summary>
    Failed,

    /// <summary>SQL applied inside a rolled-back transaction (DryRun mode).</summary>
    DryRunSucceeded,

    /// <summary>Migration verified against history without execution (ValidateOnly mode).</summary>
    Validated,
}
