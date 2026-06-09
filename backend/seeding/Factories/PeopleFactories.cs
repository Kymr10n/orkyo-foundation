using Bogus;
using Npgsql;
using NpgsqlTypes;
using Orkyo.Foundation.Seed.Narrative;
using Orkyo.Foundation.Seed.Profiles;
using Orkyo.Foundation.Seed.Scales;

namespace Orkyo.Foundation.Seed.Factories;

/// <summary>
/// Seeds the people side: job titles, departments, person resources, and
/// person profiles.
///
/// MVP scope:
///  - Departments are seeded flat (no hierarchy). Adding parent_department_id
///    links is a follow-up.
///  - Resources are created with allocation_mode='Fractional' and
///    base_availability_percent=100, matching the production person default.
/// </summary>
public static class PeopleFactories
{
    public sealed record SeededJobTitle(Guid Id, string Name);
    public sealed record SeededDepartment(Guid Id, string Name);
    public sealed record SeededPerson(Guid ResourceId, string Name);
    public sealed record SeededPersonGroup(Guid Id, string Name);

    public static async Task<Guid> ResolvePersonResourceTypeIdAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT id FROM public.resource_types WHERE key = 'person' LIMIT 1", conn, tx);
        var id = (Guid?)await cmd.ExecuteScalarAsync();
        return id ?? throw new InvalidOperationException(
            "resource_types row with key='person' not found. Has the tenant DB been migrated?");
    }

    public static async Task<IReadOnlyList<SeededJobTitle>> SeedJobTitlesAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        IProfile profile, IScale scale, Faker faker)
    {
        var pool = profile.JobTitlePool;
        var titles = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; titles.Count < scale.JobTitles && i < scale.JobTitles * 5; i++)
        {
            // Generate a unique title — append a numeric suffix if pool exhausted.
            var baseName = pool[i % pool.Count];
            var candidate = i < pool.Count ? baseName : $"{baseName} {i / pool.Count + 1}";
            if (seen.Add(candidate)) titles.Add(candidate);
        }

        var seeded = new List<SeededJobTitle>(titles.Count);
        var now = DateTime.UtcNow;
        using var writer = await conn.BeginBinaryImportAsync(
            "COPY public.job_titles (id, name, is_active, created_at, updated_at) FROM STDIN (FORMAT BINARY)");
        _ = tx;

        foreach (var name in titles)
        {
            var id = Guid.NewGuid();
            await writer.StartRowAsync();
            await writer.WriteAsync(id, NpgsqlDbType.Uuid);
            await writer.WriteAsync(name, NpgsqlDbType.Varchar);
            await writer.WriteAsync(true, NpgsqlDbType.Boolean);
            await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
            await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
            seeded.Add(new SeededJobTitle(id, name));
        }
        await writer.CompleteAsync();
        _ = faker; // pool consumed deterministically; faker reserved for future shuffling
        return seeded;
    }

    // Subdivision suffixes for child departments — generic enough to work across
    // all profiles; combined with parent name (e.g. "Operations North").
    private static readonly string[] DeptSubdivisions =
        ["North", "South", "East", "West", "Central", "Alpha", "Beta", "Gamma", "Delta", "Epsilon"];

    public static async Task<IReadOnlyList<SeededDepartment>> SeedDepartmentsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        IProfile profile, IScale scale, Faker faker)
    {
        // ── Root departments ──────────────────────────────────────────────────
        // ux_departments_root_name enforces uniqueness at root level.
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

        var roots = rootNames.Select(n => new SeededDepartment(Guid.NewGuid(), n)).ToList();

        // ── Child departments ─────────────────────────────────────────────────
        // ux_departments_sibling_name enforces (parent_id, name) uniqueness, so
        // suffixes only need to be unique within a single parent.
        var childCounters = new int[roots.Count];
        var children = new List<(SeededDepartment Dept, Guid ParentId)>(childCount);
        for (var i = 0; i < childCount; i++)
        {
            var parentIdx = i % roots.Count;
            var suffix = DeptSubdivisions[childCounters[parentIdx]++ % DeptSubdivisions.Length];
            var childName = $"{roots[parentIdx].Name} {suffix}";
            children.Add((new SeededDepartment(Guid.NewGuid(), childName), roots[parentIdx].Id));
        }

        var now = DateTime.UtcNow;
        _ = tx;
        _ = faker;
        const string copySql =
            "COPY public.departments (id, parent_department_id, name, is_active, created_at, updated_at) " +
            "FROM STDIN (FORMAT BINARY)";

        // Write roots first — FK is ON DELETE RESTRICT, so parents must exist.
        using (var writer = await conn.BeginBinaryImportAsync(copySql))
        {
            foreach (var root in roots)
            {
                await writer.StartRowAsync();
                await writer.WriteAsync(root.Id, NpgsqlDbType.Uuid);
                await writer.WriteNullAsync();                              // parent_department_id
                await writer.WriteAsync(root.Name, NpgsqlDbType.Varchar);
                await writer.WriteAsync(true, NpgsqlDbType.Boolean);
                await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
                await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
            }
            await writer.CompleteAsync();
        }

        using (var writer = await conn.BeginBinaryImportAsync(copySql))
        {
            foreach (var (dept, parentId) in children)
            {
                await writer.StartRowAsync();
                await writer.WriteAsync(dept.Id, NpgsqlDbType.Uuid);
                await writer.WriteAsync(parentId, NpgsqlDbType.Uuid);      // parent_department_id
                await writer.WriteAsync(dept.Name, NpgsqlDbType.Varchar);
                await writer.WriteAsync(true, NpgsqlDbType.Boolean);
                await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
                await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
            }
            await writer.CompleteAsync();
        }

        var all = new List<SeededDepartment>(scale.Departments);
        all.AddRange(roots);
        all.AddRange(children.Select(c => c.Dept));
        return all;
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

        // Insert resources (the bulk of the work).
        _ = tx;
        using (var writer = await conn.BeginBinaryImportAsync(
            "COPY public.resources (id, resource_type_id, name, allocation_mode, base_availability_percent, is_active, created_at, updated_at) " +
            "FROM STDIN (FORMAT BINARY)"))
        {
            for (var i = 0; i < scale.People; i++)
            {
                var id = Guid.NewGuid();
                var fullName = faker.Name.FullName();
                await writer.StartRowAsync();
                await writer.WriteAsync(id, NpgsqlDbType.Uuid);
                await writer.WriteAsync(personResourceTypeId, NpgsqlDbType.Uuid);
                await writer.WriteAsync(fullName, NpgsqlDbType.Varchar);
                await writer.WriteAsync("Fractional", NpgsqlDbType.Varchar);
                await writer.WriteAsync(100, NpgsqlDbType.Integer);
                await writer.WriteAsync(true, NpgsqlDbType.Boolean);
                await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
                await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
                people.Add(new SeededPerson(id, fullName));
            }
            await writer.CompleteAsync();
        }

        // Insert person_profiles linking job_title + department.
        // Skipped if there are no job titles or departments (won't crash; profile rows are optional).
        if (jobTitles.Count > 0 || departments.Count > 0)
        {
            using var writer = await conn.BeginBinaryImportAsync(
                "COPY public.person_profiles (resource_id, email, job_title_id, department_id, created_at, updated_at) " +
                "FROM STDIN (FORMAT BINARY)");

            foreach (var person in people)
            {
                var email = $"{Slugify(person.Name)}@orkyo.example";
                var jobTitleId = jobTitles.Count == 0 ? (Guid?)null : jobTitles[faker.Random.Int(0, jobTitles.Count - 1)].Id;
                var deptId = departments.Count == 0 ? (Guid?)null : departments[faker.Random.Int(0, departments.Count - 1)].Id;

                await writer.StartRowAsync();
                await writer.WriteAsync(person.ResourceId, NpgsqlDbType.Uuid);
                await writer.WriteAsync(email, NpgsqlDbType.Citext);
                if (jobTitleId is null) await writer.WriteNullAsync(); else await writer.WriteAsync(jobTitleId.Value, NpgsqlDbType.Uuid);
                if (deptId is null) await writer.WriteNullAsync(); else await writer.WriteAsync(deptId.Value, NpgsqlDbType.Uuid);
                await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
                await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
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
