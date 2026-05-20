using Bogus;
using Npgsql;
using NpgsqlTypes;
using Orkyo.Foundation.Seed.Distributions;
using Orkyo.Foundation.Seed.Profiles;
using Orkyo.Foundation.Seed.Scales;

namespace Orkyo.Foundation.Seed.Factories;

/// <summary>
/// Seeds requests and their resource assignments.
///
/// MVP scope:
///  - All requests are leaf (no summary/container hierarchy).
///  - No criteria / templates / requirements yet — those plug in via PresetApplier later.
///  - Each scheduled request gets 1-3 random people assigned at allocation_percent=100.
/// </summary>
public static class WorkItemFactories
{
    public sealed record SeededRequest(Guid Id, DateTime? StartTs, DateTime? EndTs, string Status);

    public static async Task<IReadOnlyList<SeededRequest>> SeedRequestsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        IProfile profile, IScale scale, Faker faker,
        RequestTimeDistribution timing)
    {
        // Two-phase: build the row tuples first so we can return their IDs for
        // the resource-assignments step, then bulk-import with COPY.
        var rows = new List<SeededRequest>(scale.Requests);
        var names = new List<string>(scale.Requests);
        for (var i = 0; i < scale.Requests; i++)
        {
            var slot = timing.Pick();
            var range = timing.Generate(slot);
            var verb = profile.RequestNameVerbs[faker.Random.Int(0, profile.RequestNameVerbs.Count - 1)];
            var noun = profile.RequestNameNouns[faker.Random.Int(0, profile.RequestNameNouns.Count - 1)];
            var name = $"{verb} {noun}";
            if (name.Length > 200) name = name[..200];
            names.Add(name);
            rows.Add(new SeededRequest(
                Id: Guid.NewGuid(),
                StartTs: range?.start,
                EndTs: range?.end,
                Status: timing.StatusFor(slot)));
        }

        var now = DateTime.UtcNow;
        _ = tx;
        using var writer = await conn.BeginBinaryImportAsync(
            "COPY public.requests (" +
                "id, name, description, start_ts, end_ts, " +
                "minimal_duration_value, minimal_duration_unit, status, " +
                "created_at, updated_at, scheduling_settings_apply, planning_mode, sort_order" +
            ") FROM STDIN (FORMAT BINARY)");

        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            await writer.StartRowAsync();
            await writer.WriteAsync(r.Id, NpgsqlDbType.Uuid);
            await writer.WriteAsync(names[i], NpgsqlDbType.Varchar);
            await writer.WriteNullAsync();                                                  // description
            if (r.StartTs is null) await writer.WriteNullAsync(); else await writer.WriteAsync(r.StartTs.Value, NpgsqlDbType.TimestampTz);
            if (r.EndTs is null) await writer.WriteNullAsync(); else await writer.WriteAsync(r.EndTs.Value, NpgsqlDbType.TimestampTz);
            await writer.WriteAsync(60, NpgsqlDbType.Integer);                                // minimal_duration_value
            await writer.WriteAsync("minutes", NpgsqlDbType.Varchar);                         // minimal_duration_unit
            await writer.WriteAsync(r.Status, NpgsqlDbType.Varchar);
            await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
            await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
            await writer.WriteAsync(true, NpgsqlDbType.Boolean);                              // scheduling_settings_apply
            await writer.WriteAsync("leaf", NpgsqlDbType.Varchar);                            // planning_mode
            await writer.WriteAsync(i, NpgsqlDbType.Integer);                                 // sort_order
        }
        await writer.CompleteAsync();
        return rows;
    }

    public static async Task<int> SeedAssignmentsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Faker faker,
        IReadOnlyList<SeededRequest> requests,
        IReadOnlyList<PeopleFactories.SeededPerson> people,
        IReadOnlyList<SpaceFactories.SeededSpace> spaces)
    {
        var scheduled = requests.Where(r => r.StartTs.HasValue && r.EndTs.HasValue).ToList();
        if (scheduled.Count == 0) return 0;

        var now = DateTime.UtcNow;
        var count = 0;
        _ = tx;
        using var writer = await conn.BeginBinaryImportAsync(
            "COPY public.resource_assignments (" +
                "id, request_id, resource_id, start_utc, end_utc, " +
                "allocation_percent, assignment_status, created_at, updated_at" +
            ") FROM STDIN (FORMAT BINARY)");

        foreach (var request in scheduled)
        {
            // Assign one space resource per scheduled request (spaces are Exclusive resources).
            if (spaces.Count > 0)
            {
                var space = spaces[faker.Random.Int(0, spaces.Count - 1)];
                await writer.StartRowAsync();
                await writer.WriteAsync(Guid.NewGuid(), NpgsqlDbType.Uuid);
                await writer.WriteAsync(request.Id, NpgsqlDbType.Uuid);
                await writer.WriteAsync(space.Id, NpgsqlDbType.Uuid);
                await writer.WriteAsync(request.StartTs!.Value, NpgsqlDbType.TimestampTz);
                await writer.WriteAsync(request.EndTs!.Value, NpgsqlDbType.TimestampTz);
                await writer.WriteAsync(100m, NpgsqlDbType.Numeric);
                await writer.WriteAsync("Planned", NpgsqlDbType.Varchar);
                await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
                await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
                count++;
            }

            // Assign 1-3 distinct people per scheduled request.
            if (people.Count > 0)
            {
                var assigneeCount = faker.Random.Int(1, Math.Min(3, people.Count));
                var assignees = new HashSet<Guid>();
                while (assignees.Count < assigneeCount)
                    assignees.Add(people[faker.Random.Int(0, people.Count - 1)].ResourceId);
                foreach (var personResourceId in assignees)
                {
                    await writer.StartRowAsync();
                    await writer.WriteAsync(Guid.NewGuid(), NpgsqlDbType.Uuid);
                    await writer.WriteAsync(request.Id, NpgsqlDbType.Uuid);
                    await writer.WriteAsync(personResourceId, NpgsqlDbType.Uuid);
                    await writer.WriteAsync(request.StartTs!.Value, NpgsqlDbType.TimestampTz);
                    await writer.WriteAsync(request.EndTs!.Value, NpgsqlDbType.TimestampTz);
                    await writer.WriteAsync(100m, NpgsqlDbType.Numeric);
                    await writer.WriteAsync("Planned", NpgsqlDbType.Varchar);
                    await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
                    await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
                    count++;
                }
            }
        }
        await writer.CompleteAsync();
        return count;
    }
}
