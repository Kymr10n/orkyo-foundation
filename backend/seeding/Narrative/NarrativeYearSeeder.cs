using Bogus;
using Npgsql;
using NpgsqlTypes;
using Orkyo.Foundation.Seed.Factories;
using Orkyo.Foundation.Seed.Scales;

namespace Orkyo.Foundation.Seed.Narrative;

/// <summary>
/// Generates the coherent year of work: per-facility campaign + routine + recurring (PM/QA) jobs placed
/// in shift hours within the calendar, each with skill requirements and capability-matched, facility-
/// local, timeline-aware assignments (Exclusive machines/people aren't accidentally double-booked;
/// Fractional forklifts/cranes are shared; ConcurrentCapacity storage holds several jobs). Injects a
/// small, bounded set of intentional conflicts so conflict detection has something to surface. Replaces
/// the random WorkItemFactories for the demo.
/// </summary>
public static class NarrativeYearSeeder
{
    public sealed record Result(int Requests, int Requirements, int Assignments, int Conflicts,
        IReadOnlyList<Guid> RequestIds);

    private sealed record Job(
        Guid Id, string Name, Guid? ParentId, DateTime Start, DateTime End, string Status,
        int DurationHours, IReadOnlyList<Guid> RequiredCriteria, JobArchetype Archetype,
        Guid? SpaceId, List<(Guid ResId, decimal Pct)> Assignees);

    public static async Task<Result> SeedAsync(
        NpgsqlConnection conn,
        IReadOnlyList<FacilityCohort> cohorts,
        IReadOnlyDictionary<string, Guid> criteria,
        IReadOnlyDictionary<Guid, HashSet<Guid>> personSkills,
        YearCalendar cal,
        IScale scale,
        Faker faker)
    {
        var parents = new List<(Guid Id, string Name, int SortOrder)>();
        var jobs = new List<Job>();
        var conflicts = 0;

        // Recurring cadence is fixed; campaign+routine volume fills up to the scale target.
        var recurringPerFacility = cal.MonthStarts().Count() /*PM monthly*/ + 4 /*QA quarterly*/;
        var variableTotal = Math.Max(cohorts.Count * 10, scale.Requests - cohorts.Count * (recurringPerFacility + 1));
        var variablePerFacility = variableTotal / cohorts.Count;

        foreach (var cohort in cohorts)
        {
            var cohortStart = jobs.Count; // snapshot before this cohort adds jobs
            var ctx = new AssignContext(cohort, personSkills, faker);
            var campaignWin = cal.CampaignWindow(cohort.Facility.SiteCode);

            // Campaign summary parent.
            var parentId = Guid.NewGuid();
            parents.Add((parentId, $"{cohort.Facility.CampaignName} ({cohort.Facility.SiteCode})", parents.Count));

            var campaignArchetypes = cohort.Facility.Archetypes.Where(a => a.Cadence == JobCadence.Campaign).ToList();
            var routineArchetypes = cohort.Facility.Archetypes.Where(a => a.Cadence == JobCadence.Routine).ToList();
            var weightSum = campaignArchetypes.Concat(routineArchetypes).Sum(a => a.Weight);

            foreach (var arch in campaignArchetypes)
            {
                var n = Math.Max(1, variablePerFacility * arch.Weight / Math.Max(1, weightSum));
                for (var i = 0; i < n; i++)
                {
                    var day = cal.PickWorkingDay(campaignWin.Start, campaignWin.End, faker);
                    if (day is null) continue;
                    jobs.Add(BuildJob(cohort, arch, cal, criteria, ctx, day.Value, parentId, faker));
                }
            }
            foreach (var arch in routineArchetypes)
            {
                var n = Math.Max(1, variablePerFacility * arch.Weight / Math.Max(1, weightSum));
                for (var i = 0; i < n; i++)
                {
                    var day = cal.PickWorkingDay(cal.Start, cal.End, faker);
                    if (day is null) continue;
                    jobs.Add(BuildJob(cohort, arch, cal, criteria, ctx, day.Value, null, faker));
                }
            }

            // Recurring PM — one per month; QA — one per quarter.
            var pm = cohort.Facility.Archetypes.First(a => a.Cadence == JobCadence.MonthlyPm);
            var monthIdx = 0;
            foreach (var month in cal.MonthStarts())
            {
                var day = cal.PickWorkingDay(month, month.AddMonths(1), faker);
                if (day is not null)
                {
                    jobs.Add(BuildJob(cohort, pm, cal, criteria, ctx, day.Value, null, faker));
                    if (monthIdx % 3 == 0)
                    {
                        var qa = cohort.Facility.Archetypes.First(a => a.Cadence == JobCadence.QuarterlyQa);
                        var qday = cal.PickWorkingDay(month, month.AddMonths(1), faker);
                        if (qday is not null) jobs.Add(BuildJob(cohort, qa, cal, criteria, ctx, qday.Value, null, faker));
                    }
                }
                monthIdx++;
            }

            // Scope conflict injection to this cohort's jobs only — avoids cross-facility
            // swaps and keeps the budget proportional to this cohort's volume.
            var cohortJobs = jobs.GetRange(cohortStart, jobs.Count - cohortStart);
            var personIds = cohort.People.Select(p => p.ResourceId).ToHashSet();
            var toolIds = cohort.Tools.Select(t => t.Id).ToHashSet();
            var concurrentRooms = cohort.Facility.ConcurrentRoomCodes.ToHashSet();

            // Intentional capability conflicts: ~5 % of this cohort's skill-bearing jobs keep their
            // (correct) room but are staffed by a person who lacks a required skill — and only that
            // person, so nobody covers it. Person-skills are checked against the assigned people, so
            // this surfaces a capability blocker on the people dimension (see ConflictService).
            var capBudget = Math.Max(1, cohortJobs.Count / 20);
            var capPool = cohortJobs
                .Where(j => j.SpaceId is not null && j.RequiredCriteria.Count > 0)
                .OrderBy(_ => faker.Random.Int())
                .Take(capBudget)
                .ToList();

            foreach (var job in capPool)
            {
                // A cohort person missing at least one of this job's required criteria.
                var required = job.RequiredCriteria;
                var incapable = cohort.People
                    .Select(p => p.ResourceId)
                    .Where(pid => !(personSkills.TryGetValue(pid, out var sk) && required.All(sk.Contains)))
                    .OrderBy(_ => faker.Random.Int())
                    .FirstOrDefault();
                if (incapable == Guid.Empty) continue;

                // Keep only the room (and any non-person, non-tool placement); a tool could otherwise
                // cover a tool-applicable skill (e.g. CNC). Then add the single incapable person.
                var newAssignees = job.Assignees
                    .Where(a => !personIds.Contains(a.ResId) && !toolIds.Contains(a.ResId))
                    .Append((incapable, 100m))
                    .ToList();
                jobs[cohortStart + cohortJobs.IndexOf(job)] = job with { Assignees = newAssignees };
            }

            // Intentional scheduling conflicts: clone a few jobs that sit in a non-concurrent
            // (Exclusive) room with a lead onto the same room+lead+slot. The clone double-books both
            // the Exclusive room and the Exclusive lead → one space overlap and one person overlap,
            // covering "spaces and people" without depending on tools. Exclude the capability-conflict
            // jobs so the two conflict kinds stay distinct.
            bool InNonConcurrentRoom(Job j) =>
                j.SpaceId is { } sid
                && !concurrentRooms.Contains(j.Archetype.RoomCode)
                && j.Assignees.Any(a => a.ResId == sid);
            var clonePool = cohortJobs
                .Where(j => !capPool.Contains(j)
                    && InNonConcurrentRoom(j)
                    && j.Assignees.Any(a => personIds.Contains(a.ResId)))
                .ToList();
            var conflictBudget = Math.Max(1, cohortJobs.Count / 20); // ~5%
            for (var i = 0; i < conflictBudget && clonePool.Count > 0; i++)
            {
                var src = faker.PickRandom(clonePool);
                var clone = src with
                {
                    Id = Guid.NewGuid(),
                    Name = $"Rush order — {src.Name}",
                    ParentId = null,
                    Assignees = src.Assignees.ToList(),
                };
                jobs.Add(clone);
                conflicts++;
            }
        }

        await WriteRequestsAsync(conn, parents, jobs);
        var reqCount = await WriteRequirementsAsync(conn, jobs, criteria);
        var asgCount = await WriteAssignmentsAsync(conn, jobs);

        var allIds = parents.Select(p => p.Id)
            .Concat(jobs.Select(j => j.Id))
            .ToList();
        return new Result(allIds.Count, reqCount, asgCount, conflicts, allIds);
    }

    private static Job BuildJob(
        FacilityCohort cohort, JobArchetype arch, YearCalendar cal,
        IReadOnlyDictionary<string, Guid> criteria, AssignContext ctx,
        DateTime day, Guid? parentId, Faker faker)
    {
        var (start, end) = cal.MakeSlot(day, arch.MinHours, arch.MaxHours, faker);
        var status = cal.StatusFor(start, end, faker);
        var requiredCriteria = arch.RequiredSkills.Select(s => criteria[s]).ToList();
        var name = $"{arch.Verb} {arch.Noun} — {cohort.Facility.SiteCode}";

        var assignees = new List<(Guid, decimal)>();

        // Space (the room).
        Guid? spaceId = null;
        if (cohort.SpaceByRoomCode.TryGetValue(arch.RoomCode, out var space))
        {
            spaceId = space.Id;
            var concurrent = cohort.Facility.ConcurrentRoomCodes.Contains(arch.RoomCode);
            if (concurrent || ctx.IsFree(space.Id, start, end))
            {
                assignees.Add((space.Id, 100m));
                if (!concurrent) ctx.MarkBusy(space.Id, start, end);
            }
        }

        // Lead person — must hold all required skills; prefer a free one.
        var lead = ctx.PickCapablePerson(requiredCriteria, start, end);
        if (lead is { } leadId)
        {
            assignees.Add((leadId, 100m));
            ctx.MarkBusy(leadId, start, end);

            // Additional team members for multi-person jobs (assembly crews, packaging lines, etc.).
            // Helpers are assigned at 50 % so they can support concurrent jobs without being Overbooked.
            // They are NOT tracked in _busy — only the lead (Exclusive primary operator) is.
            if (arch.TeamSize > 1)
            {
                foreach (var helper in ctx.PickHelpers(leadId, requiredCriteria, arch.TeamSize - 1))
                    assignees.Add((helper, 50m));
            }
        }

        // Tool (Exclusive ⇒ free slot; Fractional ⇒ shared at 50%).
        if (arch.ToolRole is { } role)
        {
            var tool = ctx.PickTool(role, start, end);
            if (tool is { } t)
            {
                var pct = t.AllocationMode == "Fractional" ? 50m : 100m;
                assignees.Add((t.Id, pct));
                if (t.AllocationMode != "Fractional") ctx.MarkBusy(t.Id, start, end);
            }
        }

        var hours = (int)Math.Round((end - start).TotalHours);
        return new Job(Guid.NewGuid(), name, parentId, start, end, status, hours, requiredCriteria, arch, spaceId, assignees);
    }

    // ── Assignment context: per-facility timelines, capability lookup, tool pools ──
    private sealed class AssignContext
    {
        private readonly Dictionary<Guid, List<(DateTime S, DateTime E)>> _busy = new();
        private readonly FacilityCohort _cohort;
        private readonly IReadOnlyDictionary<Guid, HashSet<Guid>> _personSkills;
        private readonly Faker _faker;

        public AssignContext(FacilityCohort cohort, IReadOnlyDictionary<Guid, HashSet<Guid>> personSkills, Faker faker)
        {
            _cohort = cohort; _personSkills = personSkills; _faker = faker;
        }

        public bool IsFree(Guid id, DateTime s, DateTime e) =>
            !_busy.TryGetValue(id, out var list) || !list.Any(b => s < b.E && b.S < e);

        public void MarkBusy(Guid id, DateTime s, DateTime e)
        {
            if (!_busy.TryGetValue(id, out var list)) _busy[id] = list = [];
            list.Add((s, e));
        }

        public Guid? PickCapablePerson(IReadOnlyList<Guid> required, DateTime s, DateTime e)
        {
            var candidates = _cohort.People
                .Where(p => _personSkills.TryGetValue(p.ResourceId, out var sk) && required.All(sk.Contains))
                .Select(p => p.ResourceId)
                .ToList();
            if (candidates.Count == 0) return null;
            var shuffled = candidates.OrderBy(_ => _faker.Random.Int());
            return shuffled.FirstOrDefault(id => IsFree(id, s, e)) is var free && free != Guid.Empty
                ? free
                : candidates[0]; // all busy ⇒ allow (rare); keeps the job staffed
        }

        // Helpers are supporting crew: not tracked for Exclusive scheduling, float freely between jobs.
        public IEnumerable<Guid> PickHelpers(Guid leadId, IReadOnlyList<Guid> required, int count) =>
            _cohort.People
                .Where(p => p.ResourceId != leadId
                    && _personSkills.TryGetValue(p.ResourceId, out var sk)
                    && required.Any(c => sk.Contains(c)))
                .OrderBy(_ => _faker.Random.Int())
                .Take(count)
                .Select(p => p.ResourceId);

        public ToolFactory.SeededTool? PickTool(string role, DateTime s, DateTime e)
        {
            // A role's tools are homogeneous (machines are Exclusive; forklifts/cranes Fractional).
            var pool = _cohort.Tools.Where(t => t.Role == role).OrderBy(_ => _faker.Random.Int()).ToList();
            if (pool.Count == 0) return null;
            if (pool[0].AllocationMode == "Fractional") return pool[0];   // shareable — overlap is fine
            return pool.FirstOrDefault(t => IsFree(t.Id, s, e));          // free machine, else skip (no accidental double-book)
        }
    }

    // ── Bulk writers ──────────────────────────────────────────────────────────
    private const string RequestCopy =
        "COPY public.requests (id, name, description, start_ts, end_ts, minimal_duration_value, " +
        "minimal_duration_unit, status, created_at, updated_at, scheduling_settings_apply, planning_mode, " +
        "sort_order, parent_request_id) FROM STDIN (FORMAT BINARY)";

    private static async Task WriteRequestsAsync(
        NpgsqlConnection conn, IReadOnlyList<(Guid Id, string Name, int SortOrder)> parents, IReadOnlyList<Job> jobs)
    {
        var now = DateTime.UtcNow;
        using (var w = await conn.BeginBinaryImportAsync(RequestCopy))
        {
            foreach (var (id, name, sort) in parents)
            {
                await w.StartRowAsync();
                await w.WriteAsync(id, NpgsqlDbType.Uuid);
                await w.WriteAsync(name, NpgsqlDbType.Varchar);
                await w.WriteNullAsync();
                await w.WriteNullAsync();
                await w.WriteNullAsync();
                await w.WriteAsync(60, NpgsqlDbType.Integer);
                await w.WriteAsync("minutes", NpgsqlDbType.Varchar);
                await w.WriteAsync("planned", NpgsqlDbType.Varchar);
                await w.WriteAsync(now, NpgsqlDbType.TimestampTz);
                await w.WriteAsync(now, NpgsqlDbType.TimestampTz);
                await w.WriteAsync(false, NpgsqlDbType.Boolean);
                await w.WriteAsync("summary", NpgsqlDbType.Varchar);
                await w.WriteAsync(sort, NpgsqlDbType.Integer);
                await w.WriteNullAsync();
            }
            await w.CompleteAsync();
        }
        using (var w = await conn.BeginBinaryImportAsync(RequestCopy))
        {
            var sort = 0;
            foreach (var j in jobs)
            {
                await w.StartRowAsync();
                await w.WriteAsync(j.Id, NpgsqlDbType.Uuid);
                await w.WriteAsync(j.Name, NpgsqlDbType.Varchar);
                await w.WriteNullAsync();
                await w.WriteAsync(j.Start, NpgsqlDbType.TimestampTz);
                await w.WriteAsync(j.End, NpgsqlDbType.TimestampTz);
                await w.WriteAsync(Math.Max(1, j.DurationHours), NpgsqlDbType.Integer);
                await w.WriteAsync("hours", NpgsqlDbType.Varchar);
                await w.WriteAsync(j.Status, NpgsqlDbType.Varchar);
                await w.WriteAsync(now, NpgsqlDbType.TimestampTz);
                await w.WriteAsync(now, NpgsqlDbType.TimestampTz);
                await w.WriteAsync(true, NpgsqlDbType.Boolean);
                await w.WriteAsync("leaf", NpgsqlDbType.Varchar);
                await w.WriteAsync(sort++, NpgsqlDbType.Integer);
                if (j.ParentId is { } pid) await w.WriteAsync(pid, NpgsqlDbType.Uuid); else await w.WriteNullAsync();
            }
            await w.CompleteAsync();
        }
    }

    private static async Task<int> WriteRequirementsAsync(
        NpgsqlConnection conn, IReadOnlyList<Job> jobs, IReadOnlyDictionary<string, Guid> criteria)
    {
        var byId = criteria.ToDictionary(kv => kv.Value, kv => SkillCatalog.ByKey(kv.Key));
        var count = 0;
        using var w = await conn.BeginBinaryImportAsync(
            "COPY public.request_requirements (request_id, criterion_id, value, operator, allowed_values) FROM STDIN (FORMAT BINARY)");
        foreach (var j in jobs)
            foreach (var cid in j.RequiredCriteria)
            {
                var skill = byId[cid];
                await w.StartRowAsync();
                await w.WriteAsync(j.Id, NpgsqlDbType.Uuid);
                await w.WriteAsync(cid, NpgsqlDbType.Uuid);
                if (skill.DataType == "Enum")
                {
                    await w.WriteAsync($"\"{skill.EnumValues![0]}\"", NpgsqlDbType.Jsonb);
                    await w.WriteNullAsync(); // operator
                    await w.WriteAsync(System.Text.Json.JsonSerializer.Serialize(skill.EnumValues), NpgsqlDbType.Jsonb);
                }
                else // Boolean (presence/kind match)
                {
                    await w.WriteAsync("true", NpgsqlDbType.Jsonb);
                    await w.WriteNullAsync(); // operator
                    await w.WriteNullAsync(); // allowed_values
                }
                count++;
            }
        await w.CompleteAsync();
        return count;
    }

    private static async Task<int> WriteAssignmentsAsync(NpgsqlConnection conn, IReadOnlyList<Job> jobs)
    {
        var now = DateTime.UtcNow;
        var count = 0;
        using var w = await conn.BeginBinaryImportAsync(
            "COPY public.resource_assignments (id, request_id, resource_id, start_utc, end_utc, " +
            "allocation_percent, assignment_status, created_at, updated_at) FROM STDIN (FORMAT BINARY)");
        foreach (var j in jobs)
            foreach (var (resId, pct) in j.Assignees)
            {
                await w.StartRowAsync();
                await w.WriteAsync(Guid.NewGuid(), NpgsqlDbType.Uuid);
                await w.WriteAsync(j.Id, NpgsqlDbType.Uuid);
                await w.WriteAsync(resId, NpgsqlDbType.Uuid);
                await w.WriteAsync(j.Start, NpgsqlDbType.TimestampTz);
                await w.WriteAsync(j.End, NpgsqlDbType.TimestampTz);
                await w.WriteAsync(pct, NpgsqlDbType.Numeric);
                await w.WriteAsync("Planned", NpgsqlDbType.Varchar);
                await w.WriteAsync(now, NpgsqlDbType.TimestampTz);
                await w.WriteAsync(now, NpgsqlDbType.TimestampTz);
                count++;
            }
        await w.CompleteAsync();
        return count;
    }
}
