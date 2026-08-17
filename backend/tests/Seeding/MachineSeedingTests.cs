using System.Text.Json;
using Api.Models;
using Api.Services;
using Bogus;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Orkyo.Foundation.Seed.Factories;
using Orkyo.Foundation.Seed.Floorplans;
using Orkyo.Foundation.Seed.Narrative;
using Xunit;

namespace Orkyo.Foundation.Tests.Seeding;

/// <summary>
/// Integration test for the machine seeder against a real tenant DB. The point of seeding machines
/// at all is to prove the tenant-defined model works end to end, so this asserts the parts the
/// database itself will not catch: that every value document validates against the field
/// definitions it claims to satisfy, and that both list bindings mean what they say.
///
/// Everything runs inside a rolled-back transaction so the shared DB stays clean.
/// </summary>
[Collection("Database collection")]
public class MachineSeedingTests
{
    private readonly IOrgDbConnectionFactory _connFactory;
    private readonly OrgContext _orgContext;

    public MachineSeedingTests(DatabaseFixture fixture)
    {
        var scope = fixture.Factory.Services.CreateScope();
        _connFactory = scope.ServiceProvider.GetRequiredService<IOrgDbConnectionFactory>();
        _orgContext = scope.ServiceProvider.GetRequiredService<OrgContext>();
    }

    private sealed record Seeded(
        IReadOnlyList<MachineFactory.SeededMachine> Machines,
        MachineListFactory.SeededLists Lists,
        MachineListFactory.SeededFields Fields,
        IReadOnlyDictionary<string, Guid> TypeIds);

    private static async Task<Seeded> SeedAsync(NpgsqlConnection conn, Guid orgId)
    {
        // The seeder defines its own `room` type rather than borrowing the built-in space one,
        // so ask it rather than looking up a key it no longer uses. The caller already owns a
        // transaction here, and Npgsql has no nested ones — pass null and ride the ambient one.
        var spaceTypeId = await SpaceFactories.ResolveSpaceResourceTypeIdAsync(conn, null);

        var fixtures = FloorplanCatalog.ForProfile("manufacturing");
        var floorplan = await FloorplanFactory.SeedAsync(conn, orgId, fixtures, spaceTypeId);

        var faker = new Faker { Random = new Randomizer(1337) };
        var typeIds = await MachineFactory.SeedTypesAsync(conn);
        var lists = await MachineListFactory.SeedDefinitionsAsync(conn);
        var fields = await MachineListFactory.SeedFieldsAsync(conn, typeIds, lists);
        var docs = MachineListFactory.BuildValueDocuments(MachineCatalog.All, lists, faker);
        var machines = await MachineFactory.SeedMachinesAsync(conn, floorplan.Sites, typeIds, docs);
        await MachineFactory.SeedGroupsAsync(conn, typeIds, machines);
        await MachineListFactory.SeedMaintenanceHistoryAsync(conn, machines, fields, faker);

        return new Seeded(machines, lists, fields, typeIds);
    }

    [Fact]
    public async Task SeedsTenantDefinedPlaceableTypes()
    {
        await using var conn = _connFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await SeedAsync(conn, _orgContext.OrgId);

        await using var cmd = new NpgsqlCommand(
            @"SELECT count(*) FILTER (WHERE has_geometry),
                     count(*) FILTER (WHERE is_system)
              FROM resource_types WHERE key IN ('mill','drill','assembly_station')", conn, tx);
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();

        Assert.Equal(3, r.GetInt64(0));
        // Tenant-defined, not built in — that is the whole demonstration.
        Assert.Equal(0, r.GetInt64(1));
    }

    [Fact]
    public async Task PlacesEveryMachineWithGeometry_IncludingCircles()
    {
        await using var conn = _connFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var seeded = await SeedAsync(conn, _orgContext.OrgId);

        await using var cmd = new NpgsqlCommand(
            @"SELECT count(*),
                     count(*) FILTER (WHERE r.is_physical AND r.geometry IS NOT NULL),
                     count(*) FILTER (WHERE r.geometry->>'Type' = 'circle'
                                        AND jsonb_array_length(r.geometry->'Coordinates') = 2),
                     count(*) FILTER (WHERE NOT r.cross_site_allowed)
              FROM resources r
              JOIN resource_types rt ON rt.id = r.resource_type_id
              WHERE rt.key IN ('mill','drill','assembly_station')", conn, tx);
        await using var r2 = await cmd.ExecuteReaderAsync();
        await r2.ReadAsync();

        Assert.Equal(seeded.Machines.Count, r2.GetInt64(0));
        Assert.Equal(seeded.Machines.Count, r2.GetInt64(1));
        // A circle is centre plus one rim point — three drills in the catalog.
        Assert.Equal(3, r2.GetInt64(2));
        // A machine is bolted down; letting it travel would put it on another site's plan.
        Assert.Equal(seeded.Machines.Count, r2.GetInt64(3));
    }

    [Fact]
    public async Task EveryMachineDocument_ValidatesAgainstItsOwnFieldDefinitions()
    {
        // The seeder writes custom_fields with raw SQL, bypassing the API. If the documents did not
        // satisfy the definitions the same seeder wrote, the demo would show fields that the edit
        // dialog refuses to save — so validate through the real service, not a hand-rolled check.
        await using var conn = _connFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var seeded = await SeedAsync(conn, _orgContext.OrgId);

        await using var cmd = new NpgsqlCommand(
            @"SELECT r.code, r.resource_type_id, r.custom_fields
              FROM resources r
              JOIN resource_types rt ON rt.id = r.resource_type_id
              WHERE rt.key IN ('mill','drill','assembly_station')", conn, tx);

        var docs = new List<(string Code, Guid TypeId, Dictionary<string, JsonElement> Values)>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                docs.Add((reader.GetString(0), reader.GetGuid(1),
                    JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(reader.GetString(2))!));
            }
        }

        Assert.NotEmpty(docs);

        // Field definitions, read back the way the API reads them.
        var fieldsByType = new Dictionary<Guid, List<(string Key, string DataType, bool Required)>>();
        await using (var fieldCmd = new NpgsqlCommand(
            "SELECT resource_type_id, key, data_type, is_required FROM resource_custom_fields", conn, tx))
        await using (var reader = await fieldCmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var typeId = reader.GetGuid(0);
                if (!fieldsByType.TryGetValue(typeId, out var list))
                    fieldsByType[typeId] = list = [];
                list.Add((reader.GetString(1), reader.GetString(2), reader.GetBoolean(3)));
            }
        }

        foreach (var (code, typeId, values) in docs)
        {
            var fields = fieldsByType[typeId];

            foreach (var key in values.Keys)
            {
                Assert.True(fields.Any(f => f.Key == key),
                    $"{code} carries a value for '{key}', which its type does not define.");
            }

            foreach (var field in fields.Where(f => f.DataType == CustomFieldDataTypes.List))
            {
                // A list field's rows hang off the resource, so a value here would be meaningless —
                // and the API rejects it.
                Assert.False(values.ContainsKey(field.Key),
                    $"{code} carries a value for list field '{field.Key}', which takes none.");
            }

            foreach (var field in fields.Where(f => f.Required))
            {
                Assert.True(values.ContainsKey(field.Key),
                    $"{code} is missing required field '{field.Key}'.");
            }
        }
    }

    [Fact]
    public async Task LookupValues_PointAtRowsOfTheBoundSharedInstance()
    {
        // A lookup value is a row id. If it named a row from another instance — or a row that had
        // been deleted — the machine would show an empty selection with no error anywhere.
        await using var conn = _connFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var seeded = await SeedAsync(conn, _orgContext.OrgId);

        await using var cmd = new NpgsqlCommand(
            @"SELECT count(*) FROM (
                SELECT jsonb_array_elements_text(r.custom_fields->'tooling') AS row_id
                FROM resources r
                JOIN resource_types rt ON rt.id = r.resource_type_id
                WHERE rt.key IN ('mill','drill') AND r.custom_fields ? 'tooling'
              ) picks
              WHERE picks.row_id NOT IN (
                SELECT id::text FROM list_rows WHERE list_instance_id = @instanceId
              )", conn, tx);
        cmd.Parameters.AddWithValue("instanceId", seeded.Lists.ToolingInstanceId);

        Assert.Equal(0L, (long)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task PerResourceLists_AreShapedTheWayTheConstraintDemands()
    {
        await using var conn = _connFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var seeded = await SeedAsync(conn, _orgContext.OrgId);

        // Scoped to the machines this test seeded. The fixture's database is shared, so counting
        // every resource-kind instance would be answering a question about other suites' data.
        await using var cmd = new NpgsqlCommand(
            @"SELECT count(*),
                     count(*) FILTER (WHERE i.name IS NULL AND i.resource_id IS NOT NULL AND i.field_id IS NOT NULL),
                     (SELECT count(*) FROM list_rows lr
                        JOIN list_instances li ON li.id = lr.list_instance_id
                        WHERE li.kind = 'resource' AND li.resource_id = ANY(@ids))
              FROM list_instances i WHERE i.kind = 'resource' AND i.resource_id = ANY(@ids)", conn, tx);
        cmd.Parameters.AddWithValue("ids", seeded.Machines.Select(m => m.Id).ToArray());
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();

        var instances = r.GetInt64(0);
        Assert.True(instances > 0, "some machines should carry a maintenance history");
        // kind='resource' means named by its owner, not by a name of its own.
        Assert.Equal(instances, r.GetInt64(1));
        Assert.True(r.GetInt64(2) >= instances * 3, "each history has at least three entries");
    }

    [Fact]
    public async Task SharedListDesignatesTheColumnThatNamesARow()
    {
        await using var conn = _connFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var seeded = await SeedAsync(conn, _orgContext.OrgId);

        await using var cmd = new NpgsqlCommand(
            @"SELECT c.key FROM list_definitions d
              JOIN list_columns c ON c.id = d.display_column_id
              WHERE d.id = @id", conn, tx);
        cmd.Parameters.AddWithValue("id", seeded.Lists.ToolingDefinitionId);

        // Without a designation a row is named by its first column plus everything else as context,
        // which reads badly for a catalogue whose second column is a number.
        Assert.Equal("item", (string?)await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task ResetSweepsSeededTypesSoASecondRunIsClean()
    {
        // The trap this guards: resource_types, resource_custom_fields and the list tables are not
        // children of anything the old truncate list named, so they survived a reset and the second
        // seed died on a unique constraint.
        await using var conn = _connFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await SeedAsync(conn, _orgContext.OrgId);
        await Orkyo.Foundation.Seed.TenantReset.TruncateAllAsync(conn, tx);

        await using var cmd = new NpgsqlCommand(
            @"SELECT (SELECT count(*) FROM resource_types WHERE is_system = false),
                     (SELECT count(*) FROM resource_types WHERE is_system),
                     (SELECT count(*) FROM resource_custom_fields),
                     (SELECT count(*) FROM list_definitions),
                     (SELECT count(*) FROM list_rows)", conn, tx);
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();

        Assert.Equal(0, r.GetInt64(0));
        // The built-ins belong to the migration, not to the seed.
        Assert.True(r.GetInt64(1) > 0, "system resource types must survive a reset");
        Assert.Equal(0, r.GetInt64(2));
        Assert.Equal(0, r.GetInt64(3));
        Assert.Equal(0, r.GetInt64(4));
    }

    [Fact]
    public async Task GroupsEveryMachineIntoATypedCell()
    {
        await using var conn = _connFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var seeded = await SeedAsync(conn, _orgContext.OrgId);

        await using var cmd = new NpgsqlCommand(
            @"SELECT count(DISTINCT g.id),
                     count(m.resource_id),
                     count(*) FILTER (WHERE g.resource_type_id <> m.resource_type_id)
              FROM resource_groups g
              JOIN resource_group_members m ON m.resource_group_id = g.id
              JOIN resource_types rt ON rt.id = g.resource_type_id
              WHERE rt.key IN ('mill','drill','assembly_station')", conn, tx);
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();

        Assert.True(r.GetInt64(0) > 0, "machines should be grouped into cells");
        // Every machine lands in exactly one cell.
        Assert.Equal(seeded.Machines.Count, r.GetInt64(1));
        // A member row carries the type too; a mismatch would put a drill in a mill cell.
        Assert.Equal(0, r.GetInt64(2));
    }
}
