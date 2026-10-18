using Api.Helpers;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Api.Services;

/// <summary>Outcome of a <see cref="IWorkerJobCoordinator.RunIfDueAsync"/> attempt.</summary>
public enum WorkerJobOutcome
{
    /// <summary>The job ran (successfully — failures throw after being journaled).</summary>
    Ran,

    /// <summary>The journal shows a recent-enough successful run; nothing to do.</summary>
    NotDue,

    /// <summary>Another worker instance holds the job's advisory lock right now.</summary>
    HeldElsewhere,

    /// <summary>The job threw. Journaled (consecutive_failures + 1) and logged; the next
    /// attempt waits for the backoff in <see cref="WorkerJobCoordinator.BackoffAfter"/>.</summary>
    Failed,
}

/// <summary>
/// Cross-instance, restart-safe scheduling guard for worker jobs. The workers previously
/// tracked "last run" in instance-local fields initialised to <see cref="DateTime.MinValue"/>,
/// so every restart immediately re-ran the daily GDPR pass, and two worker instances would
/// double-run every job — including the destructive tenant cleanup. This coordinator replaces
/// that state with a control-plane journal row per job, read and advanced under a per-job
/// Postgres advisory lock.
/// </summary>
public interface IWorkerJobCoordinator
{
    /// <summary>
    /// Runs <paramref name="job"/> iff <paramref name="isDue"/>(nowUtc, lastCompletedUtc)
    /// says it is due, under a per-job advisory lock. Due-ness is evaluated INSIDE the lock
    /// (double-checked) so a second instance that raced past an earlier check cannot re-run
    /// the job right after the winner finishes. Only a successful run advances
    /// <c>last_succeeded_at</c>. A failing job is journaled (<c>consecutive_failures</c> + 1),
    /// logged, and reported as <see cref="WorkerJobOutcome.Failed"/> — never thrown — so one
    /// broken job can neither stop the jobs after it nor hide its own last success. Its next
    /// attempt waits for an exponential backoff, which also stops a permanently broken daily
    /// job from hammering its dependencies every loop.
    /// </summary>
    /// <param name="jobName">Stable job identifier — one journal row and one lock per name.</param>
    /// <param name="isDue">(nowUtc, lastCompletedUtc) → should the job run. Last completed is
    /// <see cref="DateTime.MinValue"/> when the job has never succeeded.</param>
    /// <param name="job">The job body.</param>
    Task<WorkerJobOutcome> RunIfDueAsync(
        string jobName,
        Func<DateTime, DateTime, bool> isDue,
        Func<CancellationToken, Task> job,
        CancellationToken cancellationToken);
}

/// <summary>Well-known job names — shared by both editions' workers and the ops heartbeat.</summary>
public static class WorkerJobNames
{
    public const string TenantLifecycle = "tenant-lifecycle";
    public const string UserLifecycle = "user-lifecycle";
    public const string AnnouncementBroadcast = "announcement-broadcast";
    public const string EmailOutbox = "email-outbox";
}

public sealed class WorkerJobCoordinator : IWorkerJobCoordinator
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<WorkerJobCoordinator> _logger;
    private readonly TimeProvider _time;

    public WorkerJobCoordinator(
        IDbConnectionFactory connectionFactory,
        ILogger<WorkerJobCoordinator> logger,
        TimeProvider time)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
        _time = time;
    }

    public async Task<WorkerJobOutcome> RunIfDueAsync(
        string jobName,
        Func<DateTime, DateTime, bool> isDue,
        Func<CancellationToken, Task> job,
        CancellationToken cancellationToken)
    {
        // The lock is session-scoped, so it must live on a dedicated connection that stays
        // open for the whole run; the same connection carries the journal reads/writes.
        await using var connection = _connectionFactory.CreateControlPlaneConnection();
        await connection.OpenAsync(cancellationToken);

        var lockId = StableHash64($"orkyo:job:{jobName}");
        if (!await TryAcquireLockAsync(connection, lockId, cancellationToken))
        {
            _logger.LogDebug("Job {JobName}: advisory lock held by another instance, skipping", jobName);
            return WorkerJobOutcome.HeldElsewhere;
        }

        try
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var state = await ReadStateAsync(connection, jobName, cancellationToken);
            if (!isDue(now, state.LastSucceeded) || now < BackoffAfter(state))
            {
                return WorkerJobOutcome.NotDue;
            }

            await RecordStartedAsync(connection, jobName, cancellationToken);
            try
            {
                await job(cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                await RecordResultAsync(connection, jobName, succeeded: false, CancellationToken.None);
                _logger.LogError(ex, "Job {JobName} failed ({ConsecutiveFailures} in a row)",
                    jobName, state.ConsecutiveFailures + 1);
                return WorkerJobOutcome.Failed;
            }

            await RecordResultAsync(connection, jobName, succeeded: true, cancellationToken);
            return WorkerJobOutcome.Ran;
        }
        finally
        {
            await ReleaseLockAsync(connection, lockId);
        }
    }

    private static async Task<bool> TryAcquireLockAsync(NpgsqlConnection connection, long lockId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
        cmd.Parameters.AddWithValue("key", lockId);
        return (bool)(await cmd.ExecuteScalarAsync(ct) ?? false);
    }

    private static async Task ReleaseLockAsync(NpgsqlConnection connection, long lockId)
    {
        await using var cmd = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
        cmd.Parameters.AddWithValue("key", lockId);
        await cmd.ExecuteScalarAsync(CancellationToken.None);
    }

    /// <summary>The journal row as the scheduler reads it. Never-succeeded reads as
    /// <see cref="DateTime.MinValue"/>, matching the <c>isDue</c> contract.</summary>
    internal sealed record JobState(DateTime LastSucceeded, DateTime? LastCompleted, int ConsecutiveFailures);

    /// <summary>
    /// Earliest time the next attempt is allowed after a failure: 2^n minutes, capped at an
    /// hour. Zero failures means no wait. Exposed for the tests and the diagnostics page.
    /// </summary>
    internal static DateTime BackoffAfter(JobState state)
    {
        if (state.ConsecutiveFailures == 0 || state.LastCompleted is null) return DateTime.MinValue;
        var minutes = Math.Min(Math.Pow(2, state.ConsecutiveFailures), 60);
        return state.LastCompleted.Value + TimeSpan.FromMinutes(minutes);
    }

    private static async Task<JobState> ReadStateAsync(NpgsqlConnection connection, string jobName, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT last_succeeded_at, completed_at, consecutive_failures FROM worker_job_runs WHERE job_name = @job", connection);
        cmd.Parameters.AddWithValue("job", jobName);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return new JobState(DateTime.MinValue, null, 0);
        return new JobState(
            reader.GetNullableDateTime("last_succeeded_at")?.ToUniversalTime() ?? DateTime.MinValue,
            reader.GetNullableDateTime("completed_at")?.ToUniversalTime(),
            reader.GetInt32("consecutive_failures"));
    }

    /// <summary>Marks an attempt in flight. last_succeeded_at and consecutive_failures are
    /// untouched: a start is not an outcome. The journal is stamped from the injected clock,
    /// not <c>NOW()</c>, so the due check and the backoff read the same time they wrote.</summary>
    private async Task RecordStartedAsync(NpgsqlConnection connection, string jobName, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO worker_job_runs (job_name, started_at, completed_at, result)
            VALUES (@job, @now, NULL, NULL)
            ON CONFLICT (job_name) DO UPDATE SET started_at = EXCLUDED.started_at, completed_at = NULL, result = NULL
            """, connection);
        cmd.Parameters.AddWithValue("job", jobName);
        cmd.Parameters.AddWithValue("now", _time.GetUtcNow().UtcDateTime);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task RecordResultAsync(NpgsqlConnection connection, string jobName, bool succeeded, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(succeeded
            ? "UPDATE worker_job_runs SET completed_at = @now, result = 'succeeded', last_succeeded_at = @now, consecutive_failures = 0 WHERE job_name = @job"
            : "UPDATE worker_job_runs SET completed_at = @now, result = 'failed', consecutive_failures = consecutive_failures + 1 WHERE job_name = @job",
            connection);
        cmd.Parameters.AddWithValue("job", jobName);
        cmd.Parameters.AddWithValue("now", _time.GetUtcNow().UtcDateTime);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// FNV-1a 64-bit — same scheme as the migrator's AdvisoryLock so operators reason about
    /// one hash. Reimplemented here (≈10 lines) because core must not depend on the migrator
    /// package; the "orkyo:job:" namespace keeps the key spaces disjoint regardless.
    /// </summary>
    private static long StableHash64(string key)
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
