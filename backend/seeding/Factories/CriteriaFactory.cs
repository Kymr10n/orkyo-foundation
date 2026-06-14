using Bogus;
using Npgsql;
using NpgsqlTypes;
using Orkyo.Foundation.Seed.Scales;

namespace Orkyo.Foundation.Seed.Factories;

/// <summary>
/// Seeds criteria and their resource-type applicability rows.
/// Criteria are domain-agnostic attribute definitions — no profile-specific pool required.
/// Each criterion is randomly assigned to one or more resource types (space / person).
/// </summary>
public static class CriteriaFactory
{
    public sealed record SeededCriterion(Guid Id, string Name);

    // Fixed pool of realistic criteria. Covers the three resource types naturally.
    // Names must satisfy the API rule: start with a letter, then letters/digits/underscores/hyphens.
    // The seeder picks a subset based on scale.Criteria.
    private static readonly IReadOnlyList<(string Name, string DataType, string? Unit, string? Description, string[] ResourceTypeKeys)> Pool =
    [
        ("Capacity_persons",         "Number",  "persons",  "Maximum number of occupants.",                                      ["space"]),
        ("AV_Equipment",             "Boolean", null,       "Room has audio/visual equipment.",                                   ["space"]),
        ("Whiteboard",               "Boolean", null,       "Room has a whiteboard.",                                             ["space"]),
        ("Standing_Desks",           "Boolean", null,       "Workstation has standing-desk capability.",                          ["space"]),
        ("Natural_Light",            "Boolean", null,       "Space receives natural daylight.",                                   ["space"]),
        ("Video_Conferencing",       "Boolean", null,       "Equipped for video conferencing.",                                   ["space"]),
        ("Noise_Level",              "Enum",    null,       "Typical ambient noise level.",                                       ["space"]),
        ("Floor_Area_m2",            "Number",  "m²",       "Usable floor area in square metres.",                               ["space"]),
        ("Parking_Spaces",           "Number",  "spaces",   "Number of reserved parking spots.",                                  ["space"]),
        ("Accessibility",            "Boolean", null,       "Fully accessible for mobility-impaired individuals.",                ["space"]),
        ("Years_Experience",         "Number",  "years",    "Total professional experience.",                                     ["person"]),
        ("Certification_Level",      "Enum",    null,       "Professional certification tier.",                                   ["person"]),
        ("Remote_Work_Capable",      "Boolean", null,       "Person can work fully remotely.",                                    ["person"]),
        ("Language_English",         "Boolean", null,       "Proficient in English.",                                             ["person"]),
        ("Language_Spanish",         "Boolean", null,       "Proficient in Spanish.",                                             ["person"]),
        ("Language_French",          "Boolean", null,       "Proficient in French.",                                              ["person"]),
        ("Drivers_Licence",          "Boolean", null,       "Holds a valid driver's licence.",                                    ["person"]),
        ("Security_Clearance",       "Enum",    null,       "Government security clearance level.",                               ["person"]),
        ("First_Aid_Certified",      "Boolean", null,       "Holds a current first-aid certificate.",                             ["person"]),
        ("Min_Clearance_Height_m",   "Number",  "m",        "Minimum overhead clearance required.",                               ["space"]),
        ("Power_Supply_kW",          "Number",  "kW",       "Electrical power draw or requirement.",                              ["space"]),
        ("Safety_Rating",            "Enum",    null,       "Regulatory safety rating.",                                          ["person"]),
        ("Project_Lead_Eligible",    "Boolean", null,       "May be assigned as project lead.",                                   ["person"]),
    ];

    private static readonly string[] NoiseEnumValues = ["quiet", "moderate", "loud"];
    private static readonly string[] CertLevelValues = ["associate", "professional", "expert"];
    private static readonly string[] ClearanceValues = ["none", "confidential", "secret", "top-secret"];
    private static readonly string[] SafetyRatingValues = ["low", "medium", "high", "critical"];

    public static async Task<IReadOnlyList<SeededCriterion>> SeedCriteriaAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        IScale scale, Faker faker)
    {
        var count = Math.Min(scale.Criteria, Pool.Count);
        // Pick a deterministic subset: shuffle a copy of the pool indices, take <count>.
        var indices = Enumerable.Range(0, Pool.Count).ToList();
        indices = [.. indices.OrderBy(_ => faker.Random.Int())];
        var selected = indices.Take(count).Select(i => Pool[i]).ToList();

        var seeded = new List<SeededCriterion>(count);
        var now = DateTime.UtcNow;

        using (var writer = await conn.BeginBinaryImportAsync(
            "COPY public.criteria (id, name, data_type, description, unit, enum_values, created_at, updated_at) FROM STDIN (FORMAT BINARY)"))
        {
            _ = tx;
            foreach (var (name, dataType, unit, description, _) in selected)
            {
                var id = Guid.NewGuid();
                var enumValues = dataType == "Enum" ? EnumValuesFor(name) : null;

                await writer.StartRowAsync();
                await writer.WriteAsync(id, NpgsqlDbType.Uuid);
                await writer.WriteAsync(name, NpgsqlDbType.Varchar);
                await writer.WriteAsync(dataType, NpgsqlDbType.Varchar);
                if (description is not null)
                    await writer.WriteAsync(description, NpgsqlDbType.Text);
                else
                    await writer.WriteNullAsync();
                if (unit is not null)
                    await writer.WriteAsync(unit, NpgsqlDbType.Varchar);
                else
                    await writer.WriteNullAsync();
                if (enumValues is not null)
                    await writer.WriteAsync(System.Text.Json.JsonSerializer.Serialize(enumValues), NpgsqlDbType.Jsonb);
                else
                    await writer.WriteNullAsync();
                await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
                await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);

                seeded.Add(new SeededCriterion(id, name));
            }
            await writer.CompleteAsync();
        }

        // Seed criterion_resource_types — resolve resource type IDs first.
        var resourceTypeIds = await ResolveResourceTypeIdsAsync(conn, tx);

        using (var writer = await conn.BeginBinaryImportAsync(
            "COPY public.criterion_resource_types (criterion_id, resource_type_id) FROM STDIN (FORMAT BINARY)"))
        {
            for (var i = 0; i < selected.Count; i++)
            {
                var (_, _, _, _, keys) = selected[i];
                var criterionId = seeded[i].Id;

                foreach (var key in keys)
                {
                    if (!resourceTypeIds.TryGetValue(key, out var rtId)) continue;
                    await writer.StartRowAsync();
                    await writer.WriteAsync(criterionId, NpgsqlDbType.Uuid);
                    await writer.WriteAsync(rtId, NpgsqlDbType.Uuid);
                }
            }
            await writer.CompleteAsync();
        }

        return seeded;
    }

    private static async Task<Dictionary<string, Guid>> ResolveResourceTypeIdsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        var result = new Dictionary<string, Guid>();
        await using var cmd = new NpgsqlCommand(
            "SELECT key, id FROM public.resource_types WHERE key IN ('space', 'person')",
            conn, tx);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result[reader.GetString(0)] = reader.GetGuid(1);
        return result;
    }

    private static string[] EnumValuesFor(string name) => name switch
    {
        "Noise_Level" => NoiseEnumValues,
        "Certification_Level" => CertLevelValues,
        "Security_Clearance" => ClearanceValues,
        "Safety_Rating" => SafetyRatingValues,
        _ => ["option_a", "option_b", "option_c"],
    };
}
