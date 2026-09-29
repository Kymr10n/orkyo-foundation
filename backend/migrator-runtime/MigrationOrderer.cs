using Orkyo.Migrations.Abstractions;

namespace Orkyo.Migrator;

/// <summary>
/// Deterministic ordering + duplicate-id validation across modules.
///
/// Order rule (per the architecture spec):
///   1. Filter by <see cref="MigrationTargetDatabase"/> (caller-supplied).
///   2. Sort by module <see cref="IMigrationModule.Order"/> ascending.
///   3. Within a module, sort by <see cref="MigrationScript.Id"/> lexically.
/// </summary>
public static class MigrationOrderer
{
    public static IReadOnlyList<MigrationScript> Order(
        IEnumerable<IMigrationModule> modules,
        MigrationTargetDatabase target)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var ordered = modules
            .OrderBy(m => m.Order)
            .ThenBy(m => m.ModuleName, StringComparer.Ordinal)
            .SelectMany(m => m.GetMigrations()
                .Where(s => s.TargetDatabase == target)
                .OrderBy(s => s.Id, StringComparer.Ordinal))
            .ToList();

        ValidateNoDuplicateIds(ordered);

        return ordered;
    }

    private static void ValidateNoDuplicateIds(IReadOnlyList<MigrationScript> ordered)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var script in ordered)
        {
            if (!seen.Add(script.Id))
            {
                throw new InvalidOperationException(
                    $"Duplicate migration id '{script.Id}' across registered modules. " +
                    $"Each migration id must be globally unique within a target database.");
            }
        }
    }
}
