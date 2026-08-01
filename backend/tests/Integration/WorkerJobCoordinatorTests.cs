using Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Orkyo.Foundation.Tests.Integration;

/// <summary>
/// DB-backed tests for <see cref="WorkerJobCoordinator"/> — the restart-safe,
/// cross-instance scheduling guard for worker jobs (journal row + per-job Postgres
/// advisory lock). Each test uses a unique job name so runs don't interfere.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class WorkerJobCoordinatorTests
{
    private readonly PostgresFixture _fixture;

    public WorkerJobCoordinatorTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private WorkerJobCoordinator BuildCoordinator() =>
        new(_fixture.CreateConnectionFactory(), NullLogger<WorkerJobCoordinator>.Instance);

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

        var (completedAt, result) = await ReadJournalAsync(job);
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
    public async Task RunIfDueAsync_FailingJob_JournalsFailureAndStaysDue()
    {
        var coordinator = BuildCoordinator();
        var job = UniqueJob();
        var isDue = DueIfOlderThan(TimeSpan.FromHours(1));

        var act = () => coordinator.RunIfDueAsync(
            job, isDue, _ => throw new InvalidOperationException("boom"), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();

        var (_, result) = await ReadJournalAsync(job);
        result.Should().Be("failed");

        // A failure must not advance the schedule: the next attempt still runs.
        var outcome = await coordinator.RunIfDueAsync(job, isDue, _ => Task.CompletedTask, CancellationToken.None);
        outcome.Should().Be(WorkerJobOutcome.Ran);
    }

    [Fact]
    public async Task RunIfDueAsync_DueAgainAfterInterval_Runs()
    {
        var coordinator = BuildCoordinator();
        var job = UniqueJob();

        (await coordinator.RunIfDueAsync(job, AlwaysDue, _ => Task.CompletedTask, CancellationToken.None))
            .Should().Be(WorkerJobOutcome.Ran);

        // Zero-interval due-ness: a completed run is immediately due again.
        (await coordinator.RunIfDueAsync(job, DueIfOlderThan(TimeSpan.Zero), _ => Task.CompletedTask, CancellationToken.None))
            .Should().Be(WorkerJobOutcome.Ran);
    }

    private async Task<(DateTime? CompletedAt, string? Result)> ReadJournalAsync(string jobName)
    {
        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT completed_at, result FROM worker_job_runs WHERE job_name = @job", conn);
        cmd.Parameters.AddWithValue("job", jobName);
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue($"journal row for {jobName} should exist");
        return (
            reader.IsDBNull(0) ? null : reader.GetDateTime(0),
            reader.IsDBNull(1) ? null : reader.GetString(1));
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
