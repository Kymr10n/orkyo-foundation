using Orkyo.Migrations.Abstractions;
using Orkyo.Migrator;

namespace Orkyo.Foundation.Migrations;

/// <summary>
/// Foundation <see cref="IMigrationModule"/>. Loads any SQL embedded under <c>sql/</c>
/// in this assembly and exposes them as Orkyo migrations.
/// </summary>
/// <remarks>
/// Owns the shared migration set consumed by both editions — the scripts under
/// <c>sql/{controlplane,tenant}/</c> in this assembly. Adding a foundation migration
/// is dropping a numbered file there (see the migration rules in <c>CLAUDE.md</c>:
/// applied migrations are immutable; fixes are follow-up migrations).
/// </remarks>
public sealed class FoundationMigrationModule : IMigrationModule
{
    public string ModuleName => "foundation";

    /// <summary>
    /// Foundation always orders before product modules (SaaS=2000, Community=3000).
    /// </summary>
    public int Order => 1000;

    /// <summary>
    /// Tenant-phase migrations that assume the tenant schema has its own database. They predate the
    /// <c>-- @scope</c> directive and cannot be edited (applied migrations are immutable), so they are
    /// marked here by id. Community, which runs control plane and tenant in one database where the
    /// control-plane <c>feedback</c> table (1170) already lives, skips them: the create (1240) would
    /// collide with that table and the drop (1630) would destroy it. New migrations with the same
    /// property declare the directive in the file instead of growing this list.
    /// </summary>
    public static readonly IReadOnlySet<string> TenantDatabaseOnlyIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "1240.foundation.feedback",
        "1630.foundation.drop_feedback",
    };

    public IReadOnlyCollection<MigrationScript> GetMigrations() =>
        EmbeddedSqlLoader.LoadFromAssembly(typeof(FoundationMigrationModule).Assembly, ModuleName)
            .Select(s => s.Scope == MigrationScope.Default && TenantDatabaseOnlyIds.Contains(s.Id)
                ? s with { Scope = MigrationScope.TenantDatabaseOnly }
                : s)
            .ToList();
}
