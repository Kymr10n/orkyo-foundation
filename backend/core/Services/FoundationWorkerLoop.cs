using Orkyo.Shared;

namespace Api.Services;

/// <summary>
/// One scheduled job of a product's background worker: a stable name (one journal row and
/// one advisory lock per name — see <see cref="WorkerJobNames"/>), the due-ness rule, and
/// the body. Products declare their list through <c>AddFoundationWorkerLoop</c>.
/// </summary>
/// <param name="Name">The journal/lock key. Use a <see cref="WorkerJobNames"/> constant.</param>
/// <param name="IsDue">(nowUtc, lastCompletedUtc) → run now. <see cref="WorkerSchedulePolicy"/>
/// holds the shared rules; <c>(_, _) => true</c> runs every loop, single-flighted.</param>
/// <param name="Run">The job body.</param>
public sealed record WorkerJob(
    string Name,
    Func<DateTime, DateTime, bool> IsDue,
    Func<CancellationToken, Task> Run);

/// <summary>
/// The worker loop both editions used to carry as their own <c>BackgroundService</c>: run
/// every declared job through <see cref="IWorkerJobCoordinator"/> (journal + per-job lock, so
/// a restart resumes the cadence and a second instance skips instead of double-running),
/// touch the heartbeat, sleep with jitter. Failure handling is the coordinator's: a job that
/// throws is journaled, backed off and reported, and never stops the jobs after it. The
/// product keeps a few-line hosted service that calls <see cref="RunAsync"/>; this class has
/// no hosting dependency so it can live in core.
/// </summary>
public sealed class FoundationWorkerLoop
{
    private readonly IWorkerJobCoordinator _jobs;
    private readonly IReadOnlyList<WorkerJob> _jobList;
    private readonly ILogger<FoundationWorkerLoop> _logger;
    private readonly TimeProvider _time;

    /// <summary>The sleep between cycles comes from <paramref name="time"/>, so a test
    /// drives the cadence with a <c>FakeTimeProvider</c> instead of waiting.</summary>
    public FoundationWorkerLoop(
        IWorkerJobCoordinator jobs,
        IEnumerable<WorkerJob> jobList,
        ILogger<FoundationWorkerLoop> logger,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(jobList);
        ArgumentNullException.ThrowIfNull(time);
        _jobs = jobs;
        _jobList = jobList.ToList();
        _logger = logger;
        _time = time;
        if (_jobList.Count == 0)
            throw new ArgumentException("A worker loop needs at least one job.", nameof(jobList));
    }

    public IReadOnlyList<WorkerJob> Jobs => _jobList;

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker loop started with {Count} job(s): {Jobs}",
            _jobList.Count, string.Join(", ", _jobList.Select(j => j.Name)));

        // Cadence state lives in the worker_job_runs journal (via IWorkerJobCoordinator),
        // not in instance fields: a restart resumes the schedule instead of immediately
        // re-running the daily GDPR pass, and a second worker instance skips instead of
        // double-running (each job runs under a per-job Postgres advisory lock).
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var heldElsewhere = false;
                foreach (var job in _jobList)
                {
                    // The coordinator owns every outcome, including a thrown exception, so a
                    // broken job is one Failed outcome here — the loop never leaves this list
                    // early. The only exception that escapes is a journal connection failure,
                    // which is the infrastructure being down, not a job.
                    var outcome = await _jobs.RunIfDueAsync(job.Name, job.IsDue, job.Run, stoppingToken);
                    heldElsewhere |= outcome == WorkerJobOutcome.HeldElsewhere;
                }

                if (heldElsewhere)
                    _logger.LogDebug("A job is running on another worker instance this cycle");

                WorkerHeartbeat.Touch();
                var jitter = TimeSpan.FromSeconds(Random.Shared.Next(0, 15));
                await Task.Delay(WorkerSchedulePolicy.GetLoopDelay(jitter), _time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Worker loop cancelled");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Worker loop could not reach the job journal");
                await Task.Delay(WorkerSchedulePolicy.GetErrorRetryDelay(), _time, stoppingToken);
            }
        }

        _logger.LogInformation("Worker loop stopped");
    }
}
