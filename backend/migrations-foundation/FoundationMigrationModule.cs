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
    /// Tenant-phase migrations that assume the tenant schema has its own database, marked by id
    /// (applied migrations are immutable, so the mark cannot live in the file). Community, which runs
    /// control plane and tenant in one database where the control-plane <c>feedback</c> table (1170)
    /// already lives, skips them: the create (1240) would collide with that table and the drop (1630)
    /// would destroy it. A new migration with the same property is added to this list.
    /// </summary>
    public static readonly IReadOnlySet<string> TenantDatabaseOnlyIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "1240.foundation.feedback",
        "1630.foundation.drop_feedback",
    };

    public IReadOnlyCollection<MigrationScript> GetMigrations() =>
        EmbeddedSqlLoader.LoadFromAssembly(typeof(FoundationMigrationModule).Assembly, ModuleName)
            .Select(s => TenantDatabaseOnlyIds.Contains(s.Id)
                ? s with { Scope = MigrationScope.TenantDatabaseOnly }
                : s)
            .ToList();
}
