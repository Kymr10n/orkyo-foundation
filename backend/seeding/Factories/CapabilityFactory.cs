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
    public static async Task<IReadOnlyDictionary<string, Guid>> SeedSkillCriteriaAsync(
        NpgsqlConnection conn, bool includeTools)
    {
        var now = DateTime.UtcNow;
        var map = new Dictionary<string, Guid>();
        var skills = includeTools
            ? SkillCatalog.All
            : SkillCatalog.All.Where(s => s.Kind != SkillKind.ToolSpec).ToList();

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
                foreach (var key in TypesFor(s.Key, includeTools))
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

            // 1) Coverage: assign every required skill to at least one person (works even when the
            //    cohort has fewer people than skills — a person may hold several).
            for (var k = 0; k < required.Count; k++)
                AddPersonCap(cohort.People[k % cohort.People.Count].ResourceId, required[k]);

            // 2) Richness: each person also holds a primary facility skill by index.
            for (var j = 0; j < cohort.People.Count; j++)
                AddPersonCap(cohort.People[j].ResourceId, required[j % required.Count]);

            // Tools: machines carry their operation skill; forklifts/cranes carry Max Load.
            foreach (var tool in cohort.Tools)
            {
                if (tool.Role == "cnc")
                    rows.Add((tool.Id, criteria[SkillCatalog.CncOperation], "true"));
                if (tool.MaxLoadTons is { } load)
                    rows.Add((tool.Id, criteria[SkillCatalog.MaxLoadTons], load.ToString(CultureInfo.InvariantCulture)));
            }

            // Spaces: QC rooms are clean rooms; the paint booth is ventilated.
            if (cohort.SpaceByRoomCode.TryGetValue("QC", out var qc))
                rows.Add((qc.Id, criteria[SkillCatalog.CleanRoom], "true"));
            if (cohort.SpaceByRoomCode.TryGetValue("PAINT", out var paint))
                rows.Add((paint.Id, criteria[SkillCatalog.Ventilated], "true"));

            // Rooms inherit the required-skill capabilities of the archetypes that use them so
            // that correctly-routed space assignments satisfy their jobs' requirements.
            foreach (var arch in cohort.Facility.Archetypes)
            {
                if (!cohort.SpaceByRoomCode.TryGetValue(arch.RoomCode, out var room)) continue;
                foreach (var skillKey in arch.RequiredSkills)
                {
                    if (rows.Any(r => r.ResourceId == room.Id && r.CriterionId == criteria[skillKey])) continue;
                    rows.Add((room.Id, criteria[skillKey], ValueFor(skillKey, faker)));
                }
            }
        }

        using var writer = await conn.BeginBinaryImportAsync(
            "COPY public.resource_capabilities (resource_id, criterion_id, value) FROM STDIN (FORMAT BINARY)");
        foreach (var (rid, cid, json) in rows)
        {
            await writer.StartRowAsync();
            await writer.WriteAsync(rid, NpgsqlDbType.Uuid);
            await writer.WriteAsync(cid, NpgsqlDbType.Uuid);
            await writer.WriteAsync(json, NpgsqlDbType.Jsonb);
        }
        await writer.CompleteAsync();

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

    private static string[] TypesFor(string key, bool includeTools) => key switch
    {
        SkillCatalog.CncOperation => includeTools ? ["person", "tool"] : ["person"],
        SkillCatalog.CleanRoom or SkillCatalog.Ventilated => ["space"],
        SkillCatalog.MaxLoadTons => ["tool", "space"],
        _ => ["person"],
    };

    private static async Task<Dictionary<string, Guid>> ResolveTypeIdsAsync(NpgsqlConnection conn)
    {
        var result = new Dictionary<string, Guid>();
        await using var cmd = new NpgsqlCommand(
            "SELECT key, id FROM public.resource_types WHERE key IN ('space','person','tool')", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result[reader.GetString(0)] = reader.GetGuid(1);
        return result;
    }
}
