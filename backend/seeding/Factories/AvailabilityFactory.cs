using Bogus;
using Npgsql;
using NpgsqlTypes;
using Orkyo.Foundation.Seed.Narrative;

namespace Orkyo.Foundation.Seed.Factories;

/// <summary>
/// Seeds the availability model: site-scoped <c>availability_events</c> (public holidays, summer/winter
/// shutdowns, plus one tool-type-scoped maintenance window to exercise
/// <c>availability_event_scopes</c>), and per-person <c>resource_absences</c> (vacation for everyone,
/// occasional sickness, periodic training). Makes utilization and availability views look like a real
/// operation.
/// </summary>
public static class AvailabilityFactory
{
    public sealed record Result(
        int Events,
        int Absences,
        /// <summary>
        /// The vacations, so the narrative seeder can book someone over one on purpose.
        /// </summary>
        /// <remarks>
        /// Without an arranged overlap the absence-overlap conflict has no example anywhere in
        /// the demo. It is one per cohort, injected by <c>InjectAbsenceOverlaps</c>, which books
        /// the person straight onto the capacity ledger rather than asking whether they are free.
        /// </remarks>
        IReadOnlyList<(Guid PersonId, DateTime Start, DateTime End)> Vacations,
        /// <summary>
        /// Every absence window, so the narrative seeder's capacity ledger can treat time off as
        /// unavailable. Without this the booking pass sees only capacity and lands work on
        /// vacations by accident — 169 of them at medium scale, which buries the one arranged
        /// example under a wall of unintended conflicts.
        /// </summary>
        IReadOnlyList<(Guid ResourceId, DateTime Start, DateTime End)> AbsenceWindows);

    public static async Task<Result> SeedAsync(
        NpgsqlConnection conn,
        YearCalendar cal,
        IReadOnlyList<Factories.SpaceFactories.SeededSite> sites,
        IReadOnlyList<Factories.PeopleFactories.SeededPerson> people,
        Faker faker)
    {
        // The seed owns the tool type (built-ins are gone); ToolFactory has already run, so
        // this is a no-op re-read of the same row.
        var toolTypeId = await ToolFactory.EnsureToolTypeAsync(conn);

        var now = DateTime.UtcNow;
        var events = 0;
        var scopeRows = new List<(Guid EventId, Guid TargetId)>();

        using (var w = await conn.BeginBinaryImportAsync(
            "COPY public.availability_events (id, site_id, title, description, event_type, default_effect, " +
            "start_ts, end_ts, is_recurring, recurrence_rule, enabled, created_at, updated_at) FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var site in sites)
            {
                foreach (var h in cal.Holidays)
                {
                    var start = h.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                    events += await WriteEvent(w, site.Id, "Public Holiday", "public_holiday", start, start.AddDays(1), now);
                }
                foreach (var (s, e) in cal.Shutdowns)
                {
                    var title = s.Month >= 11 || s.Month == 1 ? "Winter Holiday Shutdown" : "Summer Maintenance Shutdown";
                    events += await WriteEvent(w, site.Id, title, "shutdown", s, e, now);
                }
                // One tool-scoped maintenance window mid-window (exercises availability_event_scopes).
                var mStart = cal.Start.AddMonths(4);
                var eventId = Guid.NewGuid();
                await WriteEvent(w, site.Id, "Equipment Maintenance Window", "maintenance", mStart, mStart.AddDays(1), now, eventId);
                events++;
                scopeRows.Add((eventId, toolTypeId));
            }
            await w.CompleteAsync();
        }

        using (var w = await conn.BeginBinaryImportAsync(
            "COPY public.availability_event_scopes (id, availability_event_id, target_type, target_id, effect) FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var (eventId, targetId) in scopeRows)
            {
                await w.StartRowAsync();
                await w.WriteAsync(Guid.NewGuid(), NpgsqlDbType.Uuid);
                await w.WriteAsync(eventId, NpgsqlDbType.Uuid);
                await w.WriteAsync("resource_type", NpgsqlDbType.Varchar);
                await w.WriteAsync(targetId, NpgsqlDbType.Uuid);
                await w.WriteAsync("closed", NpgsqlDbType.Varchar);
            }
            await w.CompleteAsync();
        }

        var absences = 0;
        var vacations = new List<(Guid PersonId, DateTime Start, DateTime End)>(people.Count);
        var allAbsences = new List<(Guid ResourceId, DateTime Start, DateTime End)>(people.Count * 2);
        using (var w = await conn.BeginBinaryImportAsync(
            "COPY public.resource_absences (id, resource_id, absence_type, title, notes, start_ts, end_ts, " +
            "is_recurring, recurrence_rule, enabled, created_at, updated_at) FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var p in people)
                foreach (var leave in BuildLeave(faker, cal.Start, cal.End))
                {
                    absences += await WriteAbsence(w, p.ResourceId, leave.Type, leave.Title, leave.Start, leave.End, now);
                    allAbsences.Add((p.ResourceId, leave.Start, leave.End));
                    if (leave.Type == "vacation") vacations.Add((p.ResourceId, leave.Start, leave.End));
                }
            await w.CompleteAsync();
        }

        return new Result(events, absences, vacations, allAbsences);
    }

    private sealed record LeaveBlock(string Type, string Title, int CalendarDays);

    /// <summary>One placed absence: a leave block with the dates it occupies.</summary>
    public sealed record LeaveWindow(string Type, string Title, DateTime Start, DateTime End);

    /// <summary>
    /// One person's absences across the whole calendar — the leave generator, kept free of the
    /// database so its two invariants can be tested directly: a person's absences never overlap
    /// each other, and a full-time year carries roughly 25 working days of vacation.
    /// </summary>
    /// <remarks>
    /// A leave year at a time: a calendar covering more than a year has to grant the entitlement
    /// in every one of them, not once overall. Within a year the blocks are laid end to end —
    /// gap, block, gap, block — which keeps them apart whatever their number or length. Equal
    /// slices would not do: a six-week sick leave does not fit a tenth of a year.
    /// </remarks>
    public static List<LeaveWindow> BuildLeave(Faker faker, DateTime calStart, DateTime calEnd)
    {
        var placed = new List<LeaveWindow>();

        for (var yearStart = calStart; yearStart < calEnd; yearStart = yearStart.AddYears(1))
        {
            var yearEnd = yearStart.AddYears(1) < calEnd ? yearStart.AddYears(1) : calEnd;
            var blocks = LeaveYear(faker, (yearEnd - yearStart).TotalDays / 365.0)
                .OrderBy(_ => faker.Random.Int())
                .ToList();

            var yearDays = (int)(yearEnd - yearStart).TotalDays;
            var gaps = RandomSplit(faker, Math.Max(0, yearDays - blocks.Sum(b => b.CalendarDays)), blocks.Count + 1);

            var cursor = yearStart;
            for (var i = 0; i < blocks.Count; i++)
            {
                cursor = cursor.AddDays(gaps[i]);
                var start = AlignLongBlock(cursor, blocks[i].CalendarDays, yearEnd);
                // Stop at the year boundary, not at the calendar's: Monday alignment nudges each
                // block forward a little, and left unchecked that drift lets the last block of a
                // year run into the first block of the next one.
                if (start.AddDays(blocks[i].CalendarDays) > yearEnd) break;

                var end = start.AddDays(blocks[i].CalendarDays);
                placed.Add(new LeaveWindow(blocks[i].Type, blocks[i].Title, start, end));
                cursor = end;
            }
        }

        return placed;
    }

    /// <summary>
    /// One person's leave for one year, as calendar-day blocks.
    /// </summary>
    /// <remarks>
    /// Vacation totals roughly 25 working days, the entitlement of a full-time employee: a long
    /// block, a week, and a few odd days. Blocks are calendar days, so a working week is seven.
    /// Sickness and training are what stop the demo reading as a workforce that is never ill and
    /// never trains — before this, 15 % of people were ever ill and 11 % ever trained.
    ///
    /// <paramref name="share"/> is the fraction of a year the window covers, so a trailing
    /// part-year earns a proportional share rather than a full entitlement.
    /// </remarks>
    private static List<LeaveBlock> LeaveYear(Faker faker, double share)
    {
        var blocks = new List<LeaveBlock>(8);
        bool Earned() => faker.Random.Double() < share;

        // 15 + 5 + ~5 working days ≈ 25.
        if (Earned()) blocks.Add(new("vacation", "Annual Leave", 21));
        if (Earned()) blocks.Add(new("vacation", "Annual Leave", 7));
        for (var i = 0; i < 3; i++)
            if (Earned()) blocks.Add(new("vacation", "Annual Leave", faker.Random.Int(1, 2)));

        // Short bouts most years, a second and third less often, and the occasional long
        // absence — an injury or an operation — that dominates a person's year. Together
        // roughly 8 working days, which is where a German shop floor sits.
        if (Earned() && faker.Random.Bool(0.85f)) blocks.Add(new("sickness", "Sick Leave", faker.Random.Int(3, 7)));
        if (Earned() && faker.Random.Bool(0.55f)) blocks.Add(new("sickness", "Sick Leave", faker.Random.Int(2, 7)));
        if (Earned() && faker.Random.Bool(0.25f)) blocks.Add(new("sickness", "Sick Leave", faker.Random.Int(2, 5)));
        if (Earned() && faker.Random.Bool(0.07f)) blocks.Add(new("sickness", "Long-term Sick Leave", faker.Random.Int(14, 42)));

        // A shop floor keeps its tickets current: an annual safety refresher for nearly
        // everyone, plus a longer certification course for a third of them.
        if (Earned() && faker.Random.Bool(0.80f)) blocks.Add(new("training", "Safety Refresher", faker.Random.Int(2, 3)));
        if (Earned() && faker.Random.Bool(0.40f)) blocks.Add(new("training", "Certification Training", faker.Random.Int(3, 5)));

        return blocks;
    }

    /// <summary>
    /// Nudges a block of a week or more forward to the next Monday, which is how leave of that
    /// length is actually taken. Shorter blocks, and any nudge that would run past
    /// <paramref name="limit"/>, are left where they are.
    /// </summary>
    private static DateTime AlignLongBlock(DateTime start, int days, DateTime limit)
    {
        if (days < 5) return start;

        var aligned = start.AddDays(((int)DayOfWeek.Monday - (int)start.DayOfWeek + 7) % 7);
        return aligned.AddDays(days) <= limit ? aligned : start;
    }

    /// <summary>
    /// Splits <paramref name="total"/> days into <paramref name="parts"/> random gaps that sum
    /// back to it — the spacing between one person's absences across a year.
    /// </summary>
    private static int[] RandomSplit(Faker faker, int total, int parts)
    {
        var cuts = Enumerable.Range(0, parts - 1)
            .Select(_ => faker.Random.Int(0, total))
            .OrderBy(x => x)
            .ToArray();

        var result = new int[parts];
        var previous = 0;
        for (var i = 0; i < parts - 1; i++)
        {
            result[i] = cuts[i] - previous;
            previous = cuts[i];
        }
        result[^1] = total - previous;
        return result;
    }

    private static async Task<int> WriteEvent(
        NpgsqlBinaryImporter w, Guid siteId, string title, string type,
        DateTime start, DateTime end, DateTime now, Guid? id = null)
    {
        await w.StartRowAsync();
        await w.WriteAsync(id ?? Guid.NewGuid(), NpgsqlDbType.Uuid);
        await w.WriteAsync(siteId, NpgsqlDbType.Uuid);
        await w.WriteAsync(title, NpgsqlDbType.Varchar);
        await w.WriteNullAsync();                              // description
        await w.WriteAsync(type, NpgsqlDbType.Varchar);
        await w.WriteAsync("closed", NpgsqlDbType.Varchar);    // default_effect
        await w.WriteAsync(start, NpgsqlDbType.TimestampTz);
        await w.WriteAsync(end, NpgsqlDbType.TimestampTz);
        await w.WriteAsync(false, NpgsqlDbType.Boolean);       // is_recurring
        await w.WriteNullAsync();                              // recurrence_rule
        await w.WriteAsync(true, NpgsqlDbType.Boolean);        // enabled
        await w.WriteAsync(now, NpgsqlDbType.TimestampTz);
        await w.WriteAsync(now, NpgsqlDbType.TimestampTz);
        return 1;
    }

    private static async Task<int> WriteAbsence(
        NpgsqlBinaryImporter w, Guid resourceId, string type, string title,
        DateTime start, DateTime end, DateTime now)
    {
        await w.StartRowAsync();
        await w.WriteAsync(Guid.NewGuid(), NpgsqlDbType.Uuid);
        await w.WriteAsync(resourceId, NpgsqlDbType.Uuid);
        await w.WriteAsync(type, NpgsqlDbType.Varchar);
        await w.WriteAsync(title, NpgsqlDbType.Varchar);
        await w.WriteNullAsync();                              // notes
        await w.WriteAsync(start, NpgsqlDbType.TimestampTz);
        await w.WriteAsync(end, NpgsqlDbType.TimestampTz);
        await w.WriteAsync(false, NpgsqlDbType.Boolean);
        await w.WriteNullAsync();
        await w.WriteAsync(true, NpgsqlDbType.Boolean);
        await w.WriteAsync(now, NpgsqlDbType.TimestampTz);
        await w.WriteAsync(now, NpgsqlDbType.TimestampTz);
        return 1;
    }
}
