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
/// Request hierarchy: ~1/8 of total requests are 'summary' parents (no
/// timestamps — scheduling is derived from children). The remaining 7/8 are
/// 'leaf' children distributed round-robin across parents. Parents are written
/// first in a separate COPY pass because the FK is not deferrable.
///
/// Only leaf requests receive resource assignments (summary rows have no
/// timestamps and are skipped by the assignments step automatically).
/// </summary>
public static class WorkItemFactories
{
    public sealed record SeededRequest(Guid Id, DateTime? StartTs, DateTime? EndTs, string Status);

    public static async Task<IReadOnlyList<SeededRequest>> SeedRequestsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        IProfile profile, IScale scale, Faker faker,
        RequestTimeDistribution timing)
    {
        var parentCount = Math.Max(1, scale.Requests / 8);
        var leafCount = scale.Requests - parentCount;

        // ── Phase 1: build parent (summary) rows ─────────────────────────────
        var parentRows = new List<(SeededRequest Req, string Name)>(parentCount);
        for (var i = 0; i < parentCount; i++)
        {
            var verb = profile.RequestNameVerbs[faker.Random.Int(0, profile.RequestNameVerbs.Count - 1)];
            var noun = profile.RequestNameNouns[faker.Random.Int(0, profile.RequestNameNouns.Count - 1)];
            var name = $"{verb} {noun}";
            if (name.Length > 200) name = name[..200];
            parentRows.Add((new SeededRequest(Guid.NewGuid(), null, null, "planned"), name));
        }

        // ── Phase 2: build leaf rows ──────────────────────────────────────────
        // Round-robin distribution gives even parent sizes; sort_order is
        // sequential within each parent.
        var childCounters = new int[parentCount];
        var leafRows = new List<(SeededRequest Req, string Name, Guid ParentId, int SortOrder)>(leafCount);
        for (var i = 0; i < leafCount; i++)
        {
            var parentIdx = i % parentCount;
            var slot = timing.Pick();
            var range = timing.Generate(slot);
            var verb = profile.RequestNameVerbs[faker.Random.Int(0, profile.RequestNameVerbs.Count - 1)];
            var noun = profile.RequestNameNouns[faker.Random.Int(0, profile.RequestNameNouns.Count - 1)];
            var name = $"{verb} {noun}";
            if (name.Length > 200) name = name[..200];
            leafRows.Add((
                Req: new SeededRequest(Guid.NewGuid(), range?.start, range?.end, timing.StatusFor(slot)),
                Name: name,
                ParentId: parentRows[parentIdx].Req.Id,
                SortOrder: childCounters[parentIdx]++));
        }

        var now = DateTime.UtcNow;
        _ = tx;
        const string copySql =
            "COPY public.requests (" +
                "id, name, description, start_ts, end_ts, " +
                "minimal_duration_value, minimal_duration_unit, status, " +
                "created_at, updated_at, scheduling_settings_apply, " +
                "planning_mode, sort_order, parent_request_id" +
            ") FROM STDIN (FORMAT BINARY)";

        // ── Write parents first (FK requires parents before children) ─────────
        using (var writer = await conn.BeginBinaryImportAsync(copySql))
        {
            for (var i = 0; i < parentRows.Count; i++)
            {
                var (req, name) = parentRows[i];
                await writer.StartRowAsync();
                await writer.WriteAsync(req.Id, NpgsqlDbType.Uuid);
                await writer.WriteAsync(name, NpgsqlDbType.Varchar);
                await writer.WriteNullAsync();                                  // description
                await writer.WriteNullAsync();                                  // start_ts
                await writer.WriteNullAsync();                                  // end_ts
                await writer.WriteAsync(60, NpgsqlDbType.Integer);              // minimal_duration_value
                await writer.WriteAsync("minutes", NpgsqlDbType.Varchar);       // minimal_duration_unit
                await writer.WriteAsync("planned", NpgsqlDbType.Varchar);       // status
                await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
                await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
                await writer.WriteAsync(false, NpgsqlDbType.Boolean);           // scheduling_settings_apply
                await writer.WriteAsync("summary", NpgsqlDbType.Varchar);       // planning_mode
                await writer.WriteAsync(i, NpgsqlDbType.Integer);               // sort_order
                await writer.WriteNullAsync();                                  // parent_request_id
            }
            await writer.CompleteAsync();
        }

        // ── Write leaves ──────────────────────────────────────────────────────
        using (var writer = await conn.BeginBinaryImportAsync(copySql))
        {
            foreach (var (req, name, parentId, sortOrder) in leafRows)
            {
                await writer.StartRowAsync();
                await writer.WriteAsync(req.Id, NpgsqlDbType.Uuid);
                await writer.WriteAsync(name, NpgsqlDbType.Varchar);
                await writer.WriteNullAsync();                                  // description
                if (req.StartTs is null) await writer.WriteNullAsync(); else await writer.WriteAsync(req.StartTs.Value, NpgsqlDbType.TimestampTz);
                if (req.EndTs is null) await writer.WriteNullAsync(); else await writer.WriteAsync(req.EndTs.Value, NpgsqlDbType.TimestampTz);
                await writer.WriteAsync(60, NpgsqlDbType.Integer);              // minimal_duration_value
                await writer.WriteAsync("minutes", NpgsqlDbType.Varchar);       // minimal_duration_unit
                await writer.WriteAsync(req.Status, NpgsqlDbType.Varchar);
                await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
                await writer.WriteAsync(now, NpgsqlDbType.TimestampTz);
                await writer.WriteAsync(true, NpgsqlDbType.Boolean);            // scheduling_settings_apply
                await writer.WriteAsync("leaf", NpgsqlDbType.Varchar);          // planning_mode
                await writer.WriteAsync(sortOrder, NpgsqlDbType.Integer);       // sort_order
                await writer.WriteAsync(parentId, NpgsqlDbType.Uuid);           // parent_request_id
            }
            await writer.CompleteAsync();
        }

        var all = new List<SeededRequest>(scale.Requests);
        all.AddRange(parentRows.Select(p => p.Req));
        all.AddRange(leafRows.Select(l => l.Req));
        return all;
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
