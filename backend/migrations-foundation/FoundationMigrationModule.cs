using Orkyo.Migrations.Abstractions;
using Orkyo.Migrator;

namespace Orkyo.Foundation.Migrations;

/// <summary>
/// Foundation <see cref="IMigrationModule"/>. Loads any SQL embedded under <c>sql/</c>
/// in this assembly and exposes them as Orkyo migrations.
/// </summary>
/// <remarks>
/// Currently empty by design — see <c>orkyo-saas/requirements/orkyo-migration-inventory-2026-04.md</c>:
/// every existing migration is SaaS-shaped, so foundation starts as a slot for future
/// genuinely-shared migrations. The module discovery infrastructure is in place so adding
/// a foundation script later is just dropping a file under <c>sql/{controlplane,tenant}/</c>.
/// </remarks>
public sealed class FoundationMigrationModule : IMigrationModule
{
    public string ModuleName => "foundation";

    /// <summary>
    /// Foundation always orders before product modules (SaaS=2000, Community=3000).
    /// </summary>
    public int Order => 1000;

    public IReadOnlyCollection<MigrationScript> GetMigrations() =>
        EmbeddedSqlLoader.LoadFromAssembly(typeof(FoundationMigrationModule).Assembly, ModuleName);
}
