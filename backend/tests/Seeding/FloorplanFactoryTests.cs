using Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Orkyo.Foundation.Seed.Factories;
using Orkyo.Foundation.Seed.Floorplans;
using Xunit;

namespace Orkyo.Foundation.Tests.Seeding;

/// <summary>
/// Integration test for the floorplan seeder against a real tenant DB: three sites, three plaintext
/// floorplan assets (1536×1024, valid checksum, NULL encryption metadata → AssetRepository read-path
/// passthrough), and 43 physical spaces that satisfy the <c>check_physical_has_geometry</c> CHECK
/// with rectangle geometry. Everything runs inside a rolled-back transaction so the shared DB stays
/// clean.
/// </summary>
[Collection("Database collection")]
public class FloorplanFactoryTests
{
    private readonly IOrgDbConnectionFactory _connFactory;
    private readonly OrgContext _orgContext;

    public FloorplanFactoryTests(DatabaseFixture fixture)
    {
        var scope = fixture.Factory.Services.CreateScope();
        _connFactory = scope.ServiceProvider.GetRequiredService<IOrgDbConnectionFactory>();
        _orgContext = scope.ServiceProvider.GetRequiredService<OrgContext>();
    }

    [Fact]
    public async Task SeedAsync_CreatesSitesAssetsAndPhysicalSpaces()
    {
        await using var conn = _connFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        Guid spaceTypeId;
        await using (var cmd = new NpgsqlCommand(
            "SELECT id FROM resource_types WHERE key = 'space' LIMIT 1", conn, tx))
        {
            spaceTypeId = (Guid)(await cmd.ExecuteScalarAsync())!;
        }

        var fixtures = FloorplanCatalog.ForProfile("manufacturing");
        var result = await FloorplanFactory.SeedAsync(conn, _orgContext.OrgId, fixtures, spaceTypeId);

        result.Sites.Should().HaveCount(3);
        result.Spaces.Should().HaveCount(43);
        result.Assets.Should().Be(3);

        var siteIds = result.Sites.Select(s => s.Id).ToArray();

        // ── Assets: one floorplan per site, plaintext (enc_algorithm NULL), correct dims/type ──
        await using (var cmd = new NpgsqlCommand(
            @"SELECT count(*),
                     count(*) FILTER (WHERE width_px = 1536 AND height_px = 1024),
                     count(*) FILTER (WHERE enc_algorithm IS NULL),
                     count(*) FILTER (WHERE content_type = 'image/png' AND asset_type = 'floorplan'),
                     count(*) FILTER (WHERE checksum_sha256 ~ '^[0-9a-f]{64}$'),
                     count(*) FILTER (WHERE tenant_id = @tid AND get_byte(data, 0) = 137)
              FROM assets WHERE owner_id = ANY(@ids)", conn, tx))
        {
            cmd.Parameters.AddWithValue("ids", siteIds);
            cmd.Parameters.AddWithValue("tid", _orgContext.OrgId);
            await using var r = await cmd.ExecuteReaderAsync();
            await r.ReadAsync();
            r.GetInt64(0).Should().Be(3, "one floorplan asset per site");
            r.GetInt64(1).Should().Be(3, "all assets are 1536×1024");
            r.GetInt64(2).Should().Be(3, "assets are stored plaintext (enc_algorithm NULL)");
            r.GetInt64(3).Should().Be(3, "all are image/png floorplans");
            r.GetInt64(4).Should().Be(3, "checksums are lowercase 64-hex");
            r.GetInt64(5).Should().Be(3, "tenant_id matches and data starts with the PNG magic byte (0x89)");
        }

        // ── Spaces: 43 physical, all with geometry (CHECK satisfied) ──
        await using (var cmd = new NpgsqlCommand(
            @"SELECT count(*) FILTER (WHERE is_physical),
                     count(*) FILTER (WHERE is_physical AND geometry IS NOT NULL),
                     count(*) FILTER (WHERE geometry->>'Type' = 'rectangle'
                                        AND jsonb_array_length(geometry->'Coordinates') = 2)
              FROM spaces WHERE site_id = ANY(@ids)", conn, tx))
        {
            cmd.Parameters.AddWithValue("ids", siteIds);
            await using var r = await cmd.ExecuteReaderAsync();
            await r.ReadAsync();
            r.GetInt64(0).Should().Be(43, "every seeded room is a physical space");
            r.GetInt64(1).Should().Be(43, "physical spaces carry geometry");
            r.GetInt64(2).Should().Be(43, "geometry is a two-corner rectangle in PascalCase");
        }

        // Roll back — leave the shared DB untouched.
        await tx.RollbackAsync();
    }
}
