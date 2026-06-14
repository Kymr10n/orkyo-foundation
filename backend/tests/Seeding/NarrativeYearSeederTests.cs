using Api.Services;
using Bogus;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using Orkyo.Foundation.Seed.Factories;
using Orkyo.Foundation.Seed.Floorplans;
using Orkyo.Foundation.Seed.Narrative;
using Orkyo.Foundation.Seed.Scales;
using Xunit;

namespace Orkyo.Foundation.Tests.Seeding;

/// <summary>
/// Integration test for the relatable-year narrative seed against a real tenant DB (rolled back).
/// Proves the demo is coherent and correct, not just non-empty: assigned resources actually satisfy
/// every requirement (CapabilityMatcher presence/Boolean/Enum), tool assignments are facility-local,
/// both supported allocation modes appear (Exclusive rooms/machines; Fractional people, shared
/// storage rooms, and forklifts/cranes), every Exclusive assignment carries a null percent (the
/// validator rejects a percent on Exclusive resources), holidays/shutdowns + absences exist, and
/// conflicts are only the small injected set.
/// </summary>
[Collection("Database collection")]
public class NarrativeYearSeederTests
{
    private readonly IOrgDbConnectionFactory _connFactory;
    private readonly OrgContext _orgContext;

    public NarrativeYearSeederTests(DatabaseFixture fixture)
    {
        var scope = fixture.Factory.Services.CreateScope();
        _connFactory = scope.ServiceProvider.GetRequiredService<IOrgDbConnectionFactory>();
        _orgContext = scope.ServiceProvider.GetRequiredService<OrgContext>();
    }

    [Fact]
    public async Task NarrativeSeed_IsCoherent_Matched_AndExercisesEveryAspect()
    {
        await using var conn = _connFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var faker = new Faker { Random = new Randomizer(1337) };

        var spaceTypeId = await ScalarGuid(conn, tx, "SELECT id FROM resource_types WHERE key='space' LIMIT 1");
        var personTypeId = await ScalarGuid(conn, tx, "SELECT id FROM resource_types WHERE key='person' LIMIT 1");

        var fp = await FloorplanFactory.SeedAsync(conn, _orgContext.OrgId, FloorplanCatalog.ForProfile("manufacturing"), spaceTypeId);

        // 30 minimal people (resources of type person) — enough for facility coverage.
        // Deterministic ids (own Randomizer so the main faker stream is untouched): the cross-site
        // off-site selection in SiteModelFactory.ApplyAsync is `abs(hashtext(id::text)) % 40 = 0`, so
        // random person ids made the cross-site-ratio assertion below non-deterministic run-to-run.
        var idRng = new Randomizer(1337);
        var people = new List<PeopleFactories.SeededPerson>();
        var now = DateTime.UtcNow;
        using (var w = await conn.BeginBinaryImportAsync(
            "COPY public.resources (id, resource_type_id, name, allocation_mode, base_availability_percent, is_active, created_at, updated_at) FROM STDIN (FORMAT BINARY)"))
        {
            for (var i = 0; i < 30; i++)
            {
                var id = idRng.Guid();
                await w.StartRowAsync();
                await w.WriteAsync(id, NpgsqlDbType.Uuid);
                await w.WriteAsync(personTypeId, NpgsqlDbType.Uuid);
                await w.WriteAsync($"Person {i}", NpgsqlDbType.Varchar);
                await w.WriteAsync("Fractional", NpgsqlDbType.Varchar); // people are Fractional in production (PeopleFactories)
                await w.WriteAsync(100, NpgsqlDbType.Integer);
                await w.WriteAsync(true, NpgsqlDbType.Boolean);
                await w.WriteAsync(now, NpgsqlDbType.TimestampTz);
                await w.WriteAsync(now, NpgsqlDbType.TimestampTz);
                people.Add(new PeopleFactories.SeededPerson(id, $"Person {i}"));
            }
            await w.CompleteAsync();
        }

        var facilities = FacilityModel.All;
        var tools = await ToolFactory.SeedAsync(conn, facilities);
        var criteria = await CapabilityFactory.SeedSkillCriteriaAsync(conn, includeTools: true);
        var cohorts = Cohorts.Build(facilities, fp.Sites, fp.Spaces, people, tools);
        var caps = await CapabilityFactory.AssignAsync(conn, criteria, cohorts, faker);
        var cal = new YearCalendar(DateTime.UtcNow);
        var avail = await AvailabilityFactory.SeedAsync(conn, cal, fp.Sites, people, faker, includeTools: true);
        var year = await NarrativeYearSeeder.SeedAsync(conn, cohorts, criteria, caps.PersonSkills, cal, ScaleCatalog.Resolve("tiny"), faker);

        year.Requests.Should().BeGreaterThan(0);
        year.Requirements.Should().BeGreaterThan(0);
        year.Assignments.Should().BeGreaterThan(0);
        year.Conflicts.Should().BeGreaterThan(0);
        avail.Events.Should().BeGreaterThan(0);
        avail.Absences.Should().BeGreaterThan(0);
        tools.Should().NotBeEmpty();

        // year.Conflicts counts BOTH injected kinds: ~2.5 % capability staffing + ~2.5 % scheduling
        // clones ≈ 5 % of leaf requests. Each clone flags 2 requests in the UI (source + clone), so
        // the visible conflict rate is ~10 % of scheduled requests. The 0.15 bound leaves headroom
        // for the per-cohort Math.Max(1, …) floors at small/tiny scales.
        var leafRequests = year.Requests - cohorts.Count; // subtract parent summary rows
        ((double)year.Conflicts / leafRequests).Should().BeLessThan(0.15,
            "injected conflicts must stay near ~5 % of leaf requests (visible ~10 % in the UI)");

        // Scope all assertions to the requests this test seeded — earlier tests in the suite may have
        // committed rows to these tables (via the API) and we must not count them.
        var seededIds = year.RequestIds.ToArray();

        // The demo seeds a small, scale-independent backlog of unscheduled tasks (no start/end) for
        // the user to schedule themselves — they must surface in the utilization backlog.
        var (_, backlog) = await TwoLongs(conn, tx,
            "SELECT 0, count(*) FROM requests WHERE id = ANY(@ids) AND start_ts IS NULL AND planning_mode='leaf'",
            ("ids", seededIds));
        backlog.Should().Be(15, "the narrative seed adds a 15-item unscheduled backlog");

        // Per request with requirements: does ANY assigned resource cover ALL of them? (reqSat).
        var (reqTotal, reqSat) = await TwoLongs(conn, tx, @"
            WITH req AS (SELECT request_id, array_agg(criterion_id) crits FROM request_requirements
                         WHERE request_id = ANY(@ids) GROUP BY request_id),
            sat AS (SELECT r.request_id, bool_or(NOT EXISTS (
                SELECT 1 FROM unnest(r.crits) c
                WHERE NOT EXISTS (SELECT 1 FROM resource_capabilities rc WHERE rc.resource_id=ra.resource_id AND rc.criterion_id=c))) ok
              FROM req r JOIN resource_assignments ra ON ra.request_id=r.request_id GROUP BY r.request_id)
            SELECT count(*), count(*) FILTER (WHERE ok) FROM sat",
            ("ids", seededIds));
        // Correctly-staffed jobs are satisfiable by their capable lead; the injected people-capability
        // conflicts (B1) deliberately staff a few with an incapable person and nobody else, so those
        // requirements are unsatisfiable. Both facts must hold — that's the people-capability conflict.
        reqTotal.Should().BeGreaterThan(0);
        reqSat.Should().BeGreaterThan(0, "correctly-staffed requests are satisfiable by their assigned people");
        reqSat.Should().BeLessThan(reqTotal, "the injected people-capability conflicts leave some requirements unsatisfiable");

        // Applicability is consistent with assignments: no seeded capability is assigned on a resource
        // type the criterion is not marked applicable to. Rooms now carry only their space-specs (never
        // person-skills) and people only their skills, so this holds by construction (no backfill).
        var (_, orphanCaps) = await TwoLongs(conn, tx, @"
            SELECT 0, count(*) FROM resource_capabilities rc
            JOIN resources r ON r.id = rc.resource_id
            WHERE rc.criterion_id = ANY(@critIds)
              AND NOT EXISTS (SELECT 1 FROM criterion_resource_types crt
                              WHERE crt.criterion_id = rc.criterion_id AND crt.resource_type_id = r.resource_type_id)",
            ("critIds", criteria.Values.ToArray()));
        orphanCaps.Should().Be(0, "every assigned capability must have matching criterion_resource_types applicability");

        // The scheduling conflicts (B2) are clones sharing their source's room+lead+slot — proving the
        // overbook injection fired (its double-book surfaces as space + person overlaps).
        var (_, rushClones) = await TwoLongs(conn, tx,
            "SELECT 0, count(*) FROM requests WHERE id = ANY(@ids) AND name LIKE 'Rush order — %'",
            ("ids", seededIds));
        rushClones.Should().BeGreaterThan(0, "the seed must inject overbook (clone) conflicts");

        // Tool assignments are facility-local (tool name prefixed with the request's space site code).
        var (toolTotal, toolCoherent) = await TwoLongs(conn, tx, @"
            WITH spacesite AS (
                SELECT ra.request_id, si.code site_code
                FROM resource_assignments ra JOIN spaces s ON s.id=ra.resource_id JOIN sites si ON si.id=s.site_id
                WHERE ra.request_id = ANY(@ids))
            SELECT count(*), count(*) FILTER (WHERE r.name LIKE ss.site_code||' %')
            FROM resource_assignments ra
            JOIN resources r ON r.id=ra.resource_id
            JOIN resource_types rt ON rt.id=r.resource_type_id AND rt.key='tool'
            JOIN spacesite ss ON ss.request_id=ra.request_id",
            ("ids", seededIds));
        toolCoherent.Should().Be(toolTotal, "every tool assignment must belong to the request's facility");

        // Both supported allocation modes appear among assigned resources; the unsupported
        // ConcurrentCapacity (which the validator rejects outright) must never be seeded.
        var modes = new List<string>();
        await using (var cmd = new NpgsqlCommand(
            "SELECT DISTINCT r.allocation_mode FROM resource_assignments ra JOIN resources r ON r.id=ra.resource_id WHERE ra.request_id = ANY(@ids)",
            conn, tx))
        {
            cmd.Parameters.AddWithValue("ids", seededIds);
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync()) modes.Add(rd.GetString(0));
        }
        modes.Should().Contain(["Exclusive", "Fractional"]);
        modes.Should().NotContain("ConcurrentCapacity", "ConcurrentCapacity is not supported by the validator and must not be seeded");

        // Root-cause guard: an Exclusive resource must carry a null allocation_percent — the validator
        // emits an InvalidAllocationPercent blocker otherwise, which is what made every room conflicted.
        var (_, exclusiveWithPct) = await TwoLongs(conn, tx, @"
            SELECT 0, count(*) FROM resource_assignments ra
            JOIN resources r ON r.id = ra.resource_id
            WHERE r.allocation_mode = 'Exclusive' AND ra.allocation_percent IS NOT NULL
              AND ra.request_id = ANY(@ids)",
            ("ids", seededIds));
        exclusiveWithPct.Should().Be(0, "Exclusive assignments must have a null percent (validator rejects a percent on Exclusive resources)");

        // Exclusive machines (tools) must never accidentally double-book — overlap pairs stay within
        // the injected-conflict band.
        var (_, toolPairs) = await TwoLongs(conn, tx, @"
            SELECT 0, count(*) FROM resource_assignments a
            JOIN resource_assignments b ON a.resource_id=b.resource_id AND a.id<b.id AND a.start_utc<b.end_utc AND b.start_utc<a.end_utc
            JOIN resources r ON r.id=a.resource_id
            JOIN resource_types rt ON rt.id=r.resource_type_id AND rt.key='tool'
            WHERE r.allocation_mode='Exclusive' AND a.request_id = ANY(@ids)",
            ("ids", seededIds));
        toolPairs.Should().BeLessThanOrEqualTo(year.Conflicts + 2,
            "Exclusive machines must not accidentally double-book beyond the injected conflicts");

        // People must not be accidentally overbooked either: total allocation in any overlapping
        // window must not exceed 100 %. The only intentional person overlaps are the injected clone
        // conflicts (B2), which each add one lead-lead pair. Allow a small buffer for the rare case
        // where two clones happen to share a lead.
        var (_, personPairs) = await TwoLongs(conn, tx, @"
            SELECT 0, count(*)
            FROM resource_assignments a
            JOIN resource_assignments b
                ON a.resource_id = b.resource_id AND a.id < b.id
                AND a.start_utc < b.end_utc AND b.start_utc < a.end_utc
            JOIN resources r ON r.id = a.resource_id
            JOIN resource_types rt ON rt.id = r.resource_type_id AND rt.key = 'person'
            WHERE (a.allocation_percent + b.allocation_percent) > 100
              AND a.request_id = ANY(@ids)",
            ("ids", seededIds));
        personPairs.Should().BeLessThanOrEqualTo(year.Conflicts + 2,
            "accidental person overbooking must stay within the intentional conflict band");

        // Site model: pin cohort people to their facility site, then run the full home/current-site
        // pass (which also adopts each request's space site). Regression guard for the "every booking
        // conflicted" bug — the post-commit round-robin must NOT scatter cohort people across sites
        // (that made ~56 % of requests cross-site mismatched and painted the whole grid red).
        await SiteModelFactory.ApplyCohortSitesAsync(conn, tx,
            cohorts.SelectMany(c => c.People.Select(p => (p.ResourceId, c.SiteId))).ToList());
        await SiteModelFactory.ApplyAsync(conn, spaceTypeId, personTypeId, tx);

        var (sitedReqs, crossSiteReqs) = await TwoLongs(conn, tx, @"
            SELECT count(DISTINCT req.id),
                   count(DISTINCT req.id) FILTER (
                       WHERE res.current_site_id IS NOT NULL AND res.current_site_id <> req.site_id)
            FROM requests req
            JOIN resource_assignments ra ON ra.request_id = req.id AND ra.assignment_status <> 'Cancelled'
            JOIN resources res ON res.id = ra.resource_id
            JOIN resource_types rt ON rt.id = res.resource_type_id AND rt.key = 'person'
            WHERE req.id = ANY(@ids) AND req.site_id IS NOT NULL",
            ("ids", seededIds));
        sitedReqs.Should().BeGreaterThan(0, "scheduled requests adopt their space's site");
        ((double)crossSiteReqs / sitedReqs).Should().BeLessThan(0.15,
            "cohort people stay at their facility site — only a small deliberate set is cross-site");

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task PeopleAndSpacesOnly_SeedsNoToolCriteriaTagsOrScopes()
    {
        await using var conn = _connFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var faker = new Faker { Random = new Randomizer(1337) };

        var personTypeId = await ScalarGuid(conn, tx, "SELECT id FROM resource_types WHERE key='person' LIMIT 1");
        var spaceTypeId = await ScalarGuid(conn, tx, "SELECT id FROM resource_types WHERE key='space' LIMIT 1");

        var fp = await FloorplanFactory.SeedAsync(conn, _orgContext.OrgId, FloorplanCatalog.ForProfile("manufacturing"), spaceTypeId);

        var people = new List<PeopleFactories.SeededPerson>();
        var now = DateTime.UtcNow;
        using (var w = await conn.BeginBinaryImportAsync(
            "COPY public.resources (id, resource_type_id, name, allocation_mode, base_availability_percent, is_active, created_at, updated_at) FROM STDIN (FORMAT BINARY)"))
        {
            for (var i = 0; i < 30; i++)
            {
                var id = Guid.NewGuid();
                await w.StartRowAsync();
                await w.WriteAsync(id, NpgsqlDbType.Uuid);
                await w.WriteAsync(personTypeId, NpgsqlDbType.Uuid);
                await w.WriteAsync($"Person {i}", NpgsqlDbType.Varchar);
                await w.WriteAsync("Fractional", NpgsqlDbType.Varchar); // people are Fractional in production (PeopleFactories)
                await w.WriteAsync(100, NpgsqlDbType.Integer);
                await w.WriteAsync(true, NpgsqlDbType.Boolean);
                await w.WriteAsync(now, NpgsqlDbType.TimestampTz);
                await w.WriteAsync(now, NpgsqlDbType.TimestampTz);
                people.Add(new PeopleFactories.SeededPerson(id, $"Person {i}"));
            }
            await w.CompleteAsync();
        }

        // Baselines: the shared fixture commits demo data, so assert this seed's *delta* is zero.
        const string toolTagSql = @"SELECT count(*) FROM criterion_resource_types crt
            JOIN resource_types rt ON rt.id=crt.resource_type_id AND rt.key='tool'";
        const string maxLoadSql = "SELECT count(*) FROM criteria WHERE name='Max Load'";
        const string scopeSql = "SELECT count(*) FROM availability_event_scopes";
        var (toolTags0, maxLoad0, scopes0) =
            (await ScalarLong(conn, tx, toolTagSql), await ScalarLong(conn, tx, maxLoadSql), await ScalarLong(conn, tx, scopeSql));

        var facilities = FacilityModel.All;
        // People + spaces only: no tools seeded, includeTools=false everywhere.
        var criteria = await CapabilityFactory.SeedSkillCriteriaAsync(conn, includeTools: false);
        var cohorts = Cohorts.Build(facilities, fp.Sites, fp.Spaces, people, []);
        await CapabilityFactory.AssignAsync(conn, criteria, cohorts, faker);
        var cal = new YearCalendar(DateTime.UtcNow);
        await AvailabilityFactory.SeedAsync(conn, cal, fp.Sites, people, faker, includeTools: false);

        // No new criterion is tagged to the tool resource type.
        (await ScalarLong(conn, tx, toolTagSql)).Should().Be(toolTags0,
            "no criterion should be tagged for the tool resource type when tools are off");
        // The tool-only Max Load criterion is not seeded.
        (await ScalarLong(conn, tx, maxLoadSql)).Should().Be(maxLoad0,
            "tool-only specs must not be seeded when tools are off");
        // No tool-scoped maintenance window / availability scopes.
        (await ScalarLong(conn, tx, scopeSql)).Should().Be(scopes0,
            "the tool-scoped maintenance window is skipped when tools are off");

        await tx.RollbackAsync();
    }

    private static async Task<long> ScalarLong(NpgsqlConnection conn, NpgsqlTransaction tx, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<Guid> ScalarGuid(NpgsqlConnection conn, NpgsqlTransaction tx, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<(long, long)> TwoLongs(NpgsqlConnection conn, NpgsqlTransaction tx, string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        await using var rd = await cmd.ExecuteReaderAsync();
        await rd.ReadAsync();
        return (rd.GetInt64(0), rd.GetInt64(1));
    }
}
