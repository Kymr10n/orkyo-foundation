using System.Security.Cryptography;
using Npgsql;
using NpgsqlTypes;
using Orkyo.Foundation.Seed.Floorplans;

namespace Orkyo.Foundation.Seed.Factories;

/// <summary>
/// Seeds the curated floorplan demo: fixed sites, their floorplan image assets, and one physical
/// space per labeled room (geometry = a two-corner rectangle in image-pixel space, the shape the
/// frontend overlay renders). Reuses <see cref="SpaceFactories.SeededSite"/>/<see
/// cref="SpaceFactories.SeededSpace"/> so the downstream factories (groups, assignments) are
/// unchanged. Floorplan blobs are written as plaintext with NULL encryption metadata — the
/// AssetRepository read path returns those unchanged (no master key needed in the seeder).
/// </summary>
public static class FloorplanFactory
{
    public sealed record Result(
        IReadOnlyList<SpaceFactories.SeededSite> Sites,
        IReadOnlyList<SpaceFactories.SeededSpace> Spaces,
        int Assets);

    public static async Task<Result> SeedAsync(
        NpgsqlConnection conn,
        Guid tenantId,
        IReadOnlyList<FloorplanSite> fixtures,
        Guid spaceResourceTypeId)
    {
        var now = DateTime.UtcNow;
        var sites = new List<SpaceFactories.SeededSite>(fixtures.Count);

        // ── Sites ────────────────────────────────────────────────────────────────
        using (var siteWriter = await conn.BeginBinaryImportAsync(
            "COPY public.sites (id, name, code, description, created_at, updated_at) FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var f in fixtures)
            {
                var id = Guid.NewGuid();
                await siteWriter.StartRowAsync();
                await siteWriter.WriteAsync(id, NpgsqlDbType.Uuid);
                await siteWriter.WriteAsync(f.Name, NpgsqlDbType.Varchar);
                await siteWriter.WriteAsync(f.Code, NpgsqlDbType.Varchar);
                await siteWriter.WriteAsync($"{f.Name} — demo facility with floorplan.", NpgsqlDbType.Text);
                await siteWriter.WriteAsync(now, NpgsqlDbType.TimestampTz);
                await siteWriter.WriteAsync(now, NpgsqlDbType.TimestampTz);
                sites.Add(new SpaceFactories.SeededSite(id, f.Name, f.Code));
            }
            await siteWriter.CompleteAsync();
        }

        // ── Floorplan assets (one per site) ────────────────────────────────────────
        var assetCount = 0;
        using (var assetWriter = await conn.BeginBinaryImportAsync(
            "COPY public.assets (id, tenant_id, owner_type, owner_id, asset_type, file_name, " +
            "content_type, size_bytes, checksum_sha256, width_px, height_px, storage_kind, data, " +
            "created_at, updated_at) FROM STDIN (FORMAT BINARY)"))
        {
            for (var i = 0; i < fixtures.Count; i++)
            {
                var f = fixtures[i];
                var bytes = ReadEmbeddedImage(f.ImageFileName);
                var checksum = Convert.ToHexStringLower(SHA256.HashData(bytes));

                await assetWriter.StartRowAsync();
                await assetWriter.WriteAsync(Guid.NewGuid(), NpgsqlDbType.Uuid);
                await assetWriter.WriteAsync(tenantId, NpgsqlDbType.Uuid);
                await assetWriter.WriteAsync("site", NpgsqlDbType.Varchar);
                await assetWriter.WriteAsync(sites[i].Id, NpgsqlDbType.Uuid);
                await assetWriter.WriteAsync("floorplan", NpgsqlDbType.Varchar);
                await assetWriter.WriteAsync(f.ImageFileName, NpgsqlDbType.Varchar);
                await assetWriter.WriteAsync("image/png", NpgsqlDbType.Varchar);
                await assetWriter.WriteAsync((long)bytes.Length, NpgsqlDbType.Bigint);
                await assetWriter.WriteAsync(checksum, NpgsqlDbType.Varchar);
                await assetWriter.WriteAsync(f.WidthPx, NpgsqlDbType.Integer);
                await assetWriter.WriteAsync(f.HeightPx, NpgsqlDbType.Integer);
                await assetWriter.WriteAsync("postgres", NpgsqlDbType.Varchar);
                await assetWriter.WriteAsync(bytes, NpgsqlDbType.Bytea);
                await assetWriter.WriteAsync(now, NpgsqlDbType.TimestampTz);
                await assetWriter.WriteAsync(now, NpgsqlDbType.TimestampTz);
                assetCount++;
            }
            await assetWriter.CompleteAsync();
        }

        // ── Spaces: resources first (shared UUID), then the spaces subtype rows ─────
        var spaces = new List<SpaceFactories.SeededSpace>(fixtures.Sum(f => f.Rooms.Count));

        using (var resourceWriter = await conn.BeginBinaryImportAsync(
            "COPY public.resources (id, resource_type_id, name, allocation_mode, base_availability_percent, is_active, created_at, updated_at) " +
            "FROM STDIN (FORMAT BINARY)"))
        {
            for (var s = 0; s < fixtures.Count; s++)
            {
                foreach (var room in fixtures[s].Rooms)
                {
                    var id = Guid.NewGuid();
                    await resourceWriter.StartRowAsync();
                    await resourceWriter.WriteAsync(id, NpgsqlDbType.Uuid);
                    await resourceWriter.WriteAsync(spaceResourceTypeId, NpgsqlDbType.Uuid);
                    await resourceWriter.WriteAsync(room.Name, NpgsqlDbType.Varchar);
                    await resourceWriter.WriteAsync(room.AllocationMode, NpgsqlDbType.Varchar);
                    await resourceWriter.WriteAsync(100, NpgsqlDbType.Integer);
                    await resourceWriter.WriteAsync(true, NpgsqlDbType.Boolean);
                    await resourceWriter.WriteAsync(now, NpgsqlDbType.TimestampTz);
                    await resourceWriter.WriteAsync(now, NpgsqlDbType.TimestampTz);
                    spaces.Add(new SpaceFactories.SeededSpace(id, sites[s].Id, room.Name));
                }
            }
            await resourceWriter.CompleteAsync();
        }

        using (var spaceWriter = await conn.BeginBinaryImportAsync(
            "COPY public.spaces (id, site_id, code, is_physical, geometry, properties, capacity, created_at, updated_at) " +
            "FROM STDIN (FORMAT BINARY)"))
        {
            var idx = 0;
            for (var s = 0; s < fixtures.Count; s++)
            {
                var site = sites[s];
                foreach (var room in fixtures[s].Rooms)
                {
                    var space = spaces[idx++];
                    await spaceWriter.StartRowAsync();
                    await spaceWriter.WriteAsync(space.Id, NpgsqlDbType.Uuid);
                    await spaceWriter.WriteAsync(site.Id, NpgsqlDbType.Uuid);
                    await spaceWriter.WriteAsync($"{site.Code}-{room.Code}", NpgsqlDbType.Varchar);
                    await spaceWriter.WriteAsync(true, NpgsqlDbType.Boolean);          // is_physical
                    await spaceWriter.WriteAsync(RectangleGeometryJson(room), NpgsqlDbType.Jsonb);
                    await spaceWriter.WriteAsync("{}", NpgsqlDbType.Jsonb);             // properties
                    await spaceWriter.WriteAsync(room.Capacity, NpgsqlDbType.Integer);
                    await spaceWriter.WriteAsync(now, NpgsqlDbType.TimestampTz);
                    await spaceWriter.WriteAsync(now, NpgsqlDbType.TimestampTz);
                }
            }
            await spaceWriter.CompleteAsync();
        }

        return new Result(sites, spaces, assetCount);
    }

    /// <summary>
    /// Two-corner rectangle in PascalCase — matches how the API serializes
    /// <c>SpaceGeometry</c> to the DB (System.Text.Json default casing), so the read path binds it.
    /// </summary>
    internal static string RectangleGeometryJson(FloorplanRoom r) =>
        $$"""{"Type":"rectangle","Coordinates":[{"X":{{r.X}},"Y":{{r.Y}}},{"X":{{r.X + r.W}},"Y":{{r.Y + r.H}}}]}""";

    internal static byte[] ReadEmbeddedImage(string fileName)
    {
        var asm = typeof(FloorplanFactory).Assembly;
        var resourceName = Array.Find(
            asm.GetManifestResourceNames(),
            n => n.EndsWith(fileName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Embedded floorplan image '{fileName}' not found. Is it marked <EmbeddedResource> in the seeding csproj?");

        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Could not open embedded resource '{resourceName}'.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
