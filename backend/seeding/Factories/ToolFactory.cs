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

    /// <summary>
    /// The type the seeded tools belong to, defined by the seed itself — `tool` is a catalog
    /// type since built-ins went away, so a fresh DB has no row to find. Values mirror the
    /// `tool` entry of ResourceTypeCatalog (backend/core; no project reference from seeding).
    /// Also used by <see cref="AvailabilityFactory"/>, which runs later and shares the row.
    /// </summary>
    public static Task<Guid> EnsureToolTypeAsync(NpgsqlConnection conn)
    {
        return ResourceTypeSeedHelpers.UpsertResourceTypeAsync(
            conn, tx: null, "tool", "Tool", "Tools",
            "Mobile equipment: hand tools, forklifts, cranes and the like.",
            "Wrench");
    }

    public static async Task<IReadOnlyList<SeededTool>> SeedAsync(
        NpgsqlConnection conn, IReadOnlyList<Facility> facilities,
        IReadOnlyList<SpaceFactories.SeededSite> sites)
    {
        var toolTypeId = await EnsureToolTypeAsync(conn);

        var now = DateTime.UtcNow;
        var tools = new List<SeededTool>();

        using var writer = await conn.BeginBinaryImportAsync(
            "COPY public.resources (id, resource_type_id, name, allocation_mode, base_availability_percent, is_active, home_site_id, created_at, updated_at) " +
            "FROM STDIN (FORMAT BINARY)");

        // A tool belongs to the facility that owns it. The site used to survive only as a prefix
        // on the name ("FWF Forklift"), which reads right and filters nothing — so a tool was
        // absent from its own site's utilization tab unless it happened to hold an assignment
        // in the visible window.
        var siteIdByCode = sites.ToDictionary(s => s.Code, s => s.Id);

        // The facility catalogue and the site list are built by different paths, so a code that
        // does not line up is a seed-data bug. A bare indexer would report it as
        // KeyNotFoundException naming nothing.
        var unknown = facilities.Select(f => f.SiteCode).Where(c => !siteIdByCode.ContainsKey(c)).ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidOperationException(
                $"Facilities reference site codes with no seeded site: {string.Join(", ", unknown)}. "
                + $"Known codes: {string.Join(", ", siteIdByCode.Keys)}.");
        }

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
                    await writer.WriteAsync(siteIdByCode[f.SiteCode], NpgsqlDbType.Uuid);
                    await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
                    await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
                    tools.Add(new SeededTool(id, f.SiteCode, spec.Role, name, spec.AllocationMode, spec.MaxLoadTons));
                }

        await writer.CompleteAsync();
        return tools;
    }
}
