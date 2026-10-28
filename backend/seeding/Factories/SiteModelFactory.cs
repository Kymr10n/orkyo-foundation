using Npgsql;

namespace Orkyo.Foundation.Seed.Factories;

/// <summary>
/// Populates the Home-Site model on already-seeded rows (migrations 1550 + 1560).
/// Runs as the last pass of the seed transaction so it sees every seeded row, and so the
/// binary-COPY writers don't each need the new columns threaded through.
///
/// Result on a multi-site demo tenant:
/// - spaces are immovable (cross_site_allowed = false);
/// - people get a home site: the narrative path pins them to their facility cohort's site up front
///   via <see cref="ApplyCohortSitesAsync"/> (so cohort work stays same-site); the round-robin below
///   only fills people still un-sited. On top, ~1 in 4 are not cross-site capable;
/// - requests are not touched here: the narrative seed writes each job's facility site when it
///   creates the job, and the unscheduled backlog stays site-neutral.
/// Where a person actually is at a point in time is no longer stored — it is derived from the
/// assignment covering that time, anchored to the home site when idle — so cross-site cases arise
/// naturally wherever a person's home site differs from a request's site. Single-site tenants degrade naturally: every "another site" lookup
/// is empty, so the model stays invisible.
/// </summary>
public static class SiteModelFactory
{
    /// <summary>
    /// Which people may not travel: about one in four, chosen by id so a reseed picks the same
    /// ones. <c>NarrativeYearSeeder</c> reads the same predicate so its two passes agree.
    /// </summary>
    public const string PinnedToHomeSite = "abs(hashtext(id::text)) % 4 = 0";

    /// <param name="tx">The seed transaction; tests pass their own to roll back.</param>
    public static async Task ApplyAsync(
        NpgsqlConnection conn, Guid spaceTypeId, Guid personTypeId, NpgsqlTransaction? tx = null)
    {
        // Spaces never move between sites.
        await ExecAsync(conn, tx,
            "UPDATE resources SET cross_site_allowed = false WHERE resource_type_id = @space",
            ("space", spaceTypeId));

        // Distribute home site round-robin across the tenant's sites — but only for people still
        // un-sited. Narrative cohorts are pinned to their facility site by ApplyCohortSitesAsync
        // before commit; this fills anyone outside a cohort.
        await ExecAsync(conn, tx, """
            WITH ppl AS (
                SELECT id, (ROW_NUMBER() OVER (ORDER BY created_at, id) - 1) AS rn
                FROM resources WHERE resource_type_id = @person AND home_site_id IS NULL
            ),
            site_arr AS (SELECT array_agg(id ORDER BY created_at, id) AS ids, count(*) AS n FROM sites)
            UPDATE resources r
            SET home_site_id = (SELECT ids[(p.rn % n) + 1] FROM site_arr)
            FROM ppl p
            WHERE r.id = p.id AND (SELECT n FROM site_arr) > 0
            """, ("person", personTypeId));

        // ~1 in 4 people are tied to their home site (no cross-site work).
        await ExecAsync(conn, tx,
            "UPDATE resources SET cross_site_allowed = false " +
            $"WHERE resource_type_id = @person AND {PinnedToHomeSite}",
            ("person", personTypeId));

    }

    /// <summary>
    /// Narrative path: pin each person to their facility cohort's home site so cohort work stays
    /// same-site. Must run before <see cref="ApplyAsync"/>, whose round-robin only fills people still
    /// un-sited. People rows are tenant-global but each belongs to exactly one cohort, so the
    /// (person → site) mapping is unambiguous.
    /// </summary>
    public static async Task ApplyCohortSitesAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, IReadOnlyList<(Guid PersonId, Guid SiteId)> people)
    {
        if (people.Count == 0) return;
        await ExecAsync(conn, tx, """
            UPDATE resources r
            SET home_site_id = v.site
            FROM unnest(@ids::uuid[], @sites::uuid[]) AS v(id, site)
            WHERE r.id = v.id
            """,
            ("ids", people.Select(p => p.PersonId).ToArray()),
            ("sites", people.Select(p => p.SiteId).ToArray()));
    }

    private static async Task ExecAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, string sql, params (string Name, object Value)[] args)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }
}
