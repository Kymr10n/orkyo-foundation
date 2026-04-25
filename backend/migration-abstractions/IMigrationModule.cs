namespace Orkyo.Migrations.Abstractions;

/// <summary>
/// A registrable bundle of migrations contributed by either foundation or a product
/// composition (SaaS, Community). The runner composes a deterministic apply order
/// across all registered modules using <see cref="Order"/> and the lexical id of each
/// <see cref="MigrationScript"/>.
/// </summary>
/// <remarks>
/// Implementations should be pure: <see cref="GetMigrations"/> must return the same
/// scripts (with the same checksums) on every call within a process lifetime.
/// </remarks>
public interface IMigrationModule
{
    /// <summary>
    /// Stable, human-meaningful identifier (e.g. <c>"foundation"</c>, <c>"saas-controlplane"</c>,
    /// <c>"saas-tenant"</c>). Recorded in the migration history for traceability.
    /// </summary>
    string ModuleName { get; }

    /// <summary>
    /// Module-level ordering hint. Lower values run first across modules that target the
    /// same database. Use it to guarantee foundation-shared migrations apply before
    /// product migrations. Within a module, scripts are ordered by their lexical
    /// <see cref="MigrationScript.Id"/>.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Returns the migrations contributed by this module. Called once at startup;
    /// implementations should snapshot any I/O at that point.
    /// </summary>
    IReadOnlyCollection<MigrationScript> GetMigrations();
}
