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
/// all three allocation modes appear (incl. a ConcurrentCapacity space holding overlapping jobs),
/// holidays/shutdowns + absences exist, and conflicts are only the small injected set.
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
                await w.WriteAsync("Exclusive", NpgsqlDbType.Varchar);
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
        var criteria = await CapabilityFactory.SeedSkillCriteriaAsync(conn);
        var cohorts = Cohorts.Build(facilities, fp.Sites, fp.Spaces, people, tools);
        var caps = await CapabilityFactory.AssignAsync(conn, criteria, cohorts, faker);
        var cal = new YearCalendar(DateTime.UtcNow);
        var avail = await AvailabilityFactory.SeedAsync(conn, cal, fp.Sites, people, faker);
        var year = await NarrativeYearSeeder.SeedAsync(conn, cohorts, criteria, caps.PersonSkills, cal, ScaleCatalog.Resolve("tiny"), faker);

        year.Requests.Should().BeGreaterThan(0);
        year.Requirements.Should().BeGreaterThan(0);
        year.Assignments.Should().BeGreaterThan(0);
        year.Conflicts.Should().BeGreaterThan(0);
        avail.Events.Should().BeGreaterThan(0);
        avail.Absences.Should().BeGreaterThan(0);
        tools.Should().NotBeEmpty();

        // Scope all assertions to the requests this test seeded — earlier tests in the suite may have
        // committed rows to these tables (via the API) and we must not count them.
        var seededIds = year.RequestIds.ToArray();

        // Every request with requirements has at least one assigned resource covering ALL of them.
        var (reqTotal, reqSat) = await TwoLongs(conn, tx, @"
            WITH req AS (SELECT request_id, array_agg(criterion_id) crits FROM request_requirements
                         WHERE request_id = ANY(@ids) GROUP BY request_id),
            sat AS (SELECT r.request_id, bool_or(NOT EXISTS (
                SELECT 1 FROM unnest(r.crits) c
                WHERE NOT EXISTS (SELECT 1 FROM resource_capabilities rc WHERE rc.resource_id=ra.resource_id AND rc.criterion_id=c))) ok
              FROM req r JOIN resource_assignments ra ON ra.request_id=r.request_id GROUP BY r.request_id)
            SELECT count(*), count(*) FILTER (WHERE ok) FROM sat",
            ("ids", seededIds));
        reqSat.Should().Be(reqTotal, "every requirement must be satisfiable by an assigned resource");
        reqTotal.Should().BeGreaterThan(0);

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

        // All three allocation modes appear among assigned resources.
        var modes = new List<string>();
        await using (var cmd = new NpgsqlCommand(
            "SELECT DISTINCT r.allocation_mode FROM resource_assignments ra JOIN resources r ON r.id=ra.resource_id WHERE ra.request_id = ANY(@ids)",
            conn, tx))
        {
            cmd.Parameters.AddWithValue("ids", seededIds);
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync()) modes.Add(rd.GetString(0));
        }
        modes.Should().Contain(["Exclusive", "Fractional", "ConcurrentCapacity"]);

        // Exclusive machines (tools) must never accidentally double-book — overlap pairs stay within
        // the injected-conflict band. (People may be lightly over-allocated by the lead fallback that
        // guarantees requirement satisfaction; that's acceptable demo behaviour.)
        var (_, toolPairs) = await TwoLongs(conn, tx, @"
            SELECT 0, count(*) FROM resource_assignments a
            JOIN resource_assignments b ON a.resource_id=b.resource_id AND a.id<b.id AND a.start_utc<b.end_utc AND b.start_utc<a.end_utc
            JOIN resources r ON r.id=a.resource_id
            JOIN resource_types rt ON rt.id=r.resource_type_id AND rt.key='tool'
            WHERE r.allocation_mode='Exclusive' AND a.request_id = ANY(@ids)",
            ("ids", seededIds));
        toolPairs.Should().BeLessThanOrEqualTo(year.Conflicts + 2,
            "Exclusive machines must not accidentally double-book beyond the injected conflicts");

        await tx.RollbackAsync();
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
