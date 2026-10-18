using Api.Repositories;
using Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Orkyo.Foundation.Tests.Services;

/// <summary>
/// The journal against the real worker_job_runs table. What matters is what the October 2026
/// incident lacked: a failure is an outcome, not an exception; it never erases the last
/// success; it counts; and the next attempt waits for a backoff that a success resets.
/// </summary>
[Collection("Database collection")]
public class WorkerJobCoordinatorTests
{
    private readonly IDbConnectionFactory _factory;
    private readonly string _cp;
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);

    public WorkerJobCoordinatorTests(DatabaseFixture fixture)
    {
        var scope = fixture.Factory.Services.CreateScope();
        _factory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        _cp = fixture.ControlPlaneConnectionString;
    }

    private WorkerJobCoordinator Coordinator() =>
        new(_factory, NullLogger<WorkerJobCoordinator>.Instance, _time);

    private static string JobName() => $"test-job-{Guid.NewGuid():N}";

    private static Task<WorkerJobOutcome> Run(WorkerJobCoordinator c, string job, Func<CancellationToken, Task> body) =>
        c.RunIfDueAsync(job, (_, _) => true, body, CancellationToken.None);

    private static Task Throw(CancellationToken _) => throw new InvalidOperationException("boom");

    private async Task<(DateTime? LastSucceeded, int Failures, string? Result)> ReadAsync(string job)
    {
        await using var conn = new NpgsqlConnection(_cp);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT last_succeeded_at, consecutive_failures, result FROM worker_job_runs WHERE job_name = @j", conn);
        cmd.Parameters.AddWithValue("j", job);
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return (reader.IsDBNull(0) ? null : reader.GetDateTime(0), reader.GetInt32(1), reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    [Fact]
    public async Task AFailure_IsAnOutcome_ThatCountsAndKeepsTheLastSuccess()
    {
        var job = JobName();
        var c = Coordinator();
        (await Run(c, job, _ => Task.CompletedTask)).Should().Be(WorkerJobOutcome.Ran);
        var succeededAt = (await ReadAsync(job)).LastSucceeded;
        succeededAt.Should().NotBeNull();

        _time.Advance(TimeSpan.FromHours(2));
        (await Run(c, job, Throw)).Should().Be(WorkerJobOutcome.Failed);

        var row = await ReadAsync(job);
        row.Result.Should().Be("failed");
        row.Failures.Should().Be(1);
        row.LastSucceeded.Should().Be(succeededAt, "a failed attempt never erases the last success");
    }

    [Fact]
    public async Task AfterAFailure_TheNextAttemptWaitsForTheBackoff_AndASuccessResetsIt()
    {
        var job = JobName();
        var c = Coordinator();
        var attempts = 0;
        Task Flaky(CancellationToken ct) { attempts++; return attempts <= 2 ? Throw(ct) : Task.CompletedTask; }

        (await Run(c, job, Flaky)).Should().Be(WorkerJobOutcome.Failed);           // 1 failure → wait 2 min
        (await Run(c, job, Flaky)).Should().Be(WorkerJobOutcome.NotDue, "inside the backoff");
        _time.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1));
        (await Run(c, job, Flaky)).Should().Be(WorkerJobOutcome.Failed);           // 2 failures → wait 4 min
        _time.Advance(TimeSpan.FromMinutes(3));
        (await Run(c, job, Flaky)).Should().Be(WorkerJobOutcome.NotDue, "4 minutes have not elapsed");
        _time.Advance(TimeSpan.FromMinutes(2));
        (await Run(c, job, Flaky)).Should().Be(WorkerJobOutcome.Ran);

        var row = await ReadAsync(job);
        row.Failures.Should().Be(0);
        row.Result.Should().Be("succeeded");
        row.LastSucceeded.Should().NotBeNull();
    }

    [Fact]
    public void Backoff_DoublesPerFailure_AndCapsAtAnHour()
    {
        var at = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        WorkerJobCoordinator.BackoffAfter(new WorkerJobCoordinator.JobState(DateTime.MinValue, at, 0)).Should().Be(DateTime.MinValue);
        WorkerJobCoordinator.BackoffAfter(new WorkerJobCoordinator.JobState(DateTime.MinValue, at, 1)).Should().Be(at.AddMinutes(2));
        WorkerJobCoordinator.BackoffAfter(new WorkerJobCoordinator.JobState(DateTime.MinValue, at, 3)).Should().Be(at.AddMinutes(8));
        WorkerJobCoordinator.BackoffAfter(new WorkerJobCoordinator.JobState(DateTime.MinValue, at, 20)).Should().Be(at.AddMinutes(60));
    }

    [Fact]
    public async Task AJobThatNeverSucceeded_IsDueImmediately()
    {
        var c = Coordinator();
        (await Run(c, JobName(), _ => Task.CompletedTask)).Should().Be(WorkerJobOutcome.Ran);
    }
}
