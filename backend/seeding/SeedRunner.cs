using Bogus;
using Npgsql;
using Orkyo.Foundation.Seed.Distributions;
using Orkyo.Foundation.Seed.Factories;
using Orkyo.Foundation.Seed.Profiles;
using Orkyo.Foundation.Seed.Scales;

namespace Orkyo.Foundation.Seed;

public sealed record SeedReport(
    int Sites, int Spaces, int FloorplanAssets, int SpaceGroups, int SpaceGroupMembers,
    int JobTitles, int Departments, int People,
    int PersonGroups, int PersonGroupMembers,
    int Criteria,
    int Requests, int Assignments, TimeSpan Duration,
    int Tools = 0, int Capabilities = 0, int Requirements = 0,
    int AvailabilityEvents = 0, int Absences = 0, int Templates = 0, int Conflicts = 0);

/// <summary>
/// Orchestrates an end-to-end seed run against an open Npgsql connection.
///
/// Order:
///   1. SafetyGuard refuses non-local without --force.
///   2. Open a single transaction (everything atomic — partial seeds are confusing).
///   3. Optionally truncate (Mode=Reset).
///   4. Run factories in FK-safe order.
///   5. Commit. Print row counts.
/// </summary>
public static class SeedRunner
{
    public static async Task<SeedReport> RunAsync(NpgsqlConnection conn, SeedOptions opts)
    {
        SafetyGuard.AssertLocalOrForced(conn, opts);

        var profile = ProfileCatalog.Resolve(opts.Profile);
        var scale = ScaleCatalog.Resolve(opts.Scale);

        var randomSeed = opts.UseRandom ? Random.Shared.Next() : opts.RandomSeed;
        Randomizer.Seed = new Random(randomSeed);
        var faker = new Faker { Random = new Randomizer(randomSeed) };

        var timing = new RequestTimeDistribution(opts.ReferenceDate, scale.TimeWindowDays, faker);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        await using var tx = await conn.BeginTransactionAsync();

        if (opts.Mode == SeedMode.Reset)
            await TenantReset.TruncateAllAsync(conn, tx);

        var spaceTypeId = await SpaceFactories.ResolveSpaceResourceTypeIdAsync(conn, tx);

        IReadOnlyList<SpaceFactories.SeededSite> sites;
        IReadOnlyList<SpaceFactories.SeededSpace> spaces;
        var floorplanAssets = 0;

        if (opts.UseFloorplans)
        {
            var fixtures = Floorplans.FloorplanCatalog.ForProfile(profile.Slug);
            if (fixtures.Count == 0)
                throw new InvalidOperationException(
                    $"--floorplans requested but profile '{profile.Slug}' has no floorplan set. " +
                    "Use --profile manufacturing, or drop --floorplans.");
            if (opts.TenantId == Guid.Empty)
                throw new InvalidOperationException(
                    "--floorplans requires a tenant id (assets.tenant_id). The seed CLI resolves it from control_plane.tenants.");

            var fp = await FloorplanFactory.SeedAsync(conn, opts.TenantId, fixtures, spaceTypeId);
            sites = fp.Sites;
            spaces = fp.Spaces;
            floorplanAssets = fp.Assets;
        }
        else
        {
            sites = await SpaceFactories.SeedSitesAsync(conn, tx, profile, scale, faker);
            spaces = await SpaceFactories.SeedSpacesAsync(conn, tx, profile, scale, faker, sites, spaceTypeId);
        }

        var spaceGroups = await SpaceFactories.SeedSpaceGroupsAsync(conn, tx, profile, scale, faker, spaceTypeId);
        var spaceGroupMemberCount = await SpaceFactories.SeedSpaceGroupMembersAsync(
            conn, tx, faker, spaces, spaceGroups, spaceTypeId);

        var jobTitles = await PeopleFactories.SeedJobTitlesAsync(conn, tx, profile, scale, faker);
        var departments = await PeopleFactories.SeedDepartmentsAsync(conn, tx, profile, scale, faker);
        var personTypeId = await PeopleFactories.ResolvePersonResourceTypeIdAsync(conn, tx);
        var people = await PeopleFactories.SeedPeopleAsync(
            conn, tx, profile, scale, faker, personTypeId, jobTitles, departments);
        var personGroups = await PeopleFactories.SeedPersonGroupsAsync(
            conn, tx, profile, scale, faker, personTypeId);
        var personGroupMemberCount = await PeopleFactories.SeedPersonGroupMembersAsync(
            conn, tx, faker, people, personGroups, personTypeId);

        int criteriaCount, requestCount, assignmentCount;
        int tools = 0, capabilities = 0, requirements = 0, events = 0, absences = 0, templates = 0, conflicts = 0;

        if (opts.UseFloorplans)
        {
            // ── The relatable year: coherent per-facility operations exercising every aspect ──
            var facilities = Narrative.FacilityModel.All;
            var seededTools = await ToolFactory.SeedAsync(conn, facilities);
            var skillCriteria = await CapabilityFactory.SeedSkillCriteriaAsync(conn);
            var cohorts = Narrative.Cohorts.Build(facilities, sites, spaces, people, seededTools);
            var caps = await CapabilityFactory.AssignAsync(conn, skillCriteria, cohorts, faker);
            var calendar = new Narrative.YearCalendar(opts.ReferenceDate);
            var avail = await AvailabilityFactory.SeedAsync(conn, calendar, sites, people, faker);
            templates = await TemplateFactory.SeedAsync(conn, skillCriteria);
            var year = await Narrative.NarrativeYearSeeder.SeedAsync(
                conn, cohorts, skillCriteria, caps.PersonSkills, calendar, scale, faker);

            tools = seededTools.Count;
            criteriaCount = skillCriteria.Count;
            capabilities = caps.Total;
            events = avail.Events;
            absences = avail.Absences;
            requestCount = year.Requests;
            requirements = year.Requirements;
            assignmentCount = year.Assignments;
            conflicts = year.Conflicts;
        }
        else
        {
            var criteria = await CriteriaFactory.SeedCriteriaAsync(conn, tx, scale, faker);
            var requests = await WorkItemFactories.SeedRequestsAsync(conn, tx, profile, scale, faker, timing);
            assignmentCount = await WorkItemFactories.SeedAssignmentsAsync(conn, tx, faker, requests, people, spaces);
            criteriaCount = criteria.Count;
            requestCount = requests.Count;
        }

        await tx.CommitAsync();
        sw.Stop();

        return new SeedReport(
            Sites: sites.Count,
            Spaces: spaces.Count,
            FloorplanAssets: floorplanAssets,
            SpaceGroups: spaceGroups.Count,
            SpaceGroupMembers: spaceGroupMemberCount,
            JobTitles: jobTitles.Count,
            Departments: departments.Count,
            People: people.Count,
            PersonGroups: personGroups.Count,
            PersonGroupMembers: personGroupMemberCount,
            Criteria: criteriaCount,
            Requests: requestCount,
            Assignments: assignmentCount,
            Duration: sw.Elapsed,
            Tools: tools,
            Capabilities: capabilities,
            Requirements: requirements,
            AvailabilityEvents: events,
            Absences: absences,
            Templates: templates,
            Conflicts: conflicts);
    }
}
