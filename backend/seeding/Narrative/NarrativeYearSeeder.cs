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
/// Fractional forklifts/cranes and shared storage rooms hold several jobs at partial load). Injects a
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
        Guid? SpaceId, List<(Guid ResId, decimal? Pct)> Assignees);

    // Shared storage rooms (Fractional) hold several jobs concurrently at this per-job load,
    // tracked so they never exceed 100 % (≈4 simultaneous jobs before full).
    private const decimal StoragePct = 25m;

    // Backlog of unscheduled tasks the demo user schedules themselves. Scaled, but bounded: a
    // backlog nobody could work through is not a demo of anything.
    private static int BacklogCount(IScale scale) => Math.Clamp(scale.Requests / 250, 12, 24);

    public static async Task<Result> SeedAsync(
        NpgsqlConnection conn,
        IReadOnlyList<FacilityCohort> cohorts,
        IReadOnlyDictionary<string, Guid> criteria,
        IReadOnlyDictionary<Guid, HashSet<Guid>> personSkills,
        YearCalendar cal,
        IScale scale,
        Faker faker,
        IReadOnlyList<(Guid PersonId, DateTime Start, DateTime End)> vacations)
    {
        var parents = new List<(Guid Id, string Name, int SortOrder)>();
        var jobs = new List<Job>();
        var conflicts = 0;
        // Kept per cohort so the showcase pass below can book against the same capacity ledger the
        // cohort built, rather than double-booking by accident where it means to book cleanly.
        var contexts = new List<(FacilityCohort Cohort, AssignContext Ctx)>();

        // Recurring cadence is fixed; campaign+routine volume fills up to the scale target.
        var recurringPerFacility = cal.MonthStarts().Count() /*PM monthly*/ + 4 /*QA quarterly*/;
        var variableTotal = Math.Max(cohorts.Count * 10, scale.Requests - cohorts.Count * (recurringPerFacility + 1));
        var variablePerFacility = variableTotal / cohorts.Count;

        foreach (var cohort in cohorts)
        {
            var cohortStart = jobs.Count; // snapshot before this cohort adds jobs
            var ctx = new AssignContext(cohort, personSkills, faker);
            contexts.Add((cohort, ctx));
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
            var machineIds = cohort.Machines.Select(m => m.Id).ToHashSet();
            var concurrentRooms = cohort.Facility.ConcurrentRoomCodes.ToHashSet();

            // Intentional capability conflicts: ~5 % of this cohort's skill-bearing jobs keep their
            // (correct) room but are staffed by a person who lacks a required skill — and only that
            // person, so nobody covers it. Person-skills are checked against the assigned people, so
            // this surfaces a capability blocker on the people dimension (see ConflictService).
            var capBudget = Math.Max(1, cohortJobs.Count / 40);
            var capPool = cohortJobs
                .Where(j => j.SpaceId is not null && j.RequiredCriteria.Count > 0)
                .OrderBy(_ => faker.Random.Int())
                .Take(capBudget)
                .ToList();

            foreach (var job in capPool)
            {
                // A cohort person missing at least one of this job's required criteria AND free at
                // this slot — so the replacement doesn't accidentally add an overbooking conflict
                // on top of the capability conflict.
                var required = job.RequiredCriteria;
                var incapable = cohort.People
                    .Select(p => p.ResourceId)
                    .Where(pid => !(personSkills.TryGetValue(pid, out var sk) && required.All(sk.Contains))
                        && ctx.IsFree(pid, job.Start, job.End))
                    .OrderBy(_ => faker.Random.Int())
                    .FirstOrDefault();
                if (incapable == Guid.Empty) continue;

                // Keep only the room (and any non-person, non-tool placement); a tool could otherwise
                // cover a tool-applicable skill (e.g. CNC). Then add the single incapable person.
                var newAssignees = job.Assignees
                    .Where(a => !personIds.Contains(a.ResId)
                                && !toolIds.Contains(a.ResId)
                                && !machineIds.Contains(a.ResId))
                    .Append((incapable, 100m))
                    .ToList();
                jobs[cohortStart + cohortJobs.IndexOf(job)] = job with { Assignees = newAssignees };
                ctx.MarkBusy(incapable, job.Start, job.End); // prevent a second capability-conflict job from also picking this person
                conflicts++; // a real capability blocker the validator will surface
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
            var conflictBudget = Math.Max(1, cohortJobs.Count / 40); // ~2.5% clone injection → ~5% requests flagged (source + clone each)
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

        // ── Showcase conflicts ────────────────────────────────────────────────────
        // The two injections above cover capability and overbooking. Three kinds had no example
        // anywhere in the demo, because the seeder is built to avoid them: it books only working
        // days and only free capacity, and it pins each cohort to its own site. Each is arranged
        // once here, deliberately, so the conflict list shows what the product can actually detect.
        conflicts += InjectAbsenceOverlaps(jobs, contexts, cal, criteria, vacations, faker);
        conflicts += InjectShutdownOverlap(jobs, contexts, cal, criteria, faker);
        conflicts += InjectCrossSiteAssignments(conn, jobs, contexts);

        await WriteRequestsAsync(conn, parents, jobs);
        var reqCount = await WriteRequirementsAsync(
            conn, jobs.Select(j => (j.Id, (IReadOnlyList<Guid>)j.RequiredCriteria)), criteria);
        var asgCount = await WriteAssignmentsAsync(conn, jobs);

        // A small curated backlog of unscheduled tasks so the demo's utilization backlog isn't empty —
        // users drag these onto the grid to schedule them.
        var backlog = await WriteBacklogAsync(conn, cohorts, criteria, faker, BacklogCount(scale));
        var backlogIds = backlog.Select(b => b.Id).ToList();
        reqCount += await WriteRequirementsAsync(
            conn, backlog.Select(b => (b.Id, b.RequiredCriteria)), criteria);

        var allIds = parents.Select(p => p.Id)
            .Concat(jobs.Select(j => j.Id))
            .Concat(backlogIds)
            .ToList();

        // What each request needs, or nothing is ever "scheduled" (migrations 1720/1730). Every
        // request targets a space — that is what the seeder books and what 1720 backfilled for all
        // pre-existing requests. A job additionally targets a tool only when one was actually
        // assigned: the archetype's ToolRole is a wish (PickTool may find nothing, and the
        // capability-conflict injection above strips tool assignees again), and targeting a type
        // the request never received would leave it permanently unscheduled. Read from the final
        // Assignees, which is exactly what WriteAssignmentsAsync wrote.
        var allToolIds = cohorts.SelectMany(c => c.Tools).Select(t => t.Id).ToHashSet();
        // Machines follow the same rule, but each carries its own type key, so the target is the
        // key of the machine that was actually booked rather than one blanket value.
        var machineTypeById = cohorts.SelectMany(c => c.Machines).ToDictionary(m => m.Id, m => m.TypeKey);
        var targets = allIds.Select(id => (id, "room"))
            // A backlog item names the type that can satisfy it, which is what makes
            // auto-scheduling a real demo rather than a room-placement one.
            .Concat(backlog.Where(b => b.TargetTypeKey is not null).Select(b => (b.Id, b.TargetTypeKey!)))
            .Concat(jobs.Where(j => j.Assignees.Any(a => allToolIds.Contains(a.ResId)))
                        .Select(j => (j.Id, "tool")))
            .Concat(jobs.SelectMany(j => j.Assignees
                        .Where(a => machineTypeById.ContainsKey(a.ResId))
                        .Select(a => (j.Id, machineTypeById[a.ResId]))))
            .Distinct()
            .ToList();
        await RequestTargetFactory.WriteAsync(conn, targets);

        return new Result(allIds.Count, reqCount, asgCount, conflicts, allIds);
    }

    /// <summary>
    /// Books a few people over their own holiday.
    /// </summary>
    /// <remarks>
    /// The seeder tracks capacity, not time off, so an assignment never lands on an absence by
    /// accident and the conflict has no example. One per cohort, on the first vacation that starts
    /// after the reference date, staffed by the person whose holiday it is.
    /// </remarks>
    private static int InjectAbsenceOverlaps(
        List<Job> jobs,
        IReadOnlyList<(FacilityCohort Cohort, AssignContext Ctx)> contexts,
        YearCalendar cal,
        IReadOnlyDictionary<string, Guid> criteria,
        IReadOnlyList<(Guid PersonId, DateTime Start, DateTime End)> vacations,
        Faker faker)
    {
        var vacationByPerson = vacations
            .Where(v => v.Start > cal.ReferenceDate)
            .GroupBy(v => v.PersonId)
            .ToDictionary(g => g.Key, g => g.First());

        var injected = 0;
        foreach (var (cohort, ctx) in contexts)
        {
            var arch = cohort.Facility.Archetypes.FirstOrDefault(a => a.Cadence == JobCadence.Routine);
            if (arch is null) continue;

            // The first person of the cohort with a vacation still ahead of us. Cohort order is
            // stable, so which person it is does not move between runs of the same seed.
            var pick = cohort.People
                .Select(p => p.ResourceId)
                .Where(vacationByPerson.ContainsKey)
                .Select(pid => (PersonId: pid, Vacation: vacationByPerson[pid]))
                .FirstOrDefault(x => FirstWeekdayIn(x.Vacation.Start, x.Vacation.End) is not null);
            if (pick.PersonId == Guid.Empty) continue;

            var day = FirstWeekdayIn(pick.Vacation.Start, pick.Vacation.End)!.Value;
            var (start, end) = cal.MakeSlot(day, arch.MinHours, arch.MaxHours, faker);

            var assignees = new List<(Guid, decimal?)>();
            Guid? spaceId = null;
            if (cohort.SpaceByRoomCode.TryGetValue(arch.RoomCode, out var room) && ctx.IsFree(room.Id, start, end))
            {
                spaceId = room.Id;
                assignees.Add((room.Id, null));
                ctx.MarkBusy(room.Id, start, end);
            }
            assignees.Add((pick.PersonId, 100m));
            ctx.MarkBusy(pick.PersonId, start, end);

            jobs.Add(new Job(
                Guid.NewGuid(), $"{arch.Verb} {arch.Noun} — {cohort.Facility.SiteCode}", null,
                start, end, cal.StatusFor(start, end, faker),
                (int)(end - start).TotalHours,
                arch.RequiredSkills.Select(sk => criteria[sk]).ToList(), arch,
                spaceId, assignees));
            injected++;
        }
        return injected;
    }

    /// <summary>The first Monday-to-Friday inside a window, or null when it holds none.</summary>
    private static DateTime? FirstWeekdayIn(DateTime start, DateTime end)
    {
        for (var day = start.Date; day < end.Date; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) return day;
        }
        return null;
    }

    /// <summary>
    /// Books one job inside a site shutdown.
    /// </summary>
    /// <remarks>
    /// <c>PickWorkingDay</c> refuses shutdown days by design, so the calendar is asked for the slot
    /// directly. Both the room and the person sit under the site-wide closure the availability
    /// factory wrote, so the overlap is reported against each of them.
    /// </remarks>
    private static int InjectShutdownOverlap(
        List<Job> jobs,
        IReadOnlyList<(FacilityCohort Cohort, AssignContext Ctx)> contexts,
        YearCalendar cal,
        IReadOnlyDictionary<string, Guid> criteria,
        Faker faker)
    {
        var shutdown = cal.Shutdowns.FirstOrDefault(sd => sd.Start > cal.ReferenceDate);
        if (shutdown == default) return 0;

        var day = FirstWeekdayIn(shutdown.Start, shutdown.End);
        if (day is null) return 0;

        var (cohort, ctx) = contexts[0];
        var arch = cohort.Facility.Archetypes.FirstOrDefault(a => a.Cadence == JobCadence.Routine);
        if (arch is null) return 0;

        var (start, end) = cal.MakeSlot(day.Value, arch.MinHours, arch.MaxHours, faker);
        var required = arch.RequiredSkills.Select(sk => criteria[sk]).ToList();

        var assignees = new List<(Guid, decimal?)>();
        Guid? spaceId = null;
        if (cohort.SpaceByRoomCode.TryGetValue(arch.RoomCode, out var room) && ctx.IsFree(room.Id, start, end))
        {
            spaceId = room.Id;
            assignees.Add((room.Id, null));
            ctx.MarkBusy(room.Id, start, end);
        }
        if (ctx.PickCapablePerson(required, start, end) is { } lead)
        {
            assignees.Add((lead, 100m));
            ctx.MarkBusy(lead, start, end);
        }
        if (assignees.Count == 0) return 0;

        jobs.Add(new Job(
            Guid.NewGuid(), $"{arch.Verb} {arch.Noun} — {cohort.Facility.SiteCode}", null,
            start, end, cal.StatusFor(start, end, faker),
            (int)(end - start).TotalHours, required, arch, spaceId, assignees));
        return 1;
    }

    /// <summary>
    /// Lends a person to the other site's work, where they are not allowed to travel.
    /// </summary>
    /// <remarks>
    /// A request adopts the site of the space it books, and a person pinned to another site with
    /// <c>cross_site_allowed = false</c> is a blocker on it. The flag is stamped after this
    /// transaction commits, by the same <c>hashtext</c> rule replicated here — so the person picked
    /// is one the later pass will actually pin.
    /// </remarks>
    private static int InjectCrossSiteAssignments(
        NpgsqlConnection conn,
        List<Job> jobs,
        IReadOnlyList<(FacilityCohort Cohort, AssignContext Ctx)> contexts)
    {
        if (contexts.Count < 2) return 0;

        var injected = 0;
        for (var i = 0; i < contexts.Count && injected < 2; i++)
        {
            var (host, ctx) = contexts[i];
            var visitorCohort = contexts[(i + 1) % contexts.Count].Cohort;

            var candidate = PinnedToTheirSite(conn, visitorCohort.People.Select(p => p.ResourceId).ToList());
            if (candidate is null) continue;

            // A host job with a room, so the request adopts the host site, and a slot the visitor
            // is free in — the point is the site mismatch, not an overbooking on top of it.
            var hostJob = jobs.FirstOrDefault(j =>
                j.SpaceId is not null
                && j.Start > DateTime.UtcNow
                && host.SpaceByRoomCode.Values.Any(sp => sp.Id == j.SpaceId)
                && ctx.IsFree(candidate.Value, j.Start, j.End, 50m));
            if (hostJob is null) continue;

            var index = jobs.IndexOf(hostJob);
            jobs[index] = hostJob with { Assignees = hostJob.Assignees.Append((candidate.Value, (decimal?)50m)).ToList() };
            ctx.MarkBusy(candidate.Value, hostJob.Start, hostJob.End, 50m);
            injected++;
        }
        return injected;
    }

    /// <summary>
    /// The first of <paramref name="personIds"/> that <c>SiteModelFactory</c> will forbid from
    /// travelling, using its own rule so the two passes agree.
    /// </summary>
    private static Guid? PinnedToTheirSite(NpgsqlConnection conn, IReadOnlyList<Guid> personIds)
    {
        if (personIds.Count == 0) return null;
        using var cmd = new NpgsqlCommand(
            "SELECT id FROM resources WHERE id = ANY(@ids) AND abs(hashtext(id::text)) % 4 = 0 " +
            "ORDER BY id LIMIT 1", conn);
        cmd.Parameters.AddWithValue("ids", personIds.ToArray());
        return cmd.ExecuteScalar() as Guid?;
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

        var assignees = new List<(Guid, decimal?)>();

        // Space (the room). Shared storage rooms (ConcurrentRoomCodes) are Fractional — booked at a
        // small load-tracked %; every other room is Exclusive — booked with a null percent, one job
        // per slot. Either way only book when there is capacity, so rooms never overbook accidentally.
        Guid? spaceId = null;
        if (cohort.SpaceByRoomCode.TryGetValue(arch.RoomCode, out var space))
        {
            spaceId = space.Id;
            var concurrent = cohort.Facility.ConcurrentRoomCodes.Contains(arch.RoomCode);
            var requestedPct = concurrent ? StoragePct : 100m;
            if (ctx.IsFree(space.Id, start, end, requestedPct))
            {
                assignees.Add((space.Id, concurrent ? StoragePct : (decimal?)null));
                ctx.MarkBusy(space.Id, start, end, requestedPct);
            }
        }

        // Lead person — must hold all required skills; prefer a free one.
        var lead = ctx.PickCapablePerson(requiredCriteria, start, end);
        if (lead is { } leadId)
        {
            assignees.Add((leadId, 100m));
            ctx.MarkBusy(leadId, start, end, 100m);

            // Additional team members for multi-person jobs (assembly crews, packaging lines, etc.).
            // Helpers are tracked at 50 % — PickHelpers checks capacity and marks them busy.
            if (arch.TeamSize > 1)
            {
                foreach (var helper in ctx.PickHelpers(leadId, requiredCriteria, arch.TeamSize - 1, start, end))
                    assignees.Add((helper, 50m));
            }
        }
        else
        {
            // No capable free person — drop requirements so ConflictService doesn't fire
            // a capability conflict for every unmet skill. The job appears as "needs
            // assignment" rather than conflicted.
            requiredCriteria = [];
        }

        // Tool (Exclusive ⇒ free slot; Fractional ⇒ shared at 50 %, both tracked).
        if (arch.ToolRole is { } role)
        {
            var tool = ctx.PickTool(role, start, end);
            if (tool is { } t)
            {
                // Fractional tools are shared at 50 %; Exclusive tools (machines) book the whole slot
                // and must carry a null percent (the validator rejects a percent on Exclusive resources).
                var fractional = t.AllocationMode == "Fractional";
                assignees.Add((t.Id, fractional ? 50m : (decimal?)null));
                ctx.MarkBusy(t.Id, start, end, fractional ? 50m : 100m);
            }
        }

        // Machine. Always Exclusive — a mill runs one job at a time — so a null percent, the same
        // shape the Exclusive-tool branch above writes.
        if (arch.MachineRole is { } machineRole)
        {
            if (ctx.PickMachine(machineRole, start, end) is { } machine)
            {
                assignees.Add((machine.Id, (decimal?)null));
                ctx.MarkBusy(machine.Id, start, end, 100m);
            }
        }

        var hours = (int)Math.Round((end - start).TotalHours);
        return new Job(Guid.NewGuid(), name, parentId, start, end, status, hours, requiredCriteria, arch, spaceId, assignees);
    }

    // ── Assignment context: per-facility timelines, capability lookup, tool pools ──
    private sealed class AssignContext
    {
        // Tracks cumulative allocation % per resource across overlapping windows.
        // A person/tool is available for a new assignment only when currentLoad + requestedPct ≤ 100.
        // This prevents accidental overbooking for leads (100%), helpers (50%), and fractional tools (50%).
        private readonly Dictionary<Guid, List<(DateTime S, DateTime E, decimal Pct)>> _alloc = new();
        private readonly FacilityCohort _cohort;
        private readonly IReadOnlyDictionary<Guid, HashSet<Guid>> _personSkills;
        private readonly Faker _faker;

        public AssignContext(FacilityCohort cohort, IReadOnlyDictionary<Guid, HashSet<Guid>> personSkills, Faker faker)
        {
            _cohort = cohort; _personSkills = personSkills; _faker = faker;
        }

        public bool IsFree(Guid id, DateTime s, DateTime e, decimal requestedPct = 100m)
        {
            if (!_alloc.TryGetValue(id, out var list)) return true;
            var load = list.Where(b => s < b.E && b.S < e).Sum(b => b.Pct);
            return load + requestedPct <= 100m;
        }

        public void MarkBusy(Guid id, DateTime s, DateTime e, decimal pct = 100m)
        {
            if (!_alloc.TryGetValue(id, out var list)) _alloc[id] = list = [];
            list.Add((s, e, pct));
        }

        public Guid? PickCapablePerson(IReadOnlyList<Guid> required, DateTime s, DateTime e)
        {
            var candidates = _cohort.People
                .Where(p => _personSkills.TryGetValue(p.ResourceId, out var sk) && required.All(sk.Contains))
                .Select(p => p.ResourceId)
                .ToList();
            if (candidates.Count == 0) return null;
            // Never double-book: if no capable person is free, leave the job unstaffed.
            var free = candidates.OrderBy(_ => _faker.Random.Int()).FirstOrDefault(id => IsFree(id, s, e, 100m));
            return free == Guid.Empty ? null : free;
        }

        // Helpers support the lead at 50 % — checked and tracked so total load stays ≤ 100 %.
        public IReadOnlyList<Guid> PickHelpers(Guid leadId, IReadOnlyList<Guid> required, int count, DateTime s, DateTime e)
        {
            var helpers = _cohort.People
                .Where(p => p.ResourceId != leadId
                    && _personSkills.TryGetValue(p.ResourceId, out var sk)
                    && required.Any(c => sk.Contains(c))
                    && IsFree(p.ResourceId, s, e, 50m))
                .OrderBy(_ => _faker.Random.Int())
                .Take(count)
                .Select(p => p.ResourceId)
                .ToList();
            foreach (var id in helpers) MarkBusy(id, s, e, 50m);
            return helpers;
        }

        public MachineFactory.SeededMachine? PickMachine(string role, DateTime s, DateTime e)
        {
            var pool = _cohort.Machines.Where(m => m.Role == role).OrderBy(_ => _faker.Random.Int()).ToList();
            if (pool.Count == 0) return null;
            // Machines are Exclusive without exception, so a free slot means the whole window.
            return pool.FirstOrDefault(m => IsFree(m.Id, s, e, 100m));
        }

        public ToolFactory.SeededTool? PickTool(string role, DateTime s, DateTime e)
        {
            // A role's tools are homogeneous (machines are Exclusive; forklifts/cranes Fractional).
            var pool = _cohort.Tools.Where(t => t.Role == role).OrderBy(_ => _faker.Random.Int()).ToList();
            if (pool.Count == 0) return null;
            // Fractional tools are shared at 50 % — check capacity the same way as for helpers.
            var pct = pool[0].AllocationMode == "Fractional" ? 50m : 100m;
            return pool.FirstOrDefault(t => IsFree(t.Id, s, e, pct));
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
                await w.WriteAsync("new", NpgsqlDbType.Varchar);
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

    /// <summary>
    /// Writes <paramref name="count"/> unscheduled, top-level leaf requests (no start/end, no
    /// assignments/requirements) — the demo backlog. They stay site-neutral (SiteModelFactory only
    /// sites assigned requests), so they appear in every site's backlog and can be dragged onto any
    /// space. Names mirror the scheduled jobs' "{Verb} {Noun} — {Site}".
    /// </summary>
    /// <summary>One unscheduled backlog item, with what it needs and what can satisfy it.</summary>
    private sealed record BacklogItem(Guid Id, IReadOnlyList<Guid> RequiredCriteria, string? TargetTypeKey);

    private static async Task<IReadOnlyList<BacklogItem>> WriteBacklogAsync(
        NpgsqlConnection conn, IReadOnlyList<FacilityCohort> cohorts,
        IReadOnlyDictionary<string, Guid> criteria, Faker faker, int count)
    {
        var now = DateTime.UtcNow;
        var items = new List<BacklogItem>(count + 2);

        // Which machine type each machine-driven archetype needs, read from the machines the
        // cohort actually owns — the archetype names a role, and the role is what a type answers.
        static string? TypeKeyFor(FacilityCohort cohort, JobArchetype arch) =>
            arch.MachineRole is null
                ? null
                : cohort.Machines.FirstOrDefault(m => m.Role == arch.MachineRole)?.TypeKey;

        var planned = new List<(string Name, int Hours, IReadOnlyList<Guid> Required, string? TargetTypeKey)>(count + 2);

        for (var i = 0; i < count; i++)
        {
            var cohort = faker.PickRandom(cohorts.AsEnumerable());
            var arch = faker.PickRandom(cohort.Facility.Archetypes.AsEnumerable());
            var name = $"{arch.Verb} {arch.Noun} — {cohort.Facility.SiteCode}";
            var hours = Math.Max(1, faker.Random.Int(arch.MinHours, arch.MaxHours));
            var typeKey = TypeKeyFor(cohort, arch);

            // Requirements only on the machine-driven items. A room-targeted item carrying a person
            // skill would be reported as having no compatible resource for the wrong reason — the
            // solver matches the target type's capabilities, and a room holds no person skills.
            var required = typeKey is null
                ? (IReadOnlyList<Guid>)[]
                : arch.RequiredSkills.Select(sk => criteria[sk]).ToList();

            planned.Add((name, hours, required, typeKey));
        }

        // Two that cannot be satisfied by anything, on purpose. Auto-scheduling that always
        // succeeds teaches nobody what it does when it cannot — these report "No compatible
        // resource" against a skill no person and no machine in the tenant holds.
        var weldInspection = (IReadOnlyList<Guid>)new[] { criteria[SkillCatalog.WeldInspection] };
        planned.Add(("Certify weld procedures — FWF", 6, weldInspection, "person"));
        planned.Add(("Commission new test rig — PPF", 4, weldInspection, "test_station"));

        using (var w = await conn.BeginBinaryImportAsync(RequestCopy))
        {
            for (var i = 0; i < planned.Count; i++)
            {
                var (name, hours, required, typeKey) = planned[i];
                if (name.Length > 200) name = name[..200];
                var id = Guid.NewGuid();
                items.Add(new BacklogItem(id, required, typeKey));

                await w.StartRowAsync();
                await w.WriteAsync(id, NpgsqlDbType.Uuid);
                await w.WriteAsync(name, NpgsqlDbType.Varchar);
                await w.WriteNullAsync();                                  // description
                await w.WriteNullAsync();                                  // start_ts  → unscheduled
                await w.WriteNullAsync();                                  // end_ts
                await w.WriteAsync(hours, NpgsqlDbType.Integer);          // minimal_duration_value
                await w.WriteAsync("hours", NpgsqlDbType.Varchar);        // minimal_duration_unit
                await w.WriteAsync("new", NpgsqlDbType.Varchar);      // status
                await w.WriteAsync(now, NpgsqlDbType.TimestampTz);
                await w.WriteAsync(now, NpgsqlDbType.TimestampTz);
                await w.WriteAsync(true, NpgsqlDbType.Boolean);           // scheduling_settings_apply
                await w.WriteAsync("leaf", NpgsqlDbType.Varchar);         // planning_mode
                await w.WriteAsync(i, NpgsqlDbType.Integer);              // sort_order
                await w.WriteNullAsync();                                  // parent_request_id → top-level
            }
            await w.CompleteAsync();
        }

        return items;
    }

    private static async Task<int> WriteRequirementsAsync(
        NpgsqlConnection conn,
        IEnumerable<(Guid RequestId, IReadOnlyList<Guid> Criteria)> requests,
        IReadOnlyDictionary<string, Guid> criteria)
    {
        var byId = criteria.ToDictionary(kv => kv.Value, kv => SkillCatalog.ByKey(kv.Key));
        var count = 0;
        using var w = await conn.BeginBinaryImportAsync(
            "COPY public.request_requirements (request_id, criterion_id, value, operator, allowed_values) FROM STDIN (FORMAT BINARY)");
        foreach (var (requestId, required) in requests)
            foreach (var cid in required)
            {
                var skill = byId[cid];
                await w.StartRowAsync();
                await w.WriteAsync(requestId, NpgsqlDbType.Uuid);
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
                if (pct is null) await w.WriteNullAsync(); else await w.WriteAsync(pct.Value, NpgsqlDbType.Numeric);
                await w.WriteAsync("Planned", NpgsqlDbType.Varchar);
                await w.WriteAsync(now, NpgsqlDbType.TimestampTz);
                await w.WriteAsync(now, NpgsqlDbType.TimestampTz);
                count++;
            }
        await w.CompleteAsync();
        return count;
    }
}
