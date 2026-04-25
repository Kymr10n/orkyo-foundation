namespace Orkyo.Migrations.Abstractions;

/// <summary>
/// A single migration, fully described as a value object.
/// Migrations carry their own SQL + checksum so the runner can compose modules
/// from multiple sources (foundation + product) without filesystem coupling.
/// </summary>
/// <param name="Id">
/// Stable identifier — typically the filename without extension
/// (e.g. <c>V001__control_plane_schema</c>). Used for ordering and history-table keys.
/// </param>
/// <param name="Module">
/// Name of the <see cref="IMigrationModule"/> that owns this migration. Used for
/// observability and for preventing accidental cross-module ordering issues.
/// </param>
/// <param name="TargetDatabase">Which logical database this migration applies to.</param>
/// <param name="Sql">The SQL text. Line endings are normalized for checksum stability.</param>
/// <param name="Checksum">
/// SHA-256 (or equivalent) hash of the normalized SQL. Computed once at module load;
/// the runner verifies an applied migration's stored checksum matches before re-applying.
/// </param>
/// <param name="DependsOn">
/// Optional list of <see cref="Id"/>s that must apply before this one. Empty for the
/// common case where ordering is implied by lexical id within a module.
/// </param>
public sealed record MigrationScript(
    string Id,
    string Module,
    MigrationTargetDatabase TargetDatabase,
    string Sql,
    string Checksum,
    IReadOnlyCollection<string> DependsOn);
