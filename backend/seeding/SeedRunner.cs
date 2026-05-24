using Bogus;
using Npgsql;
using Orkyo.Foundation.Seed.Distributions;
using Orkyo.Foundation.Seed.Factories;
using Orkyo.Foundation.Seed.Profiles;
using Orkyo.Foundation.Seed.Scales;

namespace Orkyo.Foundation.Seed;

public sealed record SeedReport(
    int Sites, int Spaces, int JobTitles, int Departments, int People,
    int PersonGroups, int PersonGroupMembers,
    int Requests, int Assignments, TimeSpan Duration);

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

        var sites = await SpaceFactories.SeedSitesAsync(conn, tx, profile, scale, faker);
        var spaceTypeId = await SpaceFactories.ResolveSpaceResourceTypeIdAsync(conn, tx);
        var spaces = await SpaceFactories.SeedSpacesAsync(conn, tx, profile, scale, faker, sites, spaceTypeId);

        var jobTitles = await PeopleFactories.SeedJobTitlesAsync(conn, tx, profile, scale, faker);
        var departments = await PeopleFactories.SeedDepartmentsAsync(conn, tx, profile, scale, faker);
        var personTypeId = await PeopleFactories.ResolvePersonResourceTypeIdAsync(conn, tx);
        var people = await PeopleFactories.SeedPeopleAsync(
            conn, tx, profile, scale, faker, personTypeId, jobTitles, departments);
        var personGroups = await PeopleFactories.SeedPersonGroupsAsync(
            conn, tx, profile, scale, faker, personTypeId);
        var personGroupMemberCount = await PeopleFactories.SeedPersonGroupMembersAsync(
            conn, tx, faker, people, personGroups, personTypeId);

        var requests = await WorkItemFactories.SeedRequestsAsync(
            conn, tx, profile, scale, faker, timing);
        var assignmentCount = await WorkItemFactories.SeedAssignmentsAsync(
            conn, tx, faker, requests, people, spaces);

        await tx.CommitAsync();
        sw.Stop();

        return new SeedReport(
            Sites: sites.Count,
            Spaces: spaces.Count,
            JobTitles: jobTitles.Count,
            Departments: departments.Count,
            People: people.Count,
            PersonGroups: personGroups.Count,
            PersonGroupMembers: personGroupMemberCount,
            Requests: requests.Count,
            Assignments: assignmentCount,
            Duration: sw.Elapsed);
    }
}
