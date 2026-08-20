using Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Orkyo.Foundation.Seed.Factories;
using Orkyo.Foundation.Seed.Profiles;
using Orkyo.Foundation.Seed.Scales;
using Xunit;

namespace Orkyo.Foundation.Tests.Seeding;

/// <summary>
/// The department tree the seed produces, against a real tenant DB.
///
/// The property under test is the one nothing else catches: a <c>parent</c> cell holds a row id,
/// not a department name. Migration 1900 made the column a <c>row_ref</c>, and a name left in
/// there reads as an empty cell and makes the row unsavable through the API for good — so the
/// seed and the migration have to agree, and only a test says whether they do.
///
/// Everything runs inside a rolled-back transaction so the shared DB stays clean.
/// </summary>
[Collection("Database collection")]
public class OrganizationListSeedingTests
{
    private readonly IOrgDbConnectionFactory _connFactory;
    private readonly OrgContext _orgContext;

    public OrganizationListSeedingTests(DatabaseFixture fixture)
    {
        var scope = fixture.Factory.Services.CreateScope();
        _connFactory = scope.ServiceProvider.GetRequiredService<IOrgDbConnectionFactory>();
        _orgContext = scope.ServiceProvider.GetRequiredService<OrgContext>();
    }

    private async Task<(NpgsqlConnection Conn, NpgsqlTransaction Tx, Guid InstanceId)> SeedAsync()
    {
        var conn = _connFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();
        var tx = await conn.BeginTransactionAsync();

        var personTypeId = await PeopleFactories.ResolvePersonResourceTypeIdAsync(conn, tx);
        await PeopleFactories.SeedOrganizationListsAsync(
            conn, tx, new Manufacturing(), new Small(), personTypeId);

        await using var find = new NpgsqlCommand(
            @"SELECT i.id FROM list_instances i
                JOIN list_definitions d ON d.id = i.list_definition_id
               WHERE d.name = 'Departments' AND d.scope = 'organization'", conn, tx);
        return (conn, tx, (Guid)(await find.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task TheParentColumn_IsARowReference_NotText()
    {
        var (conn, tx, _) = await SeedAsync();
        await using var _c = conn;
        await using var _t = tx;

        // An append seed adopts migration 1820's definition; a reset seed builds its own. Either
        // way the column has to come out as the type 1900 declared, or the rows below are invalid.
        await using var cmd = new NpgsqlCommand(
            @"SELECT c.data_type FROM list_columns c
                JOIN list_definitions d ON d.id = c.list_definition_id
               WHERE d.name = 'Departments' AND d.scope = 'organization' AND c.key = 'parent'",
            conn, tx);

        Assert.Equal("row_ref", await cmd.ExecuteScalarAsync() as string);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task EverySeededParent_PointsAtASiblingRow()
    {
        var (conn, tx, instanceId) = await SeedAsync();
        await using var _c = conn;
        await using var _t = tx;

        await using var cmd = new NpgsqlCommand(
            @"SELECT count(*) FILTER (WHERE r.values ? 'parent'),
                     count(*) FILTER (
                         WHERE r.values ? 'parent'
                           AND NOT EXISTS (
                               SELECT 1 FROM list_rows p
                                WHERE p.id::text = r.values ->> 'parent'
                                  AND p.list_instance_id = r.list_instance_id))
                FROM list_rows r WHERE r.list_instance_id = @instanceId", conn, tx);
        cmd.Parameters.AddWithValue("instanceId", instanceId);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        var withParent = reader.GetInt64(0);
        var dangling = reader.GetInt64(1);

        // The scale seeds two levels, so the test is only meaningful if children were made at all.
        Assert.True(withParent > 0, "the seed produced no child departments to check");
        Assert.Equal(0, dangling);

        await reader.CloseAsync();
        await tx.RollbackAsync();
    }
}
