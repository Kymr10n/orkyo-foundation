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
/// <param name="SupersededChecksums">
/// Checksums this migration's earlier text is known to have had, declared in the file with
/// <c>-- @supersedes-checksum: &lt;sha&gt;</c>. Applied migrations are immutable, so editing one
/// normally fails validation — correctly, because a silent edit means two installations ran
/// different SQL under one id. But immutability with no sanctioned exception leaves only two
/// options when a script must change: never fix it, or break every existing installation.
/// Declaring the superseded hash makes the exception explicit, reviewable in the diff, and
/// scoped to the one file, rather than a global "skip validation" switch.
/// <para>
/// Use only when the old and new text are equivalent for a database that already ran the old
/// one — a seed whose target table moved, not a change that would have produced different data.
/// </para>
/// </param>
public sealed record MigrationScript(
    string Id,
    string Module,
    MigrationTargetDatabase TargetDatabase,
    string Sql,
    string Checksum,
    IReadOnlyCollection<string> DependsOn,
    IReadOnlyCollection<string> SupersededChecksums);
