using Npgsql;
using NpgsqlTypes;
using Orkyo.Foundation.Seed.Narrative;

namespace Orkyo.Foundation.Seed.Factories;

/// <summary>
/// Writes each person's job title and department from the role they hold.
/// </summary>
/// <remarks>
/// Runs after the cohorts are built, because a persona belongs to a facility and the cohort split
/// is what says which facility a person works at. The values are list-row lookups, so this writes
/// the same <c>custom_fields</c> shape the resource form does.
/// </remarks>
public static class PersonaFactory
{
    public static async Task<int> ApplyAsync(
        NpgsqlConnection conn,
        IReadOnlyList<FacilityCohort> cohorts,
        IReadOnlyList<PeopleFactories.SeededJobTitle> jobTitles,
        IReadOnlyList<PeopleFactories.SeededDepartment> departments)
    {
        var titleByName = jobTitles
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        var deptByName = departments
            .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        var ids = new List<Guid>();
        var docs = new List<string>();

        foreach (var cohort in cohorts)
        {
            for (var j = 0; j < cohort.People.Count; j++)
            {
                var persona = PersonaCatalog.For(cohort.Facility.SiteCode, j);
                var parts = new List<string>(2);

                if (titleByName.TryGetValue(persona.JobTitle, out var titleId))
                    parts.Add($"\"job_title\":[\"{titleId}\"]");

                // The persona names a child department ("Production Machining"); a smaller scale
                // may only have seeded its root. Fall back to the root, then leave it unset rather
                // than attaching a department the person does not belong to.
                if (ResolveDepartment(deptByName, persona.Department) is { } deptId)
                    parts.Add($"\"department\":[\"{deptId}\"]");

                if (parts.Count == 0) continue;
                ids.Add(cohort.People[j].ResourceId);
                docs.Add("{" + string.Join(",", parts) + "}");
            }
        }

        if (ids.Count == 0) return 0;

        await using var update = new NpgsqlCommand(
            "UPDATE public.resources r " +
            "SET custom_fields = COALESCE(r.custom_fields, '{}'::jsonb) || v.doc::jsonb " +
            "FROM (SELECT unnest(@ids) AS id, unnest(@docs) AS doc) v WHERE r.id = v.id", conn);
        update.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = ids.ToArray() });
        update.Parameters.Add(new NpgsqlParameter("docs", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = docs.ToArray() });
        return await update.ExecuteNonQueryAsync();
    }

    /// <summary>The child department, else its root, else nothing.</summary>
    private static Guid? ResolveDepartment(IReadOnlyDictionary<string, Guid> byName, string name)
    {
        if (byName.TryGetValue(name, out var exact)) return exact;

        var space = name.IndexOf(' ');
        if (space > 0 && byName.TryGetValue(name[..space], out var root)) return root;

        return null;
    }
}
