using Orkyo.Migrations.Abstractions;

namespace Orkyo.Migrator;

/// <summary>
/// Deterministic ordering + dependency / duplicate-id validation across modules.
///
/// Order rule (per the architecture spec):
///   1. Filter by <see cref="MigrationTargetDatabase"/> (caller-supplied).
///   2. Sort by module <see cref="IMigrationModule.Order"/> ascending.
///   3. Within a module, sort by <see cref="MigrationScript.Id"/> lexically.
///   4. Validate <see cref="MigrationScript.DependsOn"/> against scripts that appear
///      earlier in the resulting order; reject forward / unknown / circular deps.
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
        ValidateDependencies(ordered);

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

    private static void ValidateDependencies(IReadOnlyList<MigrationScript> ordered)
    {
        var idToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < ordered.Count; i++) idToIndex[ordered[i].Id] = i;

        for (var i = 0; i < ordered.Count; i++)
        {
            var script = ordered[i];
            foreach (var dep in script.DependsOn)
            {
                if (!idToIndex.TryGetValue(dep, out var depIndex))
                {
                    throw new InvalidOperationException(
                        $"Migration '{script.Id}' depends on '{dep}' which is not registered " +
                        $"for target database '{script.TargetDatabase}'.");
                }
                if (depIndex >= i)
                {
                    throw new InvalidOperationException(
                        $"Migration '{script.Id}' depends on '{dep}' which is ordered after it. " +
                        $"Dependencies must apply earlier — check Module.Order or script Ids.");
                }
            }
        }
    }
}
