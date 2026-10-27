using Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Orkyo.Foundation.Tests.Integration;

/// <summary>
/// DB-backed tests for <see cref="WorkerJobCoordinator"/> — the restart-safe,
/// cross-instance scheduling guard for worker jobs (journal row + per-job Postgres
/// advisory lock). Each test uses a unique job name so runs don't interfere. The journal is
/// stamped from the injected clock, so a <see cref="FakeTimeProvider"/> drives both the due
/// check and the backoff without waiting.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Database)]
[Collection(PostgresCollection.Name)]
public sealed class WorkerJobCoordinatorTests
{
    private readonly PostgresFixture _fixture;
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);

    public WorkerJobCoordinatorTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private WorkerJobCoordinator BuildCoordinator() =>
        new(_fixture.CreateConnectionFactory(), NullLogger<WorkerJobCoordinator>.Instance, _time);

    private static Task Throw(CancellationToken _) => throw new InvalidOperationException("boom");

    private static string UniqueJob() => $"test-job-{Guid.NewGuid():N}";

    private static readonly Func<DateTime, DateTime, bool> AlwaysDue = (_, _) => true;

    private static Func<DateTime, DateTime, bool> DueIfOlderThan(TimeSpan interval) =>
        (now, last) => now - last > interval;

    [Fact]
    public async Task RunIfDueAsync_FirstRun_RunsAndJournals()
    {
        var coordinator = BuildCoordinator();
        var job = UniqueJob();
        var ran = false;

        var outcome = await coordinator.RunIfDueAsync(job, AlwaysDue, _ => { ran = true; return Task.CompletedTask; }, CancellationToken.None);

        outcome.Should().Be(WorkerJobOutcome.Ran);
        ran.Should().BeTrue();

        var (completedAt, result, _, _) = await ReadJournalAsync(job);
        completedAt.Should().NotBeNull();
        result.Should().Be("succeeded");
    }

    [Fact]
    public async Task RunIfDueAsync_RecentSuccess_IsNotDue()
    {
        var coordinator = BuildCoordinator();
        var job = UniqueJob();
        var isDue = DueIfOlderThan(TimeSpan.FromHours(1));

        (await coordinator.RunIfDueAsync(job, isDue, _ => Task.CompletedTask, CancellationToken.None))
            .Should().Be(WorkerJobOutcome.Ran);

        // A restart re-creates the coordinator with no in-memory state — the journal alone
        // must prevent the immediate re-run that the old MinValue fields caused.
        var afterRestart = BuildCoordinator();
        var reran = false;
        var outcome = await afterRestart.RunIfDueAsync(job, isDue, _ => { reran = true; return Task.CompletedTask; }, CancellationToken.None);

        outcome.Should().Be(WorkerJobOutcome.NotDue);
        reran.Should().BeFalse();
    }

    [Fact]
    public async Task RunIfDueAsync_LockHeldElsewhere_Skips()
    {
        var coordinator = BuildCoordinator();
        var job = UniqueJob();

        // Simulate a second worker instance holding the job's advisory lock.
        await using var rival = await _fixture.OpenControlPlaneConnectionAsync();
        await using (var grab = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", rival))
        {
            grab.Parameters.AddWithValue("key", Fnv1a64($"orkyo:job:{job}"));
            await grab.ExecuteScalarAsync();
        }

        var ran = false;
        var outcome = await coordinator.RunIfDueAsync(job, AlwaysDue, _ => { ran = true; return Task.CompletedTask; }, CancellationToken.None);

        outcome.Should().Be(WorkerJobOutcome.HeldElsewhere);
        ran.Should().BeFalse();
        // rival's session lock releases when the connection disposes.
    }

    [Fact]
    public async Task RunIfDueAsync_FailingJob_IsAnOutcomeThatCountsAndKeepsTheLastSuccess()
    {
        var coordinator = BuildCoordinator();
        var job = UniqueJob();

        (await coordinator.RunIfDueAsync(job, AlwaysDue, _ => Task.CompletedTask, CancellationToken.None))
            .Should().Be(WorkerJobOutcome.Ran);
        var succeededAt = (await ReadJournalAsync(job)).LastSucceededAt;
        succeededAt.Should().NotBeNull();

        _time.Advance(TimeSpan.FromHours(2));
        (await coordinator.RunIfDueAsync(job, AlwaysDue, Throw, CancellationToken.None))
            .Should().Be(WorkerJobOutcome.Failed, "a failing job is journaled, not rethrown");

        var row = await ReadJournalAsync(job);
        row.Result.Should().Be("failed");
        row.ConsecutiveFailures.Should().Be(1);
        row.LastSucceededAt.Should().Be(succeededAt, "a failed attempt never erases the last success");
    }

    [Fact]
    public async Task RunIfDueAsync_AfterAFailure_WaitsForTheBackoff_AndASuccessResetsIt()
    {
        var coordinator = BuildCoordinator();
        var job = UniqueJob();
        var attempts = 0;
        Task Flaky(CancellationToken ct) { attempts++; return attempts <= 2 ? Throw(ct) : Task.CompletedTask; }
        Task<WorkerJobOutcome> Run() => coordinator.RunIfDueAsync(job, AlwaysDue, Flaky, CancellationToken.None);

        (await Run()).Should().Be(WorkerJobOutcome.Failed);                        // 1 failure → wait 2 min
        (await Run()).Should().Be(WorkerJobOutcome.NotDue, "inside the backoff");
        _time.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1));
        (await Run()).Should().Be(WorkerJobOutcome.Failed);                        // 2 failures → wait 4 min
        _time.Advance(TimeSpan.FromMinutes(3));
        (await Run()).Should().Be(WorkerJobOutcome.NotDue, "4 minutes have not elapsed");
        _time.Advance(TimeSpan.FromMinutes(2));
        (await Run()).Should().Be(WorkerJobOutcome.Ran);

        var row = await ReadJournalAsync(job);
        row.Result.Should().Be("succeeded");
        row.ConsecutiveFailures.Should().Be(0);
        row.LastSucceededAt.Should().NotBeNull();
    }

    [Fact]
    public void BackoffAfter_DoublesPerFailure_AndCapsAtAnHour()
    {
        var at = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        WorkerJobCoordinator.BackoffAfter(new WorkerJobCoordinator.JobState(DateTime.MinValue, at, 0)).Should().Be(DateTime.MinValue);
        WorkerJobCoordinator.BackoffAfter(new WorkerJobCoordinator.JobState(DateTime.MinValue, at, 1)).Should().Be(at.AddMinutes(2));
        WorkerJobCoordinator.BackoffAfter(new WorkerJobCoordinator.JobState(DateTime.MinValue, at, 3)).Should().Be(at.AddMinutes(8));
        WorkerJobCoordinator.BackoffAfter(new WorkerJobCoordinator.JobState(DateTime.MinValue, at, 20)).Should().Be(at.AddMinutes(60));
    }

    [Fact]
    public async Task RunIfDueAsync_DueAgainAfterInterval_Runs()
    {
        var coordinator = BuildCoordinator();
        var job = UniqueJob();

        (await coordinator.RunIfDueAsync(job, AlwaysDue, _ => Task.CompletedTask, CancellationToken.None))
            .Should().Be(WorkerJobOutcome.Ran);

        // Zero-interval due-ness: a completed run is due again as soon as the clock moves.
        _time.Advance(TimeSpan.FromMilliseconds(1));
        (await coordinator.RunIfDueAsync(job, DueIfOlderThan(TimeSpan.Zero), _ => Task.CompletedTask, CancellationToken.None))
            .Should().Be(WorkerJobOutcome.Ran);
    }

    private async Task<(DateTime? CompletedAt, string? Result, DateTime? LastSucceededAt, int ConsecutiveFailures)> ReadJournalAsync(string jobName)
    {
        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT completed_at, result, last_succeeded_at, consecutive_failures FROM worker_job_runs WHERE job_name = @job", conn);
        cmd.Parameters.AddWithValue("job", jobName);
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue($"journal row for {jobName} should exist");
        return (
            reader.IsDBNull(0) ? null : reader.GetDateTime(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetDateTime(2),
            reader.GetInt32(3));
    }

    /// <summary>Mirror of the coordinator's key hash, to contend on the same lock id.</summary>
    private static long Fnv1a64(string key)
    {
        const ulong fnvOffset = 14695981039346656037UL;
        const ulong fnvPrime = 1099511628211UL;
        var hash = fnvOffset;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(key))
        {
            hash ^= b;
            hash *= fnvPrime;
        }
        return unchecked((long)hash);
    }
}
