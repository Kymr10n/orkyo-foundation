using System.Text.Json;
using Bogus;
using Npgsql;
using NpgsqlTypes;
using Orkyo.Foundation.Seed.Narrative;
using Orkyo.Foundation.Seed.Profiles;
using Orkyo.Foundation.Seed.Scales;

namespace Orkyo.Foundation.Seed.Factories;

/// <summary>
/// Seeds the people side: the two organization lists (job titles, departments), person
/// resources, and person groups.
///
/// Job titles and departments became organization-scoped lists in migration 1820, so this seeds
/// list rows rather than table rows, and adopts what the migration already created rather than
/// inserting a second copy. Resources are created with allocation_mode='Fractional' and
/// base_availability_percent=100, matching the production person default.
/// </summary>
public static class PeopleFactories
{
    /// <summary>A job-title list row. <c>Id</c> is the row id a person's lookup value holds.</summary>
    public sealed record SeededJobTitle(Guid Id, string Name);
    /// <summary>A department list row. <c>Id</c> is the row id a person's lookup value holds.</summary>
    public sealed record SeededDepartment(Guid Id, string Name);
    public sealed record SeededOrgLists(
        IReadOnlyList<SeededJobTitle> JobTitles,
        IReadOnlyList<SeededDepartment> Departments);
    public sealed record SeededPerson(Guid ResourceId, string Name);
    public sealed record SeededPersonGroup(Guid Id, string Name);

    /// <summary>
    /// The type the seeded people belong to. The demo defines it for itself — the same pattern
    /// as <see cref="SpaceFactories.ResolveSpaceResourceTypeIdAsync"/> — because `person` is no
    /// longer seeded by migration: it is a catalog type a tenant activates, and a fresh DB has
    /// no types at all. Values mirror the `person` entry of ResourceTypeCatalog (backend/core);
    /// the seeding project deliberately has no reference to core, so they are duplicated here.
    /// </summary>
    public static async Task<Guid> ResolvePersonResourceTypeIdAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        var id = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO public.resource_types " +
            "(id, key, display_name, display_name_plural, description, icon, " +
            " is_system, is_active, has_directory_profile, created_at, updated_at) " +
            "VALUES (@id, 'person', 'Person', 'People', " +
            "'Operators, fitters and everyone else who does the work.', " +
            "'Users', false, true, true, @now, @now) " +
            "ON CONFLICT (key) DO UPDATE SET updated_at = EXCLUDED.updated_at " +
            "RETURNING id", conn, tx);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("now", DateTime.UtcNow);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// Job titles and departments, seeded as the two organization lists migration 1820 defines.
    /// A reset truncates <c>list_definitions</c>, so the seed builds them rather than assuming the
    /// migration's copies survived.
    /// </summary>
    /// <remarks>
    /// Departments keep their two levels as a descriptive <c>parent</c> cell. The tree stopped
    /// being a database relationship in 1820, so a child names its parent instead of pointing at it.
    /// </remarks>
    // Subdivision suffixes for child departments — generic enough to work across all profiles;
    // combined with the parent name (for example "Operations North").
    private static readonly string[] DeptSubdivisions =
        ["North", "South", "East", "West", "Central", "Alpha", "Beta", "Gamma", "Delta", "Epsilon"];

    public static async Task<SeededOrgLists> SeedOrganizationListsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        IProfile profile, IScale scale, Guid personResourceTypeId)
    {
        var now = DateTime.UtcNow;
        _ = tx;

        var jobTitles = await SeedOrgListAsync(
            conn, now, "Job Titles", "Roles a person holds.",
            [("description", "Description"), ("active", "Active")],
            BuildJobTitleNames(profile, scale));

        var departments = await SeedOrgListAsync(
            conn, now, "Departments", "Organizational units.",
            [("code", "Code"), ("description", "Description"), ("parent", "Parent"), ("active", "Active")],
            BuildDepartmentRows(profile, scale));

        // Bound to the directory type. DO NOTHING on conflict, because migration 1820 creates the
        // same two fields — an append-mode seed runs over a database that already has them.
        await ListSeedHelpers.InsertCustomFieldAsync(
            conn, personResourceTypeId, "department", "Department", "list_lookup",
            required: false, sort: 100, now, listInstanceId: departments.InstanceId);
        await ListSeedHelpers.InsertCustomFieldAsync(
            conn, personResourceTypeId, "job_title", "Job Title", "list_lookup",
            required: false, sort: 101, now, listInstanceId: jobTitles.InstanceId);

        return new SeededOrgLists(
            jobTitles.Rows.Select(r => new SeededJobTitle(r.Id, r.Name)).ToList(),
            departments.Rows.Select(r => new SeededDepartment(r.Id, r.Name)).ToList());
    }

    private sealed record SeededOrgList(Guid InstanceId, IReadOnlyList<(Guid Id, string Name)> Rows);

    /// <summary>
    /// Creates one organization list and fills it, or adopts the one migration 1820 already made.
    /// The first column is always <c>name</c> and is the display column; the rest are declared by
    /// the caller. A row is a dictionary keyed by column key.
    /// </summary>
    private static async Task<SeededOrgList> SeedOrgListAsync(
        NpgsqlConnection conn, DateTime now, string name, string description,
        IReadOnlyList<(string Key, string Label)> extraColumns,
        IReadOnlyList<Dictionary<string, object>> rows)
    {
        const string scope = "organization";
        var definitionId = await ListSeedHelpers.FindDefinitionAsync(conn, scope, name);

        if (definitionId is null)
        {
            definitionId = await ListSeedHelpers.InsertDefinitionAsync(conn, name, description, now, scope);
            var nameColumn = await ListSeedHelpers.InsertColumnAsync(
                conn, definitionId.Value, "name", "Name", "text", required: true, sort: 0, now);
            for (var i = 0; i < extraColumns.Count; i++)
            {
                var (key, label) = extraColumns[i];
                await ListSeedHelpers.InsertColumnAsync(
                    conn, definitionId.Value, key, label,
                    key == "active" ? "boolean" : "text", required: false, sort: i + 1, now);
            }
            await ListSeedHelpers.SetDisplayColumnAsync(conn, definitionId.Value, nameColumn);
        }

        var instanceId = await ListSeedHelpers.FindSharedInstanceAsync(conn, definitionId.Value)
            ?? await ListSeedHelpers.InsertSharedInstanceAsync(conn, definitionId.Value, name, now);

        var seeded = new List<(Guid, string)>(rows.Count);
        foreach (var values in rows)
        {
            var rowId = await ListSeedHelpers.InsertRowAsync(
                conn, instanceId, JsonSerializer.Serialize(values), now);
            seeded.Add((rowId, (string)values["name"]));
        }

        return new SeededOrgList(instanceId, seeded);
    }

    private static List<Dictionary<string, object>> BuildJobTitleNames(IProfile profile, IScale scale)
    {
        var pool = profile.JobTitlePool;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<Dictionary<string, object>>();
        for (var i = 0; rows.Count < scale.JobTitles && i < scale.JobTitles * 5; i++)
        {
            var baseName = pool[i % pool.Count];
            var candidate = i < pool.Count ? baseName : $"{baseName} {i / pool.Count + 1}";
            if (seen.Add(candidate))
                rows.Add(new Dictionary<string, object> { ["name"] = candidate, ["active"] = true });
        }
        return rows;
    }

    /// <remarks>
    /// Two levels, kept as a descriptive <c>parent</c> cell. The tree stopped being a database
    /// relationship in 1820, so a child names its parent instead of pointing at it.
    /// </remarks>
    private static List<Dictionary<string, object>> BuildDepartmentRows(IProfile profile, IScale scale)
    {
        var pool = profile.DepartmentRootPool;
        var rootCount = Math.Max(2, scale.Departments / 3);
        var childCount = scale.Departments - rootCount;

        var rootNames = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; rootNames.Count < rootCount && i < rootCount * 5; i++)
        {
            var baseName = pool[i % pool.Count];
            var candidate = i < pool.Count ? baseName : $"{baseName} {i / pool.Count + 1}";
            if (seen.Add(candidate)) rootNames.Add(candidate);
        }

        var rows = rootNames
            .Select(n => new Dictionary<string, object> { ["name"] = n, ["active"] = true })
            .ToList();

        var counters = new int[rootNames.Count];
        for (var i = 0; i < childCount; i++)
        {
            var parentIdx = i % rootNames.Count;
            var suffix = DeptSubdivisions[counters[parentIdx]++ % DeptSubdivisions.Length];
            rows.Add(new Dictionary<string, object>
            {
                ["name"] = $"{rootNames[parentIdx]} {suffix}",
                ["parent"] = rootNames[parentIdx],
                ["active"] = true,
            });
        }
        return rows;
    }

    public static async Task<IReadOnlyList<SeededPerson>> SeedPeopleAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        IProfile profile, IScale scale, Faker faker,
        Guid personResourceTypeId,
        IReadOnlyList<SeededJobTitle> jobTitles,
        IReadOnlyList<SeededDepartment> departments)
    {
        var now = DateTime.UtcNow;
        var people = new List<SeededPerson>(scale.People);

        // Email is a column on resources (migration 1700). Job title and department are list
        // lookups since 1820, so they ride in custom_fields as arrays of row ids — the same shape
        // the API validates and the resource form writes.
        _ = tx;
        using (var writer = await conn.BeginBinaryImportAsync(
            "COPY public.resources (id, resource_type_id, name, allocation_mode, base_availability_percent, is_active, " +
            "email, custom_fields, created_at, updated_at) " +
            "FROM STDIN (FORMAT BINARY)"))
        {
            for (var i = 0; i < scale.People; i++)
            {
                var id = Guid.NewGuid();
                var fullName = faker.Name.FullName();
                var email = $"{Slugify(fullName)}@orkyo.example";
                var jobTitleId = jobTitles.Count == 0 ? (Guid?)null : jobTitles[faker.Random.Int(0, jobTitles.Count - 1)].Id;
                var deptId = departments.Count == 0 ? (Guid?)null : departments[faker.Random.Int(0, departments.Count - 1)].Id;
                var customFields = new Dictionary<string, object>();
                if (jobTitleId is not null) customFields["job_title"] = new[] { jobTitleId.Value.ToString() };
                if (deptId is not null) customFields["department"] = new[] { deptId.Value.ToString() };

                await writer.StartRowAsync();
                await writer.WriteAsync(id, NpgsqlDbType.Uuid);
                await writer.WriteAsync(personResourceTypeId, NpgsqlDbType.Uuid);
                await writer.WriteAsync(fullName, NpgsqlDbType.Varchar);
                await writer.WriteAsync("Fractional", NpgsqlDbType.Varchar);
                await writer.WriteAsync(100, NpgsqlDbType.Integer);
                await writer.WriteAsync(true, NpgsqlDbType.Boolean);
                await writer.WriteAsync(email, NpgsqlDbType.Citext);
                if (customFields.Count == 0) await writer.WriteNullAsync();
                else await writer.WriteAsync(JsonSerializer.Serialize(customFields), NpgsqlDbType.Jsonb);
                await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
                await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
                people.Add(new SeededPerson(id, fullName));
            }
            await writer.CompleteAsync();
        }

        _ = profile;
        return people;
    }

    public static async Task<IReadOnlyList<SeededPersonGroup>> SeedPersonGroupsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        IProfile profile, IScale scale, Faker faker,
        Guid personResourceTypeId)
    {
        var pool = profile.PersonGroupPool;
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; names.Count < scale.ResourceGroups && i < scale.ResourceGroups * 5; i++)
        {
            var baseName = pool[i % pool.Count];
            var candidate = i < pool.Count ? baseName : $"{baseName} {i / pool.Count + 1}";
            if (seen.Add(candidate)) names.Add(candidate);
        }

        var seeded = new List<SeededPersonGroup>(names.Count);
        var now = DateTime.UtcNow;
        _ = tx;
        using var writer = await conn.BeginBinaryImportAsync(
            "COPY public.resource_groups (id, name, description, color, display_order, resource_type_id, created_at, updated_at) " +
            "FROM STDIN (FORMAT BINARY)");

        for (var i = 0; i < names.Count; i++)
        {
            var id = Guid.NewGuid();
            await writer.StartRowAsync();
            await writer.WriteAsync(id, NpgsqlDbType.Uuid);
            await writer.WriteAsync(names[i], NpgsqlDbType.Varchar);
            await writer.WriteNullAsync();                               // description
            await writer.WriteNullAsync();                               // color
            await writer.WriteAsync(i, NpgsqlDbType.Integer);           // display_order
            await writer.WriteAsync(personResourceTypeId, NpgsqlDbType.Uuid);
            await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
            await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
            seeded.Add(new SeededPersonGroup(id, names[i]));
        }
        await writer.CompleteAsync();
        _ = faker;
        return seeded;
    }

    public static async Task<int> SeedPersonGroupMembersAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Faker faker,
        IReadOnlyList<SeededPerson> people,
        IReadOnlyList<SeededPersonGroup> groups,
        Guid personResourceTypeId)
    {
        if (groups.Count == 0 || people.Count == 0) return 0;

        // Each person gets one primary group (round-robin for even distribution).
        // ~25 % also get a second distinct group.
        var memberships = new List<(Guid GroupId, Guid ResourceId)>(
            (int)(people.Count * 1.25));

        for (var i = 0; i < people.Count; i++)
        {
            var primaryGroup = groups[i % groups.Count];
            memberships.Add((primaryGroup.Id, people[i].ResourceId));

            if (faker.Random.Bool(0.25f))
            {
                var secondaryGroup = groups[faker.Random.Int(0, groups.Count - 1)];
                if (secondaryGroup.Id != primaryGroup.Id)
                    memberships.Add((secondaryGroup.Id, people[i].ResourceId));
            }
        }

        _ = tx;
        using var writer = await conn.BeginBinaryImportAsync(
            "COPY public.resource_group_members (resource_group_id, resource_id, resource_type_id) " +
            "FROM STDIN (FORMAT BINARY)");

        foreach (var (groupId, resourceId) in memberships)
        {
            await writer.StartRowAsync();
            await writer.WriteAsync(groupId, NpgsqlDbType.Uuid);
            await writer.WriteAsync(resourceId, NpgsqlDbType.Uuid);
            await writer.WriteAsync(personResourceTypeId, NpgsqlDbType.Uuid);
        }
        await writer.CompleteAsync();
        return memberships.Count;
    }

    /// <summary>Team display order; the catch-all "Production Crew" comes first.</summary>
    private static readonly string[] TeamOrder =
    [
        "Production Crew", "Quality Team", "Maintenance Team", "Logistics Team",
    ];

    /// <summary>
    /// Maps a person's skill set to a single team, by precedence: specialist skills
    /// (Quality → Maintenance → Logistics) win over the general "Production Crew", which is also
    /// the fallback for anyone without a mapped skill.
    /// </summary>
    public static string TeamForSkills(IEnumerable<string> skillKeys)
    {
        var keys = skillKeys as IReadOnlySet<string> ?? skillKeys.ToHashSet();
        if (keys.Contains(SkillCatalog.QaInspection)) return "Quality Team";
        if (keys.Contains(SkillCatalog.Maintenance)) return "Maintenance Team";
        if (keys.Contains(SkillCatalog.ForkliftLicense) || keys.Contains(SkillCatalog.CraneOperation))
            return "Logistics Team";
        return "Production Crew";
    }

    /// <summary>
    /// Seeds person groups + memberships by team/role (curated-floorplan path), derived from the
    /// skills assigned by <see cref="CapabilityFactory"/>. One group per team that has at least one
    /// member; each person joins exactly one team. Replaces the round-robin assignment for the demo.
    /// </summary>
    public static async Task<(IReadOnlyList<SeededPersonGroup> Groups, int MemberCount)> SeedRoleGroupsAndMembersAsync(
        NpgsqlConnection conn,
        IReadOnlyList<SeededPerson> people,
        IReadOnlyDictionary<Guid, HashSet<Guid>> personSkills,
        IReadOnlyDictionary<string, Guid> criteriaByKey,
        Guid personResourceTypeId)
    {
        if (people.Count == 0) return ([], 0);

        // Invert skillKey→criterionId so we can name each person's skills.
        var keyByCriterion = criteriaByKey.ToDictionary(kv => kv.Value, kv => kv.Key);

        string TeamFor(SeededPerson p)
        {
            var keys = personSkills.TryGetValue(p.ResourceId, out var crit)
                ? crit.Select(c => keyByCriterion.TryGetValue(c, out var k) ? k : null).OfType<string>()
                : [];
            return TeamForSkills(keys);
        }

        var teamToPeople = people
            .GroupBy(TeamFor)
            .ToDictionary(g => g.Key, g => g.ToList());
        var presentTeams = TeamOrder.Where(teamToPeople.ContainsKey).ToList();

        var seeded = new List<SeededPersonGroup>(presentTeams.Count);
        var teamToGroupId = new Dictionary<string, Guid>(presentTeams.Count);
        var now = DateTime.UtcNow;

        using (var groupWriter = await conn.BeginBinaryImportAsync(
            "COPY public.resource_groups (id, name, description, color, display_order, resource_type_id, created_at, updated_at) " +
            "FROM STDIN (FORMAT BINARY)"))
        {
            for (var i = 0; i < presentTeams.Count; i++)
            {
                var id = Guid.NewGuid();
                teamToGroupId[presentTeams[i]] = id;
                await groupWriter.StartRowAsync();
                await groupWriter.WriteAsync(id, NpgsqlDbType.Uuid);
                await groupWriter.WriteAsync(presentTeams[i], NpgsqlDbType.Varchar);
                await groupWriter.WriteNullAsync();                          // description
                await groupWriter.WriteNullAsync();                          // color
                await groupWriter.WriteAsync(i, NpgsqlDbType.Integer);       // display_order
                await groupWriter.WriteAsync(personResourceTypeId, NpgsqlDbType.Uuid);
                await groupWriter.WriteAsync(now, NpgsqlDbType.TimestampTz);
                await groupWriter.WriteAsync(now, NpgsqlDbType.TimestampTz);
                seeded.Add(new SeededPersonGroup(id, presentTeams[i]));
            }
            await groupWriter.CompleteAsync();
        }

        var memberCount = 0;
        using (var memberWriter = await conn.BeginBinaryImportAsync(
            "COPY public.resource_group_members (resource_group_id, resource_id, resource_type_id) " +
            "FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var team in presentTeams)
                foreach (var person in teamToPeople[team])
                {
                    await memberWriter.StartRowAsync();
                    await memberWriter.WriteAsync(teamToGroupId[team], NpgsqlDbType.Uuid);
                    await memberWriter.WriteAsync(person.ResourceId, NpgsqlDbType.Uuid);
                    await memberWriter.WriteAsync(personResourceTypeId, NpgsqlDbType.Uuid);
                    memberCount++;
                }
            await memberWriter.CompleteAsync();
        }

        return (seeded, memberCount);
    }

    private static string Slugify(string s)
    {
        var lower = s.ToLowerInvariant();
        var chars = lower.Select(c => char.IsLetterOrDigit(c) ? c : '.').ToArray();
        var slug = new string(chars).Trim('.');
        while (slug.Contains("..")) slug = slug.Replace("..", ".");
        // Add a short suffix to avoid email collisions when many seeded people share a name.
        return $"{slug}.{Random.Shared.Next(0, 99999):D5}";
    }
}
