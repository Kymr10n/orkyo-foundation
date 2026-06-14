using Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Orkyo.Foundation.Seed.Factories;
using Xunit;

namespace Orkyo.Foundation.Tests.Seeding;

/// <summary>
/// Validates the demo-seed Home-Site post-step against a real tenant DB (rolled back).
/// Running ApplyAsync exercises every statement against the live schema (catching any SQL error);
/// the assertions cover the space-immovable and person-distribution effects.
/// </summary>
[Collection("Database collection")]
public class SiteModelFactoryTests
{
    private readonly IOrgDbConnectionFactory _connFactory;
    private readonly OrgContext _orgContext;

    public SiteModelFactoryTests(DatabaseFixture fixture)
    {
        var scope = fixture.Factory.Services.CreateScope();
        _connFactory = scope.ServiceProvider.GetRequiredService<IOrgDbConnectionFactory>();
        _orgContext = scope.ServiceProvider.GetRequiredService<OrgContext>();
    }

    [Fact]
    public async Task ApplyAsync_MakesSpacesImmovable_AndHomesPeople()
    {
        await using var conn = _connFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var spaceTypeId = await ScalarGuid(conn, tx, "SELECT id FROM resource_types WHERE key='space' LIMIT 1");
        var personTypeId = await ScalarGuid(conn, tx, "SELECT id FROM resource_types WHERE key='person' LIMIT 1");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        await Exec(conn, tx,
            $"INSERT INTO sites (name, code) VALUES ('Site A {suffix}', 'A{suffix}'), ('Site B {suffix}', 'B{suffix}')");

        var spaceId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        await Exec(conn, tx,
            "INSERT INTO resources (id, resource_type_id, name, allocation_mode, base_availability_percent, is_active) VALUES " +
            $"('{spaceId}', '{spaceTypeId}', 'Space {suffix}', 'Exclusive', 100, true), " +
            $"('{personId}', '{personTypeId}', 'Person {suffix}', 'Exclusive', 100, true)");

        await SiteModelFactory.ApplyAsync(conn, spaceTypeId, personTypeId, tx);

        // Spaces are immovable.
        var spaceCross = await ScalarBool(conn, tx, $"SELECT cross_site_allowed FROM resources WHERE id = '{spaceId}'");
        Assert.False(spaceCross);

        // People are anchored to a home site.
        var home = await ScalarNullableGuid(conn, tx,
            $"SELECT home_site_id FROM resources WHERE id = '{personId}'");
        Assert.NotNull(home);

        await tx.RollbackAsync();
    }

    private static async Task Exec(NpgsqlConnection conn, NpgsqlTransaction tx, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> ScalarGuid(NpgsqlConnection conn, NpgsqlTransaction tx, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<bool> ScalarBool(NpgsqlConnection conn, NpgsqlTransaction tx, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<Guid?> ScalarNullableGuid(NpgsqlConnection conn, NpgsqlTransaction tx, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        var result = await cmd.ExecuteScalarAsync();
        return result is Guid g ? g : null;
    }
}
