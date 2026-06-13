using Npgsql;

namespace Orkyo.Foundation.Seed.Factories;

/// <summary>
/// Populates the Home-Site / Current-Site model on already-seeded rows (migration 1550).
/// Runs as a post-commit pass so it sees all data regardless of which path (floorplan or generic)
/// inserted it, and so the binary-COPY writers don't each need the new columns threaded through.
///
/// Result on a multi-site demo tenant:
/// - spaces are immovable (cross_site_allowed = false);
/// - people get home=current at their site: the narrative path pins them to their facility cohort's
///   site up front via <see cref="ApplyCohortSitesAsync"/> (so cohort work stays same-site); the
///   round-robin below only fills people still un-sited (the generic path). On top, ~1 in 4 are not
///   cross-site capable and ~1 in 40 are "temporarily" at another site (current ≠ home) — together a
///   small, deliberate set that exercises the cross-site warning and block paths;
/// - scheduled requests adopt the site of the space they were placed in (parity with the runtime
///   implicit-site-on-schedule rule); unscheduled requests stay site-neutral.
/// Single-site tenants degrade naturally: every "another site" lookup is empty, so the model stays
/// invisible.
/// </summary>
public static class SiteModelFactory
{
    /// <param name="tx">Optional transaction (tests pass one to roll back); production runs tx-less.</param>
    public static async Task ApplyAsync(
        NpgsqlConnection conn, Guid spaceTypeId, Guid personTypeId, NpgsqlTransaction? tx = null)
    {
        // Spaces never move between sites.
        await ExecAsync(conn, tx,
            "UPDATE resources SET cross_site_allowed = false WHERE resource_type_id = @space",
            ("space", spaceTypeId));

        // Distribute home=current round-robin across the tenant's sites — but only for people still
        // un-sited. Narrative cohorts are pinned to their facility site by ApplyCohortSitesAsync
        // before commit; this fills the generic path (and any tenant without cohorts).
        await ExecAsync(conn, tx, """
            WITH ppl AS (
                SELECT id, (ROW_NUMBER() OVER (ORDER BY created_at, id) - 1) AS rn
                FROM resources WHERE resource_type_id = @person AND home_site_id IS NULL
            ),
            site_arr AS (SELECT array_agg(id ORDER BY created_at, id) AS ids, count(*) AS n FROM sites)
            UPDATE resources r
            SET home_site_id    = (SELECT ids[(p.rn % n) + 1] FROM site_arr),
                current_site_id = (SELECT ids[(p.rn % n) + 1] FROM site_arr)
            FROM ppl p
            WHERE r.id = p.id AND (SELECT n FROM site_arr) > 0
            """, ("person", personTypeId));

        // ~1 in 4 people are tied to their home site (no cross-site work).
        await ExecAsync(conn, tx,
            "UPDATE resources SET cross_site_allowed = false " +
            "WHERE resource_type_id = @person AND abs(hashtext(id::text)) % 4 = 0",
            ("person", personTypeId));

        // ~1 in 40 are temporarily at a different site (current ≠ home) — drives the cross-site
        // cases. Kept deliberately sparse: a request is flagged if ANY assigned person is off-site,
        // so a small per-person rate already yields a realistic ~5% of requests with a cross-site
        // warning/block without swamping the otherwise-clean schedule.
        await ExecAsync(conn, tx, """
            UPDATE resources r
            SET current_site_id = (
                SELECT id FROM sites WHERE id <> r.home_site_id ORDER BY created_at LIMIT 1)
            WHERE r.resource_type_id = @person
              AND abs(hashtext(r.id::text)) % 40 = 0
              AND (SELECT count(*) FROM sites) > 1
            """, ("person", personTypeId));

        // Scheduled requests adopt the site of the space they were placed in (implicit-site parity);
        // spaceless / unscheduled requests stay site-neutral.
        await ExecAsync(conn, tx, """
            UPDATE requests req
            SET site_id = s.site_id
            FROM resource_assignments ra
            JOIN resources res ON res.id = ra.resource_id
            JOIN spaces s ON s.id = res.id
            WHERE ra.request_id = req.id
              AND ra.assignment_status <> 'Cancelled'
              AND req.site_id IS NULL
            """);
    }

    /// <summary>
    /// Narrative path: pin each person to their facility cohort's site (home = current) so cohort
    /// work stays same-site. Must run before <see cref="ApplyAsync"/>, whose round-robin only fills
    /// people still un-sited. People rows are tenant-global but each belongs to exactly one cohort,
    /// so the (person → site) mapping is unambiguous.
    /// </summary>
    public static async Task ApplyCohortSitesAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, IReadOnlyList<(Guid PersonId, Guid SiteId)> people)
    {
        if (people.Count == 0) return;
        await ExecAsync(conn, tx, """
            UPDATE resources r
            SET home_site_id = v.site, current_site_id = v.site
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
