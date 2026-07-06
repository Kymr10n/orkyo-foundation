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

    public IReadOnlyCollection<MigrationScript> GetMigrations() =>
        EmbeddedSqlLoader.LoadFromAssembly(typeof(FoundationMigrationModule).Assembly, ModuleName);
}
