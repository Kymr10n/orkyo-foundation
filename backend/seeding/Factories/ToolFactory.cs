using Bogus;
using Npgsql;
using NpgsqlTypes;
using Orkyo.Foundation.Seed.Narrative;

namespace Orkyo.Foundation.Seed.Factories;

/// <summary>
/// Seeds tool/equipment resources (the 3rd resource type) for each facility from
/// <see cref="FacilityModel"/>: machines as Exclusive, forklifts/cranes as Fractional. Tools are
/// tenant-global rows; their facility association is held in-memory (returned) and enforced by the
/// narrative seeder, which only assigns a facility's tools to that facility's jobs.
/// </summary>
public static class ToolFactory
{
    public sealed record SeededTool(Guid Id, string SiteCode, string Role, string Name, string AllocationMode, double? MaxLoadTons);

    public static async Task<IReadOnlyList<SeededTool>> SeedAsync(
        NpgsqlConnection conn, IReadOnlyList<Facility> facilities)
    {
        Guid toolTypeId;
        await using (var cmd = new NpgsqlCommand(
            "SELECT id FROM public.resource_types WHERE key = 'tool' LIMIT 1", conn))
        {
            toolTypeId = (Guid?)await cmd.ExecuteScalarAsync()
                ?? throw new InvalidOperationException("resource_types row with key='tool' not found. Has the tenant DB been migrated?");
        }

        var now = DateTime.UtcNow;
        var tools = new List<SeededTool>();

        using var writer = await conn.BeginBinaryImportAsync(
            "COPY public.resources (id, resource_type_id, name, allocation_mode, base_availability_percent, is_active, created_at, updated_at) " +
            "FROM STDIN (FORMAT BINARY)");

        foreach (var f in facilities)
            foreach (var spec in f.Tools)
                for (var n = 1; n <= spec.Count; n++)
                {
                    var id = Guid.NewGuid();
                    var name = spec.Count > 1 ? $"{spec.Name} {n}" : spec.Name;
                    await writer.StartRowAsync();
                    await writer.WriteAsync(id, NpgsqlDbType.Uuid);
                    await writer.WriteAsync(toolTypeId, NpgsqlDbType.Uuid);
                    await writer.WriteAsync($"{f.SiteCode} {name}", NpgsqlDbType.Varchar);
                    await writer.WriteAsync(spec.AllocationMode, NpgsqlDbType.Varchar);
                    await writer.WriteAsync(100, NpgsqlDbType.Integer);
                    await writer.WriteAsync(true, NpgsqlDbType.Boolean);
                    await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
                    await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
                    tools.Add(new SeededTool(id, f.SiteCode, spec.Role, name, spec.AllocationMode, spec.MaxLoadTons));
                }

        await writer.CompleteAsync();
        return tools;
    }
}
