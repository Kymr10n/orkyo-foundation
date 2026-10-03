using Api.Models;
using Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Orkyo.Foundation.Seed.Factories;

namespace Orkyo.Foundation.Tests.Seeding;

/// <summary>
/// Integration test for the demo routings against a real tenant DB. Instantiating a routing needs
/// every operation to be a request template that targets a resource type; the database does not
/// enforce either, so this asserts both.
///
/// Everything runs inside a rolled-back transaction so the shared DB stays clean.
/// </summary>
[Collection("Database collection")]
public class RoutingFactoryTests
{
    private readonly IOrgDbConnectionFactory _connFactory;
    private readonly OrgContext _orgContext;

    public RoutingFactoryTests(DatabaseFixture fixture)
    {
        var scope = fixture.Factory.Services.CreateScope();
        _connFactory = scope.ServiceProvider.GetRequiredService<IOrgDbConnectionFactory>();
        _orgContext = scope.ServiceProvider.GetRequiredService<OrgContext>();
    }

    // The shared DB holds routings other tests committed, so every query reads only these.
    private static readonly string[] SeededNames = ["Aerospace bracket AB-310", "Drive shaft DS-200", "Product unit PU-100"];

    // The caller owns the transaction, and Npgsql has no nested ones — the factories ride it.
    private static async Task<int> SeedAsync(NpgsqlConnection conn)
    {
        var machineTypeIds = await MachineFactory.SeedTypesAsync(conn);
        var criteria = await CapabilityFactory.SeedSkillCriteriaAsync(conn);
        return await RoutingFactory.SeedAsync(conn, machineTypeIds, criteria);
    }

    [Fact]
    public async Task SeedsThreeRoutings_WithStepsInOrder()
    {
        await using var conn = _connFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var routings = await SeedAsync(conn);

        await using var cmd = new NpgsqlCommand(
            @"SELECT r.name, string_agg(t.name, ' > ' ORDER BY s.step_no)
              FROM routings r
              JOIN routing_steps s ON s.routing_id = r.id
              JOIN templates t ON t.id = s.operation_template_id
              WHERE r.name = ANY(@names)
              GROUP BY r.name ORDER BY r.name", conn, tx);
        cmd.Parameters.AddWithValue("names", SeededNames);
        await using var r = await cmd.ExecuteReaderAsync();
        var chains = new List<(string, string)>();
        while (await r.ReadAsync()) chains.Add((r.GetString(0), r.GetString(1)));

        Assert.Equal(3, routings);
        Assert.Equal(
            [
                ("Aerospace bracket AB-310", "Mill 5-axis aerospace parts > Drill fixture holes"),
                ("Drive shaft DS-200", "Turn shaft components > Machine precision components > Drill fixture holes"),
                ("Product unit PU-100", "Assemble product units > Test finished units"),
            ],
            chains);
    }

    [Fact]
    public async Task EveryOperation_IsARequestTemplateThatCanBeScheduled()
    {
        await using var conn = _connFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await SeedAsync(conn);

        // One row per distinct operation: its entity type, the machine type it targets, and how
        // many skills it asks for. The operation shared by two routings is one template.
        await using var cmd = new NpgsqlCommand(
            @"SELECT t.name, t.entity_type, rt.key, (SELECT count(*) FROM template_items i WHERE i.template_id = t.id)
              FROM templates t
              JOIN template_target_resource_types tt ON tt.template_id = t.id
              JOIN resource_types rt ON rt.id = tt.resource_type_id
              WHERE t.id IN (SELECT s.operation_template_id FROM routing_steps s
                             JOIN routings ro ON ro.id = s.routing_id WHERE ro.name = ANY(@names))
              ORDER BY t.name", conn, tx);
        cmd.Parameters.AddWithValue("names", SeededNames);
        await using var r = await cmd.ExecuteReaderAsync();
        var operations = new List<(string Name, string Entity, string Type, long Skills)>();
        while (await r.ReadAsync())
            operations.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt64(3)));

        Assert.Equal(
            [
                ("Assemble product units", "assembly_station"),
                ("Drill fixture holes", "drill"),
                ("Machine precision components", "mill"),
                ("Mill 5-axis aerospace parts", "cnc"),
                ("Test finished units", "test_station"),
                ("Turn shaft components", "lathe"),
            ],
            operations.Select(o => (o.Name, o.Type)));
        Assert.All(operations, o => Assert.Equal("request", o.Entity));
        Assert.All(operations, o => Assert.Equal(1, o.Skills));
    }
}
