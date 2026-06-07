using Bogus;
using Npgsql;
using NpgsqlTypes;
using Orkyo.Foundation.Seed.Narrative;

namespace Orkyo.Foundation.Seed.Factories;

/// <summary>
/// Seeds the availability model: site-scoped <c>availability_events</c> (public holidays, summer/winter
/// shutdowns, plus one tool-type-scoped maintenance window to exercise <c>availability_event_scopes</c>),
/// and per-person <c>resource_absences</c> (vacation for everyone, occasional sickness, periodic
/// training). Makes utilization and availability views look like a real operation.
/// </summary>
public static class AvailabilityFactory
{
    public sealed record Result(int Events, int Absences);

    public static async Task<Result> SeedAsync(
        NpgsqlConnection conn,
        YearCalendar cal,
        IReadOnlyList<Factories.SpaceFactories.SeededSite> sites,
        IReadOnlyList<Factories.PeopleFactories.SeededPerson> people,
        Faker faker)
    {
        Guid toolTypeId;
        await using (var cmd = new NpgsqlCommand("SELECT id FROM public.resource_types WHERE key='tool' LIMIT 1", conn))
            toolTypeId = (Guid)(await cmd.ExecuteScalarAsync())!;

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
        using (var w = await conn.BeginBinaryImportAsync(
            "COPY public.resource_absences (id, resource_id, absence_type, title, notes, start_ts, end_ts, " +
            "is_recurring, recurrence_rule, enabled, created_at, updated_at) FROM STDIN (FORMAT BINARY)"))
        {
            var spanDays = Math.Max(14, (int)(cal.End - cal.Start).TotalDays);
            foreach (var p in people)
            {
                // Everyone takes a vacation.
                var vacStart = cal.Start.AddDays(faker.Random.Int(0, spanDays - 12));
                absences += await WriteAbsence(w, p.ResourceId, "vacation", "Annual Leave",
                    vacStart, vacStart.AddDays(faker.Random.Int(5, 12)), now);

                if (faker.Random.Bool(0.15f))
                {
                    var s = cal.Start.AddDays(faker.Random.Int(0, spanDays - 3));
                    absences += await WriteAbsence(w, p.ResourceId, "sickness", "Sick Leave", s, s.AddDays(faker.Random.Int(1, 3)), now);
                }
                if (faker.Random.Bool(0.10f))
                {
                    var s = cal.Start.AddDays(faker.Random.Int(0, spanDays - 2));
                    absences += await WriteAbsence(w, p.ResourceId, "training", "Certification Training", s, s.AddDays(faker.Random.Int(1, 2)), now);
                }
            }
            await w.CompleteAsync();
        }

        return new Result(events, absences);
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
