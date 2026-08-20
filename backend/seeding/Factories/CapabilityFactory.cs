using System.Globalization;
using Bogus;
using Npgsql;
using NpgsqlTypes;
using Orkyo.Foundation.Seed.Narrative;

namespace Orkyo.Foundation.Seed.Factories;

/// <summary>
/// Seeds the skill/spec criteria (from <see cref="SkillCatalog"/>) and assigns capabilities to
/// resources so request→resource matching is demonstrable and satisfiable:
///   * people get facility-relevant skills (round-robin so every required skill is covered),
///   * machines/forklifts/cranes get their operation/load specs,
///   * QC rooms / paint booths get space specs.
/// Returns the per-person skill set so the narrative seeder can pick capability-matching assignees.
/// All capabilities live in one table — <c>resource_capabilities</c> (spaces share resources.id).
/// </summary>
public static class CapabilityFactory
{
    public static async Task<IReadOnlyDictionary<string, Guid>> SeedSkillCriteriaAsync(NpgsqlConnection conn)
    {
        var now = DateTime.UtcNow;
        var map = new Dictionary<string, Guid>();
        var skills = SkillCatalog.All;

        using (var writer = await conn.BeginBinaryImportAsync(
            "COPY public.criteria (id, name, data_type, description, unit, enum_values, created_at, updated_at) FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var s in skills)
            {
                var id = Guid.NewGuid();
                map[s.Key] = id;
                await writer.StartRowAsync();
                await writer.WriteAsync(id, NpgsqlDbType.Uuid);
                await writer.WriteAsync(s.Name, NpgsqlDbType.Varchar);
                await writer.WriteAsync(s.DataType, NpgsqlDbType.Varchar);
                await writer.WriteNullAsync(); // description
                if (s.Unit is not null) await writer.WriteAsync(s.Unit, NpgsqlDbType.Varchar); else await writer.WriteNullAsync();
                if (s.EnumValues is not null)
                    await writer.WriteAsync(System.Text.Json.JsonSerializer.Serialize(s.EnumValues), NpgsqlDbType.Jsonb);
                else await writer.WriteNullAsync();
                await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
                await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
            }
            await writer.CompleteAsync();
        }

        var typeIds = await ResolveTypeIdsAsync(conn);
        using (var writer = await conn.BeginBinaryImportAsync(
            "COPY public.criterion_resource_types (criterion_id, resource_type_id) FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var s in skills)
                foreach (var key in TypesFor(s.Key))
                {
                    if (!typeIds.TryGetValue(key, out var rtId)) continue;
                    await writer.StartRowAsync();
                    await writer.WriteAsync(map[s.Key], NpgsqlDbType.Uuid);
                    await writer.WriteAsync(rtId, NpgsqlDbType.Uuid);
                }
            await writer.CompleteAsync();
        }

        return map;
    }

    public sealed record AssignResult(IReadOnlyDictionary<Guid, HashSet<Guid>> PersonSkills, int Total);

    public static async Task<AssignResult> AssignAsync(
        NpgsqlConnection conn,
        IReadOnlyDictionary<string, Guid> criteria,
        IReadOnlyList<FacilityCohort> cohorts,
        Faker faker)
    {
        var personSkills = new Dictionary<Guid, HashSet<Guid>>();
        var rows = new List<(Guid ResourceId, Guid CriterionId, string ValueJson)>();

        foreach (var cohort in cohorts)
        {
            var required = FacilityModel.RequiredPersonSkills(cohort.Facility);
            if (required.Count == 0 || cohort.People.Count == 0) continue;

            void AddPersonCap(Guid pid, string skillKey)
            {
                if (!personSkills.TryGetValue(pid, out var set)) personSkills[pid] = set = [];
                if (set.Add(criteria[skillKey])) rows.Add((pid, criteria[skillKey], ValueFor(skillKey, faker)));
            }

            // 1) Personas: a person's skills come from the role they hold, so a QA Tech inspects
            //    and a welder welds. The roster is what makes job title, department, team and
            //    skills tell one story instead of four unrelated ones — see PersonaCatalog.
            var roster = PersonaCatalog.Roster(cohort.Facility.SiteCode);
            for (var j = 0; j < cohort.People.Count; j++)
            {
                foreach (var skill in roster[j % roster.Count].Skills)
                    AddPersonCap(cohort.People[j].ResourceId, skill);
            }

            // 2) Coverage backstop: a cohort smaller than its roster can miss a required skill,
            //    and a facility whose work nobody can do reads as broken rather than as a demo.
            for (var k = 0; k < required.Count; k++)
            {
                var pid = cohort.People[k % cohort.People.Count].ResourceId;
                if (!personSkills.TryGetValue(pid, out var held) || !held.Contains(criteria[required[k]]))
                    AddPersonCap(pid, required[k]);
            }

            // Tools: machines carry their operation skill; forklifts/cranes carry Max Load.
            foreach (var tool in cohort.Tools)
            {
                if (tool.Role == "cnc")
                    rows.Add((tool.Id, criteria[SkillCatalog.CncOperation], "true"));
                if (tool.MaxLoadTons is { } load)
                    rows.Add((tool.Id, criteria[SkillCatalog.MaxLoadTons], load.ToString(CultureInfo.InvariantCulture)));
            }

            // Machines carry the skill their work needs, the same way the tools above do — a job
            // requiring CNC operation is satisfiable by the mill it is booked onto.
            foreach (var machine in cohort.Machines)
            {
                var skill = machine.Role switch
                {
                    MachineCatalog.MillRole or MachineCatalog.LatheRole or MachineCatalog.CncRole
                        => SkillCatalog.CncOperation,
                    MachineCatalog.DrillRole => SkillCatalog.Drilling,
                    MachineCatalog.AssemblyRole => SkillCatalog.Assembly,
                    MachineCatalog.TestRole => SkillCatalog.QaInspection,
                    _ => null,
                };
                if (skill is not null) rows.Add((machine.Id, criteria[skill], "true"));
            }

            // Spaces: QC rooms are clean rooms; the paint booth is ventilated. Rooms carry only
            // their own space-specs — never person-skills — so applicability stays honest
            // (person-skills→person, space-specs→space) and capability conflicts are checked against
            // the assigned people, not the room. See ConflictService for the request-level match.
            if (cohort.SpaceByRoomCode.TryGetValue("QC", out var qc))
                rows.Add((qc.Id, criteria[SkillCatalog.CleanRoom], "true"));
            if (cohort.SpaceByRoomCode.TryGetValue("PAINT", out var paint))
                rows.Add((paint.Id, criteria[SkillCatalog.Ventilated], "true"));
        }

        using (var writer = await conn.BeginBinaryImportAsync(
            "COPY public.resource_capabilities (resource_id, criterion_id, value) FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var (rid, cid, json) in rows)
            {
                await writer.StartRowAsync();
                await writer.WriteAsync(rid, NpgsqlDbType.Uuid);
                await writer.WriteAsync(cid, NpgsqlDbType.Uuid);
                await writer.WriteAsync(json, NpgsqlDbType.Jsonb);
            }
            await writer.CompleteAsync();
        }

        return new AssignResult(personSkills, rows.Count);
    }

    // value jsonb matching CapabilityMatcher: Boolean→true, Enum→"VALUE", Number→n.
    private static string ValueFor(string skillKey, Faker faker)
    {
        var skill = SkillCatalog.ByKey(skillKey);
        return skill.DataType switch
        {
            "Boolean" => "true",
            "Enum" => $"\"{faker.PickRandom(skill.EnumValues!)}\"",
            "Number" => "1",
            _ => "true",
        };
    }

    private static string[] TypesFor(string key) => key switch
    {
        // A machining skill belongs to the people who hold it and to the machines that satisfy
        // it, so a job requiring CNC operation is coverable by the mill, lathe or centre it books.
        SkillCatalog.CncOperation => ["person", "tool", "mill", "lathe", "cnc"],
        SkillCatalog.Drilling => ["person", "drill"],
        SkillCatalog.Assembly => ["person", "assembly_station"],
        SkillCatalog.QaInspection => ["person", "test_station"],
        SkillCatalog.CleanRoom or SkillCatalog.Ventilated => ["room"],
        SkillCatalog.MaxLoadTons => ["tool", "room"],
        _ => ["person"],
    };

    private static async Task<Dictionary<string, Guid>> ResolveTypeIdsAsync(NpgsqlConnection conn)
    {
        var result = new Dictionary<string, Guid>();
        await using var cmd = new NpgsqlCommand(
            "SELECT key, id FROM public.resource_types WHERE key IN " +
            "('room','person','tool','mill','drill','lathe','cnc','assembly_station','test_station')", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result[reader.GetString(0)] = reader.GetGuid(1);
        return result;
    }
}
