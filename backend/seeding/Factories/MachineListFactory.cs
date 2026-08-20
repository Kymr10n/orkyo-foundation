using System.Text.Json;
using Bogus;
using Npgsql;

namespace Orkyo.Foundation.Seed.Factories;

/// <summary>
/// Seeds the tenant-defined lists the machines use, and the custom fields that bind to them.
///
/// Two bindings exist and the demo needs both, because they answer different questions:
/// <list type="bullet">
///   <item>a <c>list_lookup</c> field points at one <b>shared</b> instance — every mill picks its
///   tooling out of the same catalogue, so editing a tool's diameter changes it everywhere;</item>
///   <item>a <c>list</c> field gives each resource its <b>own</b> rows — one machine's maintenance
///   log has nothing to do with another's.</item>
/// </list>
/// Row counts are small, so these are plain INSERTs inside the run's transaction rather than COPY.
/// </summary>
public static class MachineListFactory
{
    public sealed record SeededLists(
        Guid ToolingDefinitionId,
        Guid ToolingInstanceId,
        IReadOnlyList<Guid> ToolingRowIds,
        Guid MaintenanceDefinitionId,
        Guid ConsumablesInstanceId,
        IReadOnlyList<Guid> ConsumablesRowIds);

    /// <summary>Field ids by (resource type key, field key), so value docs and per-resource list
    /// instances can find the field they belong to.</summary>
    public sealed record SeededFields(IReadOnlyDictionary<(string TypeKey, string FieldKey), Guid> Ids)
    {
        public Guid this[string typeKey, string fieldKey] => Ids[(typeKey, fieldKey)];
    }

    private const string MaintenanceLogField = "maintenance_log";
    private const string ToolingField = "tooling";

    /// <summary>The tooling a machine can be fitted with. Deterministic literals — this is a
    /// catalogue, and a catalogue that changed between runs would make the lookup ids meaningless.</summary>
    private static readonly (string Item, decimal Diameter, string Material, bool InStock)[] ToolingRows =
    [
        ("End mill 6mm",        6m,  "Carbide", true),
        ("End mill 10mm",      10m,  "Carbide", true),
        ("End mill 16mm",      16m,  "HSS",     true),
        ("Ball nose 8mm",       8m,  "Carbide", true),
        ("Face mill 50mm",     50m,  "Carbide", false),
        ("Slot drill 12mm",    12m,  "Cobalt",  true),
        ("Twist drill 5mm",     5m,  "HSS",     true),
        ("Twist drill 8.5mm",   8.5m, "HSS",    true),
        ("Reamer 10mm",        10m,  "Cobalt",  false),
        ("Chamfer tool 90deg", 12m,  "Carbide", true),
    ];

    /// <summary>The fabrication shop's own catalogue — same shape, different contents.</summary>
    private static readonly (string Item, decimal Diameter, string Material, bool InStock)[] ConsumableRows =
    [
        ("Grinding disc 125mm", 125m, "Carbide", true),
        ("Cutting disc 230mm",  230m, "Carbide", true),
        ("Flap disc 115mm",     115m, "HSS",     true),
        ("Wire brush cup",       75m, "HSS",     false),
        ("Deburring blade",      10m, "Cobalt",  true),
    ];

    public static async Task<SeededLists> SeedDefinitionsAsync(NpgsqlConnection conn)
    {
        var now = DateTime.UtcNow;

        // ── Shared: the tooling catalogue every machine picks from ────────────────
        var toolingDefId = await ListSeedHelpers.InsertDefinitionAsync(conn, "Tooling Catalog",
            "Cutting tools available across the shop. Machines reference these rather than keeping their own copies.", now);

        var toolingItem = await ListSeedHelpers.InsertColumnAsync(conn, toolingDefId, "item", "Item", "text", required: true, sort: 0, now);
        await ListSeedHelpers.InsertColumnAsync(conn, toolingDefId, "diameter_mm", "Diameter (mm)", "number", required: false, sort: 1, now);
        await ListSeedHelpers.InsertColumnAsync(conn, toolingDefId, "material", "Material", "select",
            required: false, sort: 2, now,
            optionsJson: JsonSerializer.Serialize(new[] { "HSS", "Carbide", "Cobalt" }));
        await ListSeedHelpers.InsertColumnAsync(conn, toolingDefId, "in_stock", "In stock", "boolean", required: false, sort: 3, now);
        // The column that names a row wherever one is shown as a single value.
        await ListSeedHelpers.SetDisplayColumnAsync(conn, toolingDefId, toolingItem);

        var toolingInstanceId = await ListSeedHelpers.InsertSharedInstanceAsync(conn, toolingDefId, "Standard Tooling", now);

        var rowIds = new List<Guid>(ToolingRows.Length);
        foreach (var (item, diameter, material, inStock) in ToolingRows)
        {
            var values = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["item"] = item,
                ["diameter_mm"] = diameter,
                ["material"] = material,
                ["in_stock"] = inStock,
            });
            rowIds.Add(await ListSeedHelpers.InsertRowAsync(conn, toolingInstanceId, values, now));
        }

        // ── Per-resource: each machine keeps its own maintenance history ──────────
        var maintenanceDefId = await ListSeedHelpers.InsertDefinitionAsync(conn, "Maintenance Log",
            "What was done to a machine and when. Each machine keeps its own entries.", now);

        await ListSeedHelpers.InsertColumnAsync(conn, maintenanceDefId, "date", "Date", "date", required: true, sort: 0, now);
        var workColumn = await ListSeedHelpers.InsertColumnAsync(conn, maintenanceDefId, "work", "Work", "select",
            required: false, sort: 1, now,
            optionsJson: JsonSerializer.Serialize(new[] { "Inspection", "Repair", "Calibration", "Overhaul" }));
        await ListSeedHelpers.InsertColumnAsync(conn, maintenanceDefId, "technician", "Technician", "text", required: false, sort: 2, now);
        await ListSeedHelpers.InsertColumnAsync(conn, maintenanceDefId, "downtime_h", "Downtime (h)", "number", required: false, sort: 3, now);
        await ListSeedHelpers.InsertColumnAsync(conn, maintenanceDefId, "resolved", "Resolved", "boolean", required: false, sort: 4, now);
        await ListSeedHelpers.SetDisplayColumnAsync(conn, maintenanceDefId, workColumn);

        // A second catalogue of the same shape. One definition, two instances — the split exists
        // precisely so the fabrication shop's consumables are not the machine shop's tooling, and a
        // demo with a single instance never shows why the two concepts are separate.
        var consumablesInstanceId = await ListSeedHelpers.InsertSharedInstanceAsync(conn, toolingDefId, "Fabrication Consumables", now);
        var consumableRowIds = new List<Guid>(ConsumableRows.Length);
        foreach (var (item, diameter, material, inStock) in ConsumableRows)
        {
            var values = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["item"] = item,
                ["diameter_mm"] = diameter,
                ["material"] = material,
                ["in_stock"] = inStock,
            });
            consumableRowIds.Add(await ListSeedHelpers.InsertRowAsync(conn, consumablesInstanceId, values, now));
        }

        return new SeededLists(toolingDefId, toolingInstanceId, rowIds, maintenanceDefId,
            consumablesInstanceId, consumableRowIds);
    }

    /// <summary>
    /// The custom fields each machine type carries. Scalars describe the machine; the two list
    /// fields bind to the definitions above.
    /// </summary>
    public static async Task<SeededFields> SeedFieldsAsync(
        NpgsqlConnection conn, IReadOnlyDictionary<string, Guid> typeIds, SeededLists lists)
    {
        var now = DateTime.UtcNow;
        var ids = new Dictionary<(string, string), Guid>();

        // Records the id so SeedMaintenanceHistoryAsync can find the per-resource field later.
        async Task Field(string typeKey, string key, string label, string dataType,
            bool required = false, int sort = 0, Guid? definitionId = null, Guid? instanceId = null)
        {
            var id = await ListSeedHelpers.InsertCustomFieldAsync(
                conn, typeIds[typeKey], key, label, dataType, required, sort, now,
                definitionId, instanceId);
            if (id is not null) ids[(typeKey, key)] = id.Value;
        }

        // Scalar fields come from the type spec, which mirrors the product's own type catalog —
        // so a seeded mill and a mill someone activates from the catalog agree on every field key.
        foreach (var type in Narrative.MachineCatalog.Types)
        {
            foreach (var field in type.Fields)
                await Field(type.Key, field.Key, field.Label, field.DataType, sort: field.SortOrder);
        }

        // The two list-bound fields are the seeder's own: the catalog cannot ship a field that
        // points at a list definition, because the definition does not exist until a workspace
        // makes one. ResourceTypeCatalogParityTests whitelists exactly these two.
        foreach (var type in Narrative.MachineCatalog.Types)
        {
            if (type.Key is "mill" or "drill" or "lathe" or "cnc")
            {
                await Field(type.Key, ToolingField, "Fitted tooling", "list_lookup",
                    sort: 60, instanceId: lists.ToolingInstanceId);
            }
            await Field(type.Key, MaintenanceLogField, "Maintenance log", "list",
                sort: 70, definitionId: lists.MaintenanceDefinitionId);
        }

        return new SeededFields(ids);
    }

    /// <summary>
    /// The value document per machine code, ready for the resource insert. A <c>list</c> field never
    /// appears here — its rows hang off the resource, not off the value document — while a
    /// <c>list_lookup</c> value is an array of row ids from the shared instance.
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildValueDocuments(
        IEnumerable<Narrative.MachineSpec> machines, SeededLists lists, Faker faker)
    {
        var docs = new Dictionary<string, string>();

        foreach (var machine in machines)
        {
            var values = new Dictionary<string, object>();

            switch (machine.TypeKey)
            {
                case "mill":
                    values["axis_count"] = faker.PickRandom(3, 3, 4, 5);
                    values["spindle_speed_max"] = faker.PickRandom(8000, 10000, 12000, 15000);
                    values["table_size"] = faker.PickRandom("800 × 400 mm", "1100 × 500 mm", "1300 × 600 mm");
                    values["tool_changer_positions"] = faker.PickRandom(20, 24, 30);
                    values["max_workpiece_weight"] = faker.PickRandom(300, 500, 800);
                    values[ToolingField] = PickTooling(lists.ToolingRowIds, faker);
                    break;

                case "drill":
                    values["spindle_speed_max"] = faker.PickRandom(1500, 2200, 3000);
                    values["drilling_capacity_steel"] = faker.PickRandom(16, 20, 25, 32);
                    values["spindle_taper"] = faker.PickRandom("MT2", "MT3", "MT4");
                    values["coolant_system"] = faker.Random.Bool(0.6f);
                    values["last_service"] = ServiceDate(faker);
                    values[ToolingField] = PickTooling(lists.ToolingRowIds, faker);
                    break;

                case "lathe":
                    values["swing_over_bed"] = faker.PickRandom(330, 400, 500);
                    values["distance_between_centers"] = faker.PickRandom(650, 1000, 1500);
                    values["spindle_bore"] = faker.PickRandom(38, 52);
                    values["spindle_speed_max"] = faker.PickRandom(2000, 3000, 4500);
                    values["live_tooling"] = faker.Random.Bool(0.4f);
                    values[ToolingField] = PickTooling(lists.ToolingRowIds, faker);
                    break;

                case "cnc":
                    values["controller"] = faker.PickRandom("Fanuc 0i-MF", "Siemens 840D", "Heidenhain TNC 640");
                    values["axis_count"] = faker.PickRandom(4, 5, 5);
                    values["program_storage_mb"] = faker.PickRandom(512, 1024, 2048, 4096);
                    values["maintenance_contract_until"] = ContractDate(faker);
                    values["documentation_url"] = "https://example.com/manuals/5-axis";
                    values[ToolingField] = PickTooling(lists.ToolingRowIds, faker);
                    break;

                case "assembly_station":
                    values["bench_length_m"] = faker.PickRandom(2, 3, 4);
                    values["max_bench_load_kg"] = faker.PickRandom(150, 300, 500);
                    values["esd_protected"] = faker.Random.Bool(0.5f);
                    values["compressed_air"] = faker.Random.Bool(0.7f);
                    break;

                case "test_station":
                    values["test_types"] = faker.PickRandom(
                        "ICT, functional", "leak, hipot", "functional, burn-in");
                    values["calibrated_until"] = ContractDate(faker);
                    values["calibration_certificate_url"] = "https://example.com/certs/test-rig";
                    values["climate_controlled"] = faker.Random.Bool(0.5f);
                    break;
            }

            docs[machine.Code] = JsonSerializer.Serialize(values);
        }

        return docs;
    }

    /// <summary>Gives a few machines a maintenance history, so the per-resource binding is visible
    /// in the demo rather than merely defined.</summary>
    public static async Task<int> SeedMaintenanceHistoryAsync(
        NpgsqlConnection conn,
        IReadOnlyList<MachineFactory.SeededMachine> machines,
        SeededFields fields,
        Faker faker)
    {
        var now = DateTime.UtcNow;
        // Most machines, not a hand-picked four: a log that exists on one site's mills only reads
        // as a feature nobody uses. Every fifth machine in catalog order is left without one,
        // because a shop that has serviced everything is its own kind of unrealistic.
        var withHistory = machines
            .Where((_, index) => index % 5 != 0)
            .Select(m => m.Code)
            .ToHashSet(StringComparer.Ordinal);
        var rows = 0;

        foreach (var machine in machines.Where(m => withHistory.Contains(m.Code)))
        {
            var fieldId = fields[machine.TypeKey, MaintenanceLogField];

            // kind='resource' carries the resource and field instead of a name — the shape CHECK
            // enforces the pairing, and (resource_id, field_id) is unique, so one instance per
            // machine per field.
            var instanceId = Guid.NewGuid();
            await using (var cmd = new NpgsqlCommand(
                "INSERT INTO public.list_instances (id, list_definition_id, kind, resource_id, field_id, created_at, updated_at) " +
                "SELECT @id, list_definition_id, 'resource', @resourceId, @fieldId, @now, @now " +
                "FROM public.resource_custom_fields WHERE id = @fieldId", conn))
            {
                cmd.Parameters.AddWithValue("id", instanceId);
                cmd.Parameters.AddWithValue("resourceId", machine.Id);
                cmd.Parameters.AddWithValue("fieldId", fieldId);
                cmd.Parameters.AddWithValue("now", now);
                await cmd.ExecuteNonQueryAsync();
            }

            var entries = faker.Random.Int(3, 5);
            for (var i = 0; i < entries; i++)
            {
                var values = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["date"] = ServiceDate(faker),
                    ["work"] = faker.PickRandom("Inspection", "Repair", "Calibration", "Overhaul"),
                    ["technician"] = faker.PickRandom("R. Alvarez", "T. Fischer", "M. Okafor", "S. Lindqvist"),
                    ["downtime_h"] = faker.Random.Int(1, 12),
                    ["resolved"] = faker.Random.Bool(0.85f),
                });
                await ListSeedHelpers.InsertRowAsync(conn, instanceId, values, now);
                rows++;
            }
        }

        return rows;
    }

    /// <summary>A date some months ahead — a contract or calibration that has not lapsed yet.</summary>
    private static string ContractDate(Faker faker) =>
        DateTime.UtcNow.AddDays(faker.Random.Int(60, 400)).ToString("yyyy-MM-dd");

    private static string ServiceDate(Faker faker) =>
        DateTime.UtcNow.Date.AddDays(-faker.Random.Int(10, 300)).ToString("yyyy-MM-dd");

    private static List<string> PickTooling(IReadOnlyList<Guid> rowIds, Faker faker) =>
        faker.PickRandom(rowIds, faker.Random.Int(2, 4)).Select(id => id.ToString()).ToList();
}
