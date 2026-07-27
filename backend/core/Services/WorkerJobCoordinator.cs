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
    /// the job right after the winner finishes. Only a successful run advances the journal;
    /// a failing job is recorded as <c>failed</c> and its exception rethrown, so the worker
    /// loop's existing retry/backoff semantics are preserved.
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
}

public sealed class WorkerJobCoordinator : IWorkerJobCoordinator
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<WorkerJobCoordinator> _logger;

    public WorkerJobCoordinator(IDbConnectionFactory connectionFactory, ILogger<WorkerJobCoordinator> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
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
            var lastCompleted = await ReadLastCompletedAsync(connection, jobName, cancellationToken);
            if (!isDue(DateTime.UtcNow, lastCompleted))
            {
                return WorkerJobOutcome.NotDue;
            }

            await RecordStartedAsync(connection, jobName, cancellationToken);
            try
            {
                await job(cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Journal the failure (for the ops heartbeat) without advancing completed_at,
                // so the job stays due and the loop's retry semantics apply unchanged.
                await RecordResultAsync(connection, jobName, succeeded: false, CancellationToken.None);
                throw;
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

    private static async Task<DateTime> ReadLastCompletedAsync(NpgsqlConnection connection, string jobName, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT completed_at FROM worker_job_runs WHERE job_name = @job AND result = 'succeeded'", connection);
        cmd.Parameters.AddWithValue("job", jobName);
        var value = await cmd.ExecuteScalarAsync(ct);
        return value is DateTime completed ? completed.ToUniversalTime() : DateTime.MinValue;
    }

    private static async Task RecordStartedAsync(NpgsqlConnection connection, string jobName, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO worker_job_runs (job_name, started_at, completed_at, result)
            VALUES (@job, NOW(), NULL, NULL)
            ON CONFLICT (job_name) DO UPDATE SET started_at = NOW(), completed_at = NULL, result = NULL
            """, connection);
        cmd.Parameters.AddWithValue("job", jobName);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task RecordResultAsync(NpgsqlConnection connection, string jobName, bool succeeded, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "UPDATE worker_job_runs SET completed_at = NOW(), result = @result WHERE job_name = @job", connection);
        cmd.Parameters.AddWithValue("job", jobName);
        cmd.Parameters.AddWithValue("result", succeeded ? "succeeded" : "failed");
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
