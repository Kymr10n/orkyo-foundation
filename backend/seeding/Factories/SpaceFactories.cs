using Npgsql;
using NpgsqlTypes;

namespace Orkyo.Foundation.Seed.Factories;

/// <summary>
/// The room type, space groups by functional area, and the non-work rooms. The sites and rooms
/// themselves come from <see cref="FloorplanFactory"/>.
/// </summary>
public static class SpaceFactories
{
    public sealed record SeededSite(Guid Id, string Name, string Code);
    /// <summary><c>Code</c> is the floorplan room code (CNC, WELD, QC, …).</summary>
    public sealed record SeededSpace(Guid Id, Guid SiteId, string Name, string? Code = null);
    public sealed record SeededSpaceGroup(Guid Id, string Name);

    /// <summary>
    /// The type the seeded rooms belong to. The demo defines it for itself — `room`, alongside
    /// `mill` and the rest — rather than borrowing the built-in space type, which is an ordinary
    /// tenant type since 1800 and which a tenant may well have deleted. Seeding its own type is
    /// also what lets a reset sweep everything the seed made: TenantReset deletes non-system
    /// types, so the demo carries no leftovers between runs.
    /// </summary>
    public static Task<Guid> ResolveSpaceResourceTypeIdAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx) =>
        ResourceTypeSeedHelpers.UpsertResourceTypeAsync(
            conn, tx, "room", "Room", "Rooms",
            "A room on the floorplan. Work happens in it, and it holds one group at a time.",
            "Box", hasGeometry: true, singleGroupMembership: true);

    /// <summary>Functional-area order for display + the catch-all bucket (last).</summary>
    private static readonly string[] FunctionalAreaOrder =
    [
        "Production", "Quality", "Storage & Logistics", "Maintenance & Tooling", "Facilities & Admin",
    ];

    /// <summary>
    /// Maps a floorplan room code to a coarse functional area so seeded spaces form a few
    /// meaningful groups instead of round-robin noise. Unknown/null codes fall back to the
    /// "Facilities & Admin" catch-all.
    /// </summary>
    public static string FunctionalArea(string? code) => (code ?? string.Empty).ToUpperInvariant() switch
    {
        "CNC" or "ASSY" or "FAB" or "WELD" or "PAINT" or "GRIND" or "PROD" or "PKG" => "Production",
        "QC" => "Quality",
        "RAW" or "FIN" or "MAT" or "WHSE" => "Storage & Logistics",
        "MAINT" or "TOOL" or "ELEC" or "JAN" => "Maintenance & Tooling",
        _ => "Facilities & Admin",
    };

    /// <summary>
    /// Takes the schedulable capacity off rooms no work is ever booked into, and reports how many
    /// it changed.
    /// </summary>
    /// <remarks>
    /// A floorplan is a whole building: lobbies, toilets, a janitor's cupboard, the electrical
    /// room. They are real spaces and belong on the plan, but no job archetype targets them, so
    /// every one of them sat in the utilization denominator contributing capacity that could never
    /// be filled — about eight of the thirteen-to-fifteen rooms per site, which is most of why
    /// room utilization read near zero.
    ///
    /// The work rooms are derived from the facility model rather than listed again here, so a new
    /// archetype brings its room with it and this cannot drift. They keep <c>is_active</c>: a room
    /// that vanished from the floorplan and the resource lists would be a worse lie than an idle
    /// one.
    /// </remarks>
    public static async Task<int> MarkNonWorkRoomsUnavailableAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? tx,
        IReadOnlyList<Narrative.FacilityCohort> cohorts)
    {
        var workRoomIds = cohorts
            .SelectMany(c =>
            {
                var codes = c.Facility.Archetypes.Select(a => a.RoomCode)
                    .Concat(c.Facility.ConcurrentRoomCodes)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                return c.SpaceByRoomCode
                    .Where(kv => codes.Contains(kv.Key))
                    .Select(kv => kv.Value.Id);
            })
            .Distinct()
            .ToArray();

        var siteIds = cohorts.Select(c => c.SiteId).Distinct().ToArray();
        if (siteIds.Length == 0) return 0;

        using var cmd = new NpgsqlCommand(
            "UPDATE resources SET base_availability_percent = 0, updated_at = now() " +
            "WHERE home_site_id = ANY(@sites) AND resource_type_id = @spaceType " +
            "AND NOT (id = ANY(@workRooms))", conn, tx);
        cmd.Parameters.AddWithValue("sites", siteIds);
        cmd.Parameters.AddWithValue("workRooms", workRoomIds);
        cmd.Parameters.AddWithValue("spaceType", await ResolveSpaceResourceTypeIdAsync(conn, tx));
        return await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Seeds space groups + memberships by functional area (curated-floorplan path). One group
    /// per area that has at least one space; each space joins exactly one group (single-group is
    /// enforced by migration 1530).
    /// </summary>
    public static async Task<(IReadOnlyList<SeededSpaceGroup> Groups, int MemberCount)> SeedFunctionalSpaceGroupsAsync(
        NpgsqlConnection conn,
        IReadOnlyList<SeededSpace> spaces,
        Guid spaceResourceTypeId)
    {
        if (spaces.Count == 0) return ([], 0);

        // Group spaces by area, preserving the fixed display order; drop empty areas.
        var areaToSpaces = spaces
            .GroupBy(sp => FunctionalArea(sp.Code))
            .ToDictionary(g => g.Key, g => g.ToList());
        var presentAreas = FunctionalAreaOrder.Where(areaToSpaces.ContainsKey).ToList();

        var ids = await ResourceGroupSeedHelpers.CopyGroupsAsync(
            conn, presentAreas.Select(a => (a, spaceResourceTypeId)).ToList());
        var seeded = presentAreas.Select((a, i) => new SeededSpaceGroup(ids[i], a)).ToList();

        var memberCount = await ResourceGroupSeedHelpers.CopyMembersAsync(conn,
            presentAreas.SelectMany((area, i) =>
                areaToSpaces[area].Select(space => (ids[i], space.Id, spaceResourceTypeId))));

        return (seeded, memberCount);
    }
}
