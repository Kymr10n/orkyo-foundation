using Npgsql;
using NpgsqlTypes;
using Orkyo.Foundation.Seed.Narrative;

namespace Orkyo.Foundation.Seed.Factories;

/// <summary>
/// Seeds the shop-floor machines: tenant-defined resource types that declare geometry, and the
/// machines themselves placed inside the floorplan rooms.
///
/// Nothing here is a system type. These are built exactly the way a tenant builds their own —
/// which is the reason to seed them at all, since a demo that only shows the built-in types proves
/// nothing about the model.
/// </summary>
public static class MachineFactory
{
    public sealed record SeededMachine(
        Guid Id, string TypeKey, string Role, string SiteCode, string Name, string Code,
        /// <summary>False for a machine that is owned but has never been drawn on the plan.</summary>
        bool Placed);

    /// <summary>
    /// Creates the machine resource types and returns their ids by key. Plain INSERTs rather than a
    /// COPY — three rows, and the ids are needed immediately.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, Guid>> SeedTypesAsync(NpgsqlConnection conn)
    {
        var now = DateTime.UtcNow;
        var ids = new Dictionary<string, Guid>();

        foreach (var spec in MachineCatalog.Types)
        {
            var id = Guid.NewGuid();
            // Adopt a type the workspace already has rather than failing on its key. Append mode
            // runs over a database that may hold these already — and so does any workspace that
            // activated one of them from the type catalog first. Adoption reactivates and changes
            // nothing else, which is the rule catalog activation follows: the row is the tenant's,
            // renames and edits included.
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO public.resource_types " +
                "(id, key, display_name, display_name_plural, description, icon, is_system, is_active, has_geometry, created_at, updated_at) " +
                "VALUES (@id, @key, @name, @plural, @description, @icon, false, true, true, @now, @now) " +
                "ON CONFLICT (key) DO UPDATE SET is_active = true, updated_at = @now " +
                "RETURNING id", conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("key", spec.Key);
            cmd.Parameters.AddWithValue("name", spec.DisplayName);
            cmd.Parameters.AddWithValue("plural", spec.DisplayNamePlural);
            cmd.Parameters.AddWithValue("description", spec.Description);
            cmd.Parameters.AddWithValue("icon", spec.Icon);
            cmd.Parameters.AddWithValue("now", now);
            ids[spec.Key] = (Guid)(await cmd.ExecuteScalarAsync())!;
        }

        return ids;
    }

    /// <summary>
    /// Places every machine in <see cref="MachineCatalog"/> on its site's floorplan.
    /// <paramref name="customFieldDocs"/> is the value document per machine code, built by the
    /// caller once the field definitions and list rows exist.
    /// </summary>
    public static async Task<IReadOnlyList<SeededMachine>> SeedMachinesAsync(
        NpgsqlConnection conn,
        IReadOnlyList<SpaceFactories.SeededSite> sites,
        IReadOnlyDictionary<string, Guid> typeIds,
        IReadOnlyDictionary<string, string> customFieldDocs)
    {
        var now = DateTime.UtcNow;
        var siteIdByCode = sites.ToDictionary(s => s.Code, s => s.Id);

        // Same guard as ToolFactory: the catalog and the site list are built by different paths, so
        // a code that does not line up is a seed-data bug worth naming.
        var unknown = MachineCatalog.All.Select(m => m.SiteCode).Distinct()
            .Where(c => !siteIdByCode.ContainsKey(c)).ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidOperationException(
                $"Machines reference site codes with no seeded site: {string.Join(", ", unknown)}. "
                + $"Known codes: {string.Join(", ", siteIdByCode.Keys)}.");
        }

        var machines = new List<SeededMachine>(MachineCatalog.All.Count);

        using var writer = await conn.BeginBinaryImportAsync(
            "COPY public.resources (id, resource_type_id, name, allocation_mode, base_availability_percent, is_active, " +
            "home_site_id, cross_site_allowed, code, is_physical, geometry, properties, capacity, custom_fields, created_at, updated_at) " +
            "FROM STDIN (FORMAT BINARY)");

        foreach (var spec in MachineCatalog.All)
        {
            var id = Guid.NewGuid();
            await writer.StartRowAsync();
            await writer.WriteAsync(id, NpgsqlDbType.Uuid);
            await writer.WriteAsync(typeIds[spec.TypeKey], NpgsqlDbType.Uuid);
            await writer.WriteAsync(spec.Name, NpgsqlDbType.Varchar);
            await writer.WriteAsync("Exclusive", NpgsqlDbType.Varchar);
            await writer.WriteAsync(100, NpgsqlDbType.Integer);
            await writer.WriteAsync(true, NpgsqlDbType.Boolean);
            await writer.WriteAsync(siteIdByCode[spec.SiteCode], NpgsqlDbType.Uuid);
            // A machine is bolted to its floor. Letting it travel would put it on another site's plan.
            await writer.WriteAsync(false, NpgsqlDbType.Boolean);
            await writer.WriteAsync(spec.Code, NpgsqlDbType.Varchar);
            // A machine with no shape is owned but not placed — the floorplan's
            // place-an-existing-resource flow is what puts it on the plan. It is not physical
            // until then: `resources_physical_has_geometry_check` reads physical as "occupies
            // floor area", and something with no footprint occupies none.
            await writer.WriteAsync(spec.Geometry is not null, NpgsqlDbType.Boolean);
            if (spec.Geometry is null)
                await writer.WriteNullAsync();
            else
                await writer.WriteAsync(GeometryJson(spec.Geometry), NpgsqlDbType.Jsonb);
            await writer.WriteAsync("{}", NpgsqlDbType.Jsonb);
            await writer.WriteAsync(1, NpgsqlDbType.Integer);
            await writer.WriteAsync(customFieldDocs.GetValueOrDefault(spec.Code, "{}"), NpgsqlDbType.Jsonb);
            await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
            await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);

            machines.Add(new SeededMachine(
                id, spec.TypeKey, spec.Role, spec.SiteCode, spec.Name, spec.Code, spec.Geometry is not null));
        }

        await writer.CompleteAsync();
        return machines;
    }

    /// <summary>
    /// Groups the machines into cells. Groups are typed, so a cell belongs to exactly one machine
    /// type — "Machining Cell A" is a group of mills and could not hold a drill even if someone
    /// tried. Display order follows the catalog so the sidebar reads in floor order.
    /// </summary>
    public static async Task<(int Groups, int Members)> SeedGroupsAsync(
        NpgsqlConnection conn,
        IReadOnlyDictionary<string, Guid> typeIds,
        IReadOnlyList<SeededMachine> machines)
    {
        var now = DateTime.UtcNow;
        var machineById = MachineCatalog.All.ToDictionary(m => m.Code, m => m);

        // (type key, group name) in catalog order, so the ids line up with how the floor reads.
        var cells = MachineCatalog.All
            .Select(m => (m.TypeKey, m.GroupName))
            .Distinct()
            .ToList();

        var groupIds = new Dictionary<(string TypeKey, string GroupName), Guid>();

        using (var writer = await conn.BeginBinaryImportAsync(
            "COPY public.resource_groups (id, name, description, color, display_order, resource_type_id, created_at, updated_at) " +
            "FROM STDIN (FORMAT BINARY)"))
        {
            for (var i = 0; i < cells.Count; i++)
            {
                var id = Guid.NewGuid();
                groupIds[cells[i]] = id;
                await writer.StartRowAsync();
                await writer.WriteAsync(id, NpgsqlDbType.Uuid);
                await writer.WriteAsync(cells[i].GroupName, NpgsqlDbType.Varchar);
                await writer.WriteNullAsync();                              // description
                await writer.WriteNullAsync();                              // color
                await writer.WriteAsync(i, NpgsqlDbType.Integer);
                await writer.WriteAsync(typeIds[cells[i].TypeKey], NpgsqlDbType.Uuid);
                await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
                await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
            }
            await writer.CompleteAsync();
        }

        var members = 0;
        using (var writer = await conn.BeginBinaryImportAsync(
            "COPY public.resource_group_members (resource_group_id, resource_id, resource_type_id) " +
            "FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var machine in machines)
            {
                var spec = machineById[machine.Code];
                await writer.StartRowAsync();
                await writer.WriteAsync(groupIds[(spec.TypeKey, spec.GroupName)], NpgsqlDbType.Uuid);
                await writer.WriteAsync(machine.Id, NpgsqlDbType.Uuid);
                await writer.WriteAsync(typeIds[spec.TypeKey], NpgsqlDbType.Uuid);
                members++;
            }
            await writer.CompleteAsync();
        }

        return (cells.Count, members);
    }

    /// <summary>
    /// PascalCase, matching how the API serializes <c>ResourceGeometry</c> — see
    /// <see cref="FloorplanFactory.RectangleGeometryJson"/>. A circle is its centre and one point on
    /// the rim, so the radius is the distance between the two.
    /// </summary>
    internal static string GeometryJson(MachineGeometry geometry) => geometry switch
    {
        MachineGeometry.Rect r =>
            $$"""{"Type":"rectangle","Coordinates":[{"X":{{r.X}},"Y":{{r.Y}}},{"X":{{r.X + r.W}},"Y":{{r.Y + r.H}}}]}""",
        MachineGeometry.Circle c =>
            $$"""{"Type":"circle","Coordinates":[{"X":{{c.CentreX}},"Y":{{c.CentreY}}},{"X":{{c.CentreX + c.Radius}},"Y":{{c.CentreY}}}]}""",
        _ => throw new InvalidOperationException($"Unhandled machine geometry {geometry.GetType().Name}."),
    };
}
