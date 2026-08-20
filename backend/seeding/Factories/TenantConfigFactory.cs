using System.Text.Json;
using Bogus;
using Npgsql;
using NpgsqlTypes;

namespace Orkyo.Foundation.Seed.Factories;

/// <summary>
/// The configuration a shop floor actually runs on: when each site works, the criteria templates
/// planners reuse, and the capabilities a whole team holds rather than one person.
///
/// These were the demo's blind spots — every table here was empty after a full seed, which meant
/// the utilization grid drew a 24/7 week and the template picker opened onto nothing.
/// </summary>
public static class TenantConfigFactory
{
    /// <summary>
    /// Working hours per site. Without a row the grid has no shape — no shaded evenings, no
    /// weekend, no holiday region — so the year of work reads as though it ran around the clock.
    /// </summary>
    public static async Task<int> SeedSchedulingSettingsAsync(
        NpgsqlConnection conn, IReadOnlyList<SpaceFactories.SeededSite> sites)
    {
        var now = DateTime.UtcNow;

        using var writer = await conn.BeginBinaryImportAsync(
            "COPY public.scheduling_settings (id, site_id, time_zone, working_hours_enabled, " +
            "working_day_start, working_day_end, weekends_enabled, public_holidays_enabled, " +
            "public_holiday_region, created_at, updated_at) FROM STDIN (FORMAT BINARY)");

        foreach (var site in sites)
        {
            await writer.StartRowAsync();
            await writer.WriteAsync(Guid.NewGuid(), NpgsqlDbType.Uuid);
            await writer.WriteAsync(site.Id, NpgsqlDbType.Uuid);
            await writer.WriteAsync("Europe/Berlin", NpgsqlDbType.Varchar);
            await writer.WriteAsync(true, NpgsqlDbType.Boolean);
            // A two-shift day. The narrative's jobs run 06:00–18:00, so the shaded hours line up
            // with where the work actually is rather than boxing it in.
            await writer.WriteAsync(new TimeSpan(6, 0, 0), NpgsqlDbType.Time);
            await writer.WriteAsync(new TimeSpan(18, 0, 0), NpgsqlDbType.Time);
            await writer.WriteAsync(false, NpgsqlDbType.Boolean);   // weekends off
            await writer.WriteAsync(true, NpgsqlDbType.Boolean);
            await writer.WriteAsync("DE", NpgsqlDbType.Varchar);
            await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
            await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
        }

        await writer.CompleteAsync();
        return sites.Count;
    }

    /// <summary>
    /// Criteria templates — a saved bundle of requirements a planner applies instead of picking the
    /// same skills by hand each time. Distinct from request templates, which carry timing.
    /// </summary>
    public static async Task<int> SeedCriteriaTemplatesAsync(
        NpgsqlConnection conn, IReadOnlyDictionary<string, Guid> criteria)
    {
        var now = DateTime.UtcNow;

        // (name, entity type, duration, the skills the bundle asks for)
        (string Name, string Entity, int? Hours, string[] Skills)[] specs =
        [
            ("Machining job", "request", 8, [Narrative.SkillCatalog.CncOperation]),
            ("Assembly job", "request", 6, [Narrative.SkillCatalog.Assembly]),
            ("Quality audit", "request", 4, [Narrative.SkillCatalog.QaInspection]),
            // 'space' here is templates.entity_type — a separate vocabulary with its own CHECK
            // ('request' | 'space' | 'group'), unrelated to resource_types.key.
            ("Clean room", "space", null, [Narrative.SkillCatalog.CleanRoom]),
            ("Welding crew", "group", null, [Narrative.SkillCatalog.WeldingCert, Narrative.SkillCatalog.Grinding]),
        ];

        var items = 0;
        foreach (var spec in specs)
        {
            var id = Guid.NewGuid();
            await using (var cmd = new NpgsqlCommand(
                "INSERT INTO public.templates " +
                "(id, name, description, entity_type, duration_value, duration_unit, " +
                " fixed_start, fixed_end, fixed_duration, created_at, updated_at) " +
                "VALUES (@id, @name, @description, @entity, @duration, @unit, false, false, @fixedDuration, @now, @now)", conn))
            {
                cmd.Parameters.AddWithValue("id", id);
                cmd.Parameters.AddWithValue("name", spec.Name);
                cmd.Parameters.AddWithValue("description", $"Reusable {spec.Entity} template.");
                cmd.Parameters.AddWithValue("entity", spec.Entity);
                cmd.Parameters.AddWithValue("duration", (object?)spec.Hours ?? DBNull.Value);
                cmd.Parameters.AddWithValue("unit", spec.Hours is null ? DBNull.Value : "hours");
                cmd.Parameters.AddWithValue("fixedDuration", spec.Hours is not null);
                cmd.Parameters.AddWithValue("now", now);
                await cmd.ExecuteNonQueryAsync();
            }

            foreach (var skill in spec.Skills.Where(criteria.ContainsKey))
            {
                await using var itemCmd = new NpgsqlCommand(
                    "INSERT INTO public.template_items (id, template_id, criterion_id, value, created_at, updated_at) " +
                    "VALUES (@id, @templateId, @criterionId, @value, @now, @now)", conn);
                itemCmd.Parameters.AddWithValue("id", Guid.NewGuid());
                itemCmd.Parameters.AddWithValue("templateId", id);
                itemCmd.Parameters.AddWithValue("criterionId", criteria[skill]);
                itemCmd.Parameters.Add(new NpgsqlParameter("value", NpgsqlDbType.Jsonb) { Value = "true" });
                itemCmd.Parameters.AddWithValue("now", now);
                await itemCmd.ExecuteNonQueryAsync();
                items++;
            }
        }

        return items;
    }

    /// <summary>
    /// Capabilities held by a group rather than a resource — "this crew is welding-certified" said
    /// once, instead of on every member.
    /// </summary>
    public static async Task<int> SeedGroupCapabilitiesAsync(
        NpgsqlConnection conn, IReadOnlyDictionary<string, Guid> criteria)
    {
        var now = DateTime.UtcNow;
        var rows = 0;

        // Person groups are named after the skill that defines them, so the group's capability is
        // exactly the skill its name claims.
        (string GroupNameLike, string Skill)[] pairs =
        [
            ("%Weld%", Narrative.SkillCatalog.WeldingCert),
            ("%Quality%", Narrative.SkillCatalog.QaInspection),
            ("%Assembly%", Narrative.SkillCatalog.Assembly),
            ("%Maintenance%", Narrative.SkillCatalog.Maintenance),
        ];

        foreach (var (pattern, skill) in pairs)
        {
            if (!criteria.TryGetValue(skill, out var criterionId)) continue;

            await using var cmd = new NpgsqlCommand(
                "INSERT INTO public.resource_group_capabilities (id, resource_group_id, criterion_id, value, created_at, updated_at) " +
                "SELECT gen_random_uuid(), g.id, @criterionId, @value, @now, @now " +
                "FROM public.resource_groups g WHERE g.name ILIKE @pattern " +
                "ON CONFLICT DO NOTHING", conn);
            cmd.Parameters.AddWithValue("criterionId", criterionId);
            cmd.Parameters.Add(new NpgsqlParameter("value", NpgsqlDbType.Jsonb)
            {
                Value = Narrative.SkillCatalog.ByKey(skill).DataType == "Enum" ? "\"MIG\"" : "true",
            });
            cmd.Parameters.AddWithValue("pattern", pattern);
            cmd.Parameters.AddWithValue("now", now);
            rows += await cmd.ExecuteNonQueryAsync();
        }

        return rows;
    }

    /// <summary>
    /// Custom fields on the built-in types, so the demo shows what a tenant can add to people,
    /// rooms and tools — not only to the machine types the seed invents for itself.
    /// The tool type's consumables field binds to a second shared list, which is what makes the
    /// definition/instance split visible: one shape, two catalogues.
    /// </summary>
    public static async Task<(int Fields, int Values)> SeedBuiltInCustomFieldsAsync(
        NpgsqlConnection conn, Guid consumablesInstanceId,
        IReadOnlyList<Guid> consumablesRowIds, Faker faker)
    {
        var now = DateTime.UtcNow;
        var typeIds = new Dictionary<string, Guid>();
        await using (var cmd = new NpgsqlCommand(
            "SELECT key, id FROM public.resource_types WHERE key IN ('person','room','tool')", conn))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) typeIds[reader.GetString(0)] = reader.GetGuid(1);
        }

        var fields = 0;
        async Task Field(string typeKey, string key, string label, string dataType,
            int sort, Guid? instanceId = null)
        {
            if (!typeIds.TryGetValue(typeKey, out var typeId)) return;
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO public.resource_custom_fields " +
                "(id, resource_type_id, key, label, data_type, is_required, sort_order, is_active, " +
                " list_instance_id, created_at, updated_at) " +
                "VALUES (@id, @typeId, @key, @label, @dataType, false, @sort, true, @instId, @now, @now)", conn);
            cmd.Parameters.AddWithValue("id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("typeId", typeId);
            cmd.Parameters.AddWithValue("key", key);
            cmd.Parameters.AddWithValue("label", label);
            cmd.Parameters.AddWithValue("dataType", dataType);
            cmd.Parameters.AddWithValue("sort", sort);
            cmd.Parameters.AddWithValue("instId", (object?)instanceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("now", now);
            await cmd.ExecuteNonQueryAsync();
            fields++;
        }

        // Nothing here is required: a required field would block every create flow that sends
        // no values. The person and tool fields mirror the ResourceTypeCatalog entries in
        // backend/core (the seeding project carries no reference to core), so a seeded tenant
        // and a catalog activation agree on the same field keys.
        await Field("person", "badge_number", "Badge number", "text", 0);
        await Field("person", "certified_until", "Certified until", "date", 1);
        await Field("person", "night_shift", "Night shift", "boolean", 2);

        await Field("room", "floor_area_m2", "Floor area (m²)", "number", 0);
        await Field("room", "has_extraction", "Fume extraction", "boolean", 1);

        await Field("tool", "serial_number", "Serial number", "text", 0);
        await Field("tool", "purchased_on", "Purchased", "date", 1);
        await Field("tool", "consumables", "Consumables", "list_lookup", 2, consumablesInstanceId);

        var values = await FillValuesAsync(conn, "person", faker, r => new Dictionary<string, object>
        {
            ["badge_number"] = $"B-{faker.Random.Int(1000, 9999)}",
            ["certified_until"] = DateTime.UtcNow.Date.AddDays(faker.Random.Int(30, 900)).ToString("yyyy-MM-dd"),
            ["night_shift"] = faker.Random.Bool(0.25f),
        });

        values += await FillValuesAsync(conn, "room", faker, r => new Dictionary<string, object>
        {
            ["floor_area_m2"] = faker.Random.Int(20, 400),
            ["has_extraction"] = faker.Random.Bool(0.3f),
        });

        // Tools reference consumables. The field and its shared list existed already, but nothing
        // ever pointed at a row — so the reason that second instance exists (one definition, two
        // instances) was invisible in the UI.
        values += await FillValuesAsync(conn, "tool", faker, r => new Dictionary<string, object>
        {
            ["serial_number"] = $"SN-{faker.Random.Int(100000, 999999)}",
            ["purchased_on"] = DateTime.UtcNow.Date.AddDays(-faker.Random.Int(200, 3000)).ToString("yyyy-MM-dd"),
            ["consumables"] = consumablesRowIds.Count == 0
                ? Array.Empty<string>()
                : faker.PickRandom(consumablesRowIds, Math.Min(3, consumablesRowIds.Count))
                    .Select(id => id.ToString()).ToArray(),
        });

        return (fields, values);
    }

    /// <summary>Writes a value document onto every resource of a type, one statement.</summary>
    private static async Task<int> FillValuesAsync(
        NpgsqlConnection conn, string typeKey, Faker faker,
        Func<Guid, Dictionary<string, object>> build)
    {
        var ids = new List<Guid>();
        await using (var cmd = new NpgsqlCommand(
            "SELECT r.id FROM public.resources r JOIN public.resource_types t ON t.id = r.resource_type_id " +
            "WHERE t.key = @key", conn))
        {
            cmd.Parameters.AddWithValue("key", typeKey);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) ids.Add(reader.GetGuid(0));
        }
        if (ids.Count == 0) return 0;

        var docs = ids.Select(id => JsonSerializer.Serialize(build(id))).ToArray();

        await using var update = new NpgsqlCommand(
            // Merge rather than replace. A person already carries their job-title and department
            // lookups by the time this runs, and assigning the document wholesale silently dropped
            // both — the fields were written, then overwritten a few statements later.
            "UPDATE public.resources r " +
            "SET custom_fields = COALESCE(r.custom_fields, '{}'::jsonb) || v.doc::jsonb " +
            "FROM (SELECT unnest(@ids) AS id, unnest(@docs) AS doc) v WHERE r.id = v.id", conn);
        update.Parameters.AddWithValue("ids", ids.ToArray());
        update.Parameters.AddWithValue("docs", docs);
        return await update.ExecuteNonQueryAsync();
    }
}
