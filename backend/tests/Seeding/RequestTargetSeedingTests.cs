using Api.Services;
using Bogus;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using Orkyo.Foundation.Seed;
using Orkyo.Foundation.Seed.Factories;
using Orkyo.Foundation.Seed.Floorplans;
using Orkyo.Foundation.Seed.Narrative;
using Orkyo.Foundation.Seed.Scales;
using Xunit;

namespace Orkyo.Foundation.Tests.Seeding;

/// <summary>
/// Integration test for the two facts a fresh seed depends on but nothing else asserts: every
/// seeded request declares its target resource types (migration 1720 only backfilled the requests
/// that existed when it ran — freshly COPYed ones get nothing unless the seeder writes them), and
/// tools are part of the default seed. Without the targets every request fails the scheduled
/// predicate forever, so utilization and insights come up empty on a fresh demo.
/// </summary>
[Collection("Database collection")]
public class RequestTargetSeedingTests
{
    private readonly IOrgDbConnectionFactory _connFactory;
    private readonly OrgContext _orgContext;

    public RequestTargetSeedingTests(DatabaseFixture fixture)
    {
        var scope = fixture.Factory.Services.CreateScope();
        _connFactory = scope.ServiceProvider.GetRequiredService<IOrgDbConnectionFactory>();
        _orgContext = scope.ServiceProvider.GetRequiredService<OrgContext>();
    }

    [Fact]
    public async Task DefaultSeed_TargetsResourceTypes_AndSchedulesRequests()
    {
        await using var conn = _connFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var faker = new Faker { Random = new Randomizer(1337) };

        var spaceTypeId = await SpaceFactories.ResolveSpaceResourceTypeIdAsync(conn, tx);
        var personTypeId = await ScalarGuid(conn, tx, "SELECT id FROM resource_types WHERE key='person' LIMIT 1");

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

        var facilities = FacilityModel.All;
        var tools = await ToolFactory.SeedAsync(conn, facilities, fp.Sites);
        // Machines are part of the narrative now — mills, drills and assembly stations carry the
        // work that used to be booked onto tools of the same name.
        var machineTypeIds = await MachineFactory.SeedTypesAsync(conn);
        var lists = await MachineListFactory.SeedDefinitionsAsync(conn);
        var machineFields = await MachineListFactory.SeedFieldsAsync(conn, machineTypeIds, lists);
        var machines = await MachineFactory.SeedMachinesAsync(
            conn, fp.Sites, machineTypeIds,
            MachineListFactory.BuildValueDocuments(MachineCatalog.All, lists, faker));
        var criteria = await CapabilityFactory.SeedSkillCriteriaAsync(conn);
        var cohorts = Cohorts.Build(facilities, fp.Sites, fp.Spaces, people, tools, machines);
        var caps = await CapabilityFactory.AssignAsync(conn, criteria, cohorts, faker);
        var cal = new YearCalendar(DateTime.UtcNow);
        var avail = await AvailabilityFactory.SeedAsync(conn, cal, fp.Sites, people, faker);
        var year = await NarrativeYearSeeder.SeedAsync(
            conn, cohorts, criteria, caps.PersonSkills, cal, ScaleCatalog.Resolve("tiny"), faker,
            avail.Vacations);

        // Scope every assertion to this run's rows — the shared fixture DB carries committed data.
        var seededIds = year.RequestIds.ToArray();
        var toolIds = tools.Select(t => t.Id).ToArray();

        // ── Tools are seeded by default, capability-tagged, and actually booked ──────────────
        tools.Should().NotBeEmpty("the default seed includes tool resources");
        var (_, toolCaps) = await TwoLongs(conn, tx,
            "SELECT 0, count(*) FROM resource_capabilities WHERE resource_id = ANY(@toolIds)",
            ("toolIds", toolIds));
        toolCaps.Should().BeGreaterThan(0, "seeded tools carry capabilities (CNC operation, max load)");

        // Its facility must be data, not a prefix on the name: the utilization tab filters by
        // site, so a tool without a home site is missing from its own facility's board.
        var (_, unsitedTools) = await TwoLongs(conn, tx,
            "SELECT 0, count(*) FROM resources WHERE id = ANY(@toolIds) AND home_site_id IS NULL",
            ("toolIds", toolIds));
        unsitedTools.Should().Be(0, "every seeded tool is homed at the facility that owns it");
        var (_, toolAssignments) = await TwoLongs(conn, tx,
            "SELECT 0, count(*) FROM resource_assignments WHERE request_id = ANY(@ids) AND resource_id = ANY(@toolIds)",
            ("ids", seededIds), ("toolIds", toolIds));
        toolAssignments.Should().BeGreaterThan(0, "the narrative allocates tools to requests");

        // ── Every seeded request declares at least one target type ──────────────────────────
        var (totalRequests, untargeted) = await TwoLongs(conn, tx, @"
            SELECT count(*),
                   count(*) FILTER (WHERE NOT EXISTS (
                       SELECT 1 FROM request_target_resource_types t WHERE t.request_id = r.id))
            FROM requests r WHERE r.id = ANY(@ids)",
            ("ids", seededIds));
        totalRequests.Should().Be(seededIds.Length);
        untargeted.Should().Be(0, "a request with no target types can never report itself scheduled");

        // ── 'tool' is targeted exactly where a tool was assigned ────────────────────────────
        var (toolTargeted, mismatched) = await TwoLongs(conn, tx, @"
            WITH r AS (
                SELECT req.id,
                       EXISTS (SELECT 1 FROM request_target_resource_types t
                               JOIN resource_types rt ON rt.id = t.resource_type_id AND rt.key = 'tool'
                                WHERE t.request_id = req.id) AS targets_tool,
                       EXISTS (SELECT 1 FROM resource_assignments ra
                                WHERE ra.request_id = req.id AND ra.resource_id = ANY(@toolIds)) AS has_tool
                FROM requests req WHERE req.id = ANY(@ids))
            SELECT count(*) FILTER (WHERE targets_tool),
                   count(*) FILTER (WHERE targets_tool <> has_tool)
            FROM r",
            ("ids", seededIds), ("toolIds", toolIds));
        toolTargeted.Should().BeGreaterThan(0, "tool-using jobs must declare the tool target");
        mismatched.Should().Be(0,
            "'tool' is targeted from the assignments actually written — targeting a tool a request never got would leave it permanently unscheduled");

        // ── The real predicate: the insights view must see a schedule, not a phantom backlog ─
        var (scheduledWindows, isScheduled) = await TwoLongs(conn, tx, @"
            SELECT count(*) FILTER (WHERE start_ts IS NOT NULL AND end_ts IS NOT NULL),
                   count(*) FILTER (WHERE is_scheduled)
            FROM analytics_request_summary_v WHERE request_id = ANY(@ids)",
            ("ids", seededIds));
        scheduledWindows.Should().BeGreaterThan(0);
        isScheduled.Should().BeGreaterThan(scheduledWindows / 2,
            "most requests with a booked window must satisfy the scheduled predicate — this is the assertion an unseeded target table fails");

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
