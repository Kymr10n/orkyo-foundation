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
    int AvailabilityEvents = 0, int Absences = 0, int Conflicts = 0,
    int Machines = 0, int MachineTypes = 0, int ListRows = 0, int CustomFields = 0,
    int MachineGroups = 0, int Dependencies = 0);

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
                    $"Profile '{profile.Slug}' has no floorplan set. " +
                    "Use --profile manufacturing, or pass --floorplans false to seed without floorplans.");
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

        // Space groups: the curated floorplan path groups by functional area (CNC/QC/storage/…);
        // the generic generator has no functional codes, so it keeps round-robin assignment.
        IReadOnlyList<SpaceFactories.SeededSpaceGroup> spaceGroups;
        int spaceGroupMemberCount;
        if (opts.UseFloorplans)
        {
            (spaceGroups, spaceGroupMemberCount) =
                await SpaceFactories.SeedFunctionalSpaceGroupsAsync(conn, spaces, spaceTypeId);
        }
        else
        {
            spaceGroups = await SpaceFactories.SeedSpaceGroupsAsync(conn, tx, profile, scale, faker, spaceTypeId);
            spaceGroupMemberCount = await SpaceFactories.SeedSpaceGroupMembersAsync(
                conn, tx, faker, spaces, spaceGroups, spaceTypeId);
        }

        // The person type must be resolved first: the organization lists hang their lookup fields
        // off it.
        var personTypeId = await PeopleFactories.ResolvePersonResourceTypeIdAsync(conn, tx);
        var orgLists = await PeopleFactories.SeedOrganizationListsAsync(
            conn, tx, profile, scale, personTypeId);
        var jobTitles = orgLists.JobTitles;
        var departments = orgLists.Departments;
        // The floorplan path assigns job title and department by persona once the cohorts exist,
        // so it asks for neither here — a random pair now would only be overwritten later.
        var people = await PeopleFactories.SeedPeopleAsync(
            conn, tx, profile, scale, faker, personTypeId, jobTitles, departments,
            assignOrgFields: !opts.UseFloorplans);

        // Person groups: the floorplan path groups by team/role (derived from the skills assigned
        // below in the narrative block); the generic path keeps round-robin. Assigned per-branch.
        IReadOnlyList<PeopleFactories.SeededPersonGroup> personGroups = [];
        var personGroupMemberCount = 0;

        int criteriaCount, requestCount, assignmentCount;
        int tools = 0, capabilities = 0, requirements = 0, events = 0, absences = 0, conflicts = 0;
        var dependencies = 0;
        int machines = 0, machineTypes = 0, listRows = 0, customFields = 0, machineGroups = 0;

        if (opts.UseFloorplans)
        {
            // ── The relatable year: coherent per-facility operations exercising every aspect ──
            var facilities = Narrative.FacilityModel.All;
            IReadOnlyList<ToolFactory.SeededTool> seededTools = await ToolFactory.SeedAsync(conn, facilities, sites);

            // The machines: tenant-defined placeable types, their custom fields, and the lists
            // those fields bind to. Ordered by dependency — the value documents need the shared
            // list's row ids, and the per-resource instances need both the machines and the fields.
            var machineTypeIds = await MachineFactory.SeedTypesAsync(conn);
            var lists = await MachineListFactory.SeedDefinitionsAsync(conn);
            var machineFields = await MachineListFactory.SeedFieldsAsync(conn, machineTypeIds, lists);
            var valueDocs = MachineListFactory.BuildValueDocuments(Narrative.MachineCatalog.All, lists, faker);
            var seededMachines = await MachineFactory.SeedMachinesAsync(conn, sites, machineTypeIds, valueDocs);
            var machineCells = await MachineFactory.SeedGroupsAsync(conn, machineTypeIds, seededMachines);
            listRows = lists.ToolingRowIds.Count + lists.ConsumablesRowIds.Count
                + await MachineListFactory.SeedMaintenanceHistoryAsync(conn, seededMachines, machineFields, faker);

            // Configuration the shop runs on, and the fields a tenant hangs off the built-in types.
            await TenantConfigFactory.SeedSchedulingSettingsAsync(conn, sites);
            var builtIn = await TenantConfigFactory.SeedBuiltInCustomFieldsAsync(
                conn, lists.ConsumablesInstanceId, lists.ConsumablesRowIds, faker);
            customFields = machineFields.Ids.Count + builtIn.Fields;

            var skillCriteria = await CapabilityFactory.SeedSkillCriteriaAsync(conn);
            var cohorts = Narrative.Cohorts.Build(facilities, sites, spaces, people, seededTools, seededMachines);

            // Job title and department by role, now that the cohorts say who works where. People
            // were inserted without them for exactly this reason.
            await PersonaFactory.ApplyAsync(conn, cohorts, jobTitles, departments);

            // Pin each cohort's people to their facility site so cohort work stays same-site; the
            // post-commit round-robin in SiteModelFactory then only fills any people left un-sited.
            await SiteModelFactory.ApplyCohortSitesAsync(conn, tx,
                cohorts.SelectMany(c => c.People.Select(p => (p.ResourceId, c.SiteId))).ToList());

            var caps = await CapabilityFactory.AssignAsync(conn, skillCriteria, cohorts, faker);

            // Person groups by team/role, derived from the skills just assigned.
            (personGroups, personGroupMemberCount) = await PeopleFactories.SeedRoleGroupsAndMembersAsync(
                conn, people, caps.PersonSkills, skillCriteria, personTypeId);

            var calendar = new Narrative.YearCalendar(opts.ReferenceDate);
            var avail = await AvailabilityFactory.SeedAsync(conn, calendar, sites, people, faker);
            var year = await Narrative.NarrativeYearSeeder.SeedAsync(
                conn, cohorts, skillCriteria, caps.PersonSkills, calendar, scale, faker, avail.Vacations);

            tools = seededTools.Count;
            machines = seededMachines.Count;
            machineTypes = machineTypeIds.Count;
            machineGroups = machineCells.Groups;
            await TenantConfigFactory.SeedCriteriaTemplatesAsync(conn, skillCriteria);
            await TenantConfigFactory.SeedGroupCapabilitiesAsync(conn, skillCriteria);
            criteriaCount = skillCriteria.Count;
            capabilities = caps.Total;
            events = avail.Events;
            absences = avail.Absences;
            requestCount = year.Requests;
            requirements = year.Requirements;
            assignmentCount = year.Assignments;
            conflicts = year.Conflicts;
            dependencies = year.Dependencies;
        }
        else
        {
            personGroups = await PeopleFactories.SeedPersonGroupsAsync(
                conn, tx, profile, scale, faker, personTypeId);
            personGroupMemberCount = await PeopleFactories.SeedPersonGroupMembersAsync(
                conn, tx, faker, people, personGroups, personTypeId);

            var criteria = await CriteriaFactory.SeedCriteriaAsync(conn, tx, scale, faker);
            var requests = await WorkItemFactories.SeedRequestsAsync(conn, tx, profile, scale, faker, timing);
            assignmentCount = await WorkItemFactories.SeedAssignmentsAsync(conn, tx, faker, requests, people, spaces);
            criteriaCount = criteria.Count;
            requestCount = requests.Count;
        }

        await tx.CommitAsync();

        // Populate the Home-Site / Current-Site model on the committed rows (see SiteModelFactory).
        // Post-commit so it sees data from both the floorplan and generic paths uniformly.
        await SiteModelFactory.ApplyAsync(conn, spaceTypeId, personTypeId);

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
            Conflicts: conflicts,
            Machines: machines,
            MachineTypes: machineTypes,
            ListRows: listRows,
            CustomFields: customFields,
            MachineGroups: machineGroups,
            Dependencies: dependencies);
    }
}
