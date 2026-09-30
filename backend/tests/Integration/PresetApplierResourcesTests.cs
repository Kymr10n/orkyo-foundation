using Api.Models.Preset;
using Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Orkyo.Foundation.Tests.Integration;

/// <summary>
/// The 1.1.0 sections of a preset against a real tenant database: types are activated with their
/// catalog fields, applicability lands, groups carry their type, resources home on the oldest
/// site with capabilities and memberships — and a second apply changes counts, not rows.
///
/// Runs on the isolated integration database because it needs real FKs (the composite member
/// constraints, the physical-needs-geometry check) rather than the shared HTTP fixture. Every
/// preset here uses a fresh id and fresh names, so nothing collides between tests; nothing is
/// deleted.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PresetApplierResourcesTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;

    public PresetApplierResourcesTests(PostgresFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// Activating the catalog `person` type leaves its lookup fields behind, and
    /// DirectoryLookupBindingTests asserts on a person with none — the same cleanup that test
    /// runs in its finally, so the two can share the database in either order.
    /// </summary>
    public async Task DisposeAsync()
    {
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            DELETE FROM resource_custom_fields f
            USING resource_types t
            WHERE t.id = f.resource_type_id AND t.key = 'person'", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static readonly string Run = Guid.NewGuid().ToString("N")[..8];

    private static Preset Workshop(string suffix) => new()
    {
        PresetId = $"workshop-{Run}-{suffix}",
        Name = "Workshop",
        Version = "1.1.0",
        CreatedAt = DateTime.UtcNow,
        Contents = new PresetContents
        {
            ResourceTypes =
            [
                new() { Key = $"room{Run}", DisplayName = "Room", DisplayNamePlural = "Rooms", HasGeometry = true, SingleGroupMembership = true },
                new() { Key = "person" },
                new() { Key = "tool" },
            ],
            Criteria =
            [
                new() { Key = "clean-room", Name = $"Clean Room {Run} {suffix}", DataType = Api.Models.CriterionDataType.Boolean, ResourceTypeKeys = [$"room{Run}"] },
                new() { Key = "max-load", Name = $"Max Load {Run} {suffix}", DataType = Api.Models.CriterionDataType.Number, Unit = "t", ResourceTypeKeys = ["tool", $"room{Run}"] },
            ],
            SpaceGroups =
            [
                new() { Key = "production", Name = $"Production {Run} {suffix}", ResourceTypeKey = $"room{Run}" },
            ],
            Resources =
            [
                new()
                {
                    Key = "hall", Name = $"Machine Hall {Run} {suffix}", Code = $"HALL-{Run}-{suffix}", TypeKey = $"room{Run}",
                    GroupKeys = ["production"],
                    Capabilities = [new() { CriterionKey = "max-load", Value = "5" }, new() { CriterionKey = "clean-room", Value = "false" }]
                },
                new() { Key = "anna", Name = $"Anna Keller {Run} {suffix}", Code = $"P-{Run}-{suffix}", TypeKey = "person" },
                new() { Key = "forklift", Name = $"Forklift {Run} {suffix}", TypeKey = "tool", Capabilities = [new() { CriterionKey = "max-load", Value = "2.5" }] },
            ]
        }
    };

    private async Task<PresetApplicationStats> ApplyAsync(Preset preset)
    {
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var stats = await PresetApplier.ApplyAsync(conn, tx, preset);
        await tx.CommitAsync();
        return stats;
    }

    private async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] args)
    {
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    private Task<long> CountAsync(string sql, params (string Name, object Value)[] args) => ScalarAsync<long>(sql, args);

    [Fact]
    public async Task Apply_ActivatesTypes_WithCatalogFields_AndReportsThem()
    {
        var preset = Workshop("types");

        var stats = await ApplyAsync(preset);

        stats.ResourceTypesActivated.Should().Be(3);
        (await CountAsync("SELECT count(*) FROM resource_types WHERE key = @k AND is_active", ("k", $"room{Run}"))).Should().Be(1);
        // A catalog type gets its shipped fields; `tool` ships four.
        (await CountAsync(@"SELECT count(*) FROM resource_custom_fields f
                            JOIN resource_types t ON t.id = f.resource_type_id WHERE t.key = 'tool'")).Should().BeGreaterThanOrEqualTo(4);
        // The directory type gets the two organization-list lookups when the 1820 lists exist.
        (await CountAsync(@"SELECT count(*) FROM resource_custom_fields f
                            JOIN resource_types t ON t.id = f.resource_type_id
                            WHERE t.key = 'person' AND f.key IN ('department', 'job_title') AND f.list_instance_id IS NOT NULL"))
            .Should().Be(2);
    }

    [Fact]
    public async Task Apply_WritesApplicability_TypedGroups_Resources_Capabilities_Memberships()
    {
        var preset = Workshop("rows");

        var stats = await ApplyAsync(preset);

        stats.ResourcesCreated.Should().Be(3);
        stats.ResourcesUpdated.Should().Be(0);

        var hall = preset.Contents.Resources[0];
        (await CountAsync(@"SELECT count(*) FROM criterion_resource_types crt
                            JOIN criteria c ON c.id = crt.criterion_id
                            JOIN resource_types t ON t.id = crt.resource_type_id
                            WHERE c.name = @n AND t.key = @k", ("n", preset.Contents.Criteria[1].Name), ("k", "tool"))).Should().Be(1);
        (await ScalarAsync<string>(@"SELECT t.key FROM resource_groups g JOIN resource_types t ON t.id = g.resource_type_id
                                     WHERE g.name = @n", ("n", preset.Contents.SpaceGroups[0].Name))).Should().Be($"room{Run}");
        (await ScalarAsync<bool>(@"SELECT home_site_id = (SELECT id FROM sites ORDER BY created_at, id LIMIT 1)
                                   AND NOT cross_site_allowed AND NOT is_physical
                                   FROM resources WHERE code = @c", ("c", hall.Code!))).Should().BeTrue();
        (await ScalarAsync<string>(@"SELECT allocation_mode FROM resources WHERE code = @c", ("c", preset.Contents.Resources[1].Code!)))
            .Should().Be("Fractional", "a directory type defaults to fractional allocation");
        (await CountAsync(@"SELECT count(*) FROM resource_capabilities rc JOIN resources r ON r.id = rc.resource_id
                            WHERE r.code = @c", ("c", hall.Code!))).Should().Be(2);
        (await CountAsync(@"SELECT count(*) FROM resource_group_members m JOIN resources r ON r.id = m.resource_id
                            JOIN resource_groups g ON g.id = m.resource_group_id
                            WHERE r.code = @c AND g.name = @g", ("c", hall.Code!), ("g", preset.Contents.SpaceGroups[0].Name))).Should().Be(1);
    }

    [Fact]
    public async Task ApplyTwice_AdoptsEverything_KeepsRenames_AndRecreatesADeletedRow()
    {
        var preset = Workshop("twice");
        await ApplyAsync(preset);
        var hallCode = preset.Contents.Resources[0].Code!;
        var forkliftName = preset.Contents.Resources[2].Name;

        // The tenant renames a room and deletes a tool between the two applies.
        await ScalarAsync<int>("UPDATE resources SET name = 'Renamed Hall' WHERE code = @c RETURNING 1", ("c", hallCode));
        await ScalarAsync<int>("DELETE FROM resources WHERE name = @n RETURNING 1", ("n", forkliftName));

        var again = await ApplyAsync(preset);

        again.ResourceTypesActivated.Should().Be(3);
        again.ResourcesUpdated.Should().Be(2, "the renamed room and the person are adopted");
        again.ResourcesCreated.Should().Be(1, "the deleted tool comes back");
        (await ScalarAsync<string>("SELECT name FROM resources WHERE code = @c", ("c", hallCode))).Should().Be("Renamed Hall");
        (await CountAsync("SELECT count(*) FROM resources WHERE code = @c", ("c", hallCode))).Should().Be(1);
        (await CountAsync("SELECT count(*) FROM resources WHERE name = @n", ("n", forkliftName))).Should().Be(1);
    }

    [Fact]
    public async Task Apply_AdoptsAPreExistingRowByCode_InsteadOfDuplicating()
    {
        // The office preset over an install that still carries the retired seed's rows.
        var preset = Workshop("adopt");
        var seededCode = preset.Contents.Resources[0].Code!;
        var typeId = await ScalarAsync<Guid>(@"
            INSERT INTO resource_types (key, display_name, display_name_plural, has_geometry, single_group_membership)
            VALUES (@k, 'Room', 'Rooms', true, true) ON CONFLICT (key) DO UPDATE SET is_active = true RETURNING id",
            ("k", $"room{Run}"));
        await ScalarAsync<Guid>(@"
            INSERT INTO resources (resource_type_id, name, allocation_mode, base_availability_percent, cross_site_allowed, code, is_physical, geometry)
            VALUES (@t, 'Seeded Hall', 'Exclusive', 100, false, @c, false, NULL) RETURNING id",
            ("t", typeId), ("c", seededCode));

        var stats = await ApplyAsync(preset);

        stats.ResourcesUpdated.Should().Be(1, "the seeded row is adopted by code");
        stats.ResourcesCreated.Should().Be(2);
        (await CountAsync("SELECT count(*) FROM resources WHERE code = @c", ("c", seededCode))).Should().Be(1);
        (await ScalarAsync<string>("SELECT name FROM resources WHERE code = @c", ("c", seededCode))).Should().Be("Seeded Hall", "adoption never renames");
    }
}

/// <summary>
/// The once-only rule a host relies on: applying a starter template at every start must leave a
/// database with exactly one application row and untouched edits after the first run.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class StarterTemplateServiceOnceOnlyTests
{
    private readonly PostgresFixture _fixture;

    public StarterTemplateServiceOnceOnlyTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ApplyStarterTemplateAsync_Twice_RecordsOneApplication_AndSkipsTheSecond()
    {
        var sut = new StarterTemplateService(_fixture.CreateConnectionFactory(), NullLogger<StarterTemplateService>.Instance);

        // The fixture's factory maps the identifier to a database name, as a multi-tenant one does.
        var db = PostgresFixture.TestTenantDatabase;

        await sut.ApplyStarterTemplateAsync(Guid.NewGuid(), db, Guid.Empty, StarterTemplateCatalog.Office);
        await using (var conn = await _fixture.OpenTestTenantConnectionAsync())
        await using (var cmd = new NpgsqlCommand("UPDATE resources SET name = 'Boardroom' WHERE code = 'MR-A'", conn))
            (await cmd.ExecuteNonQueryAsync()).Should().Be(1);

        await sut.ApplyStarterTemplateAsync(Guid.NewGuid(), db, Guid.Empty, StarterTemplateCatalog.Office);

        await using var check = await _fixture.OpenTestTenantConnectionAsync();
        await using var applications = new NpgsqlCommand("SELECT count(*) FROM preset_applications WHERE preset_id = 'office-v1'", check);
        ((long)(await applications.ExecuteScalarAsync())!).Should().Be(1);
        await using var name = new NpgsqlCommand("SELECT name FROM resources WHERE code = 'MR-A'", check);
        ((string)(await name.ExecuteScalarAsync())!).Should().Be("Boardroom", "a second start must not rewrite the tenant's edits");
    }
}
