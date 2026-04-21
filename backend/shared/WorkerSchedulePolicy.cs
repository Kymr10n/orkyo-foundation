namespace Orkyo.Shared;

/// <summary>
/// Centralized cadence logic for worker loops.
/// Keeps timing decisions deterministic and testable.
/// </summary>
public static class WorkerSchedulePolicy
{
    public static bool ShouldRunTenantLifecycle(DateTime nowUtc, DateTime lastRunUtc)
        => nowUtc - lastRunUtc > TimePolicyConstants.WorkerTenantLifecycleInterval;

    public static bool ShouldRunUserLifecycle(DateTime nowUtc, DateTime lastRunUtc)
        => nowUtc - lastRunUtc > TimePolicyConstants.WorkerUserLifecycleInterval;

    public static TimeSpan GetLoopDelay(TimeSpan jitter)
        => TimePolicyConstants.WorkerLoopDelay + jitter;

    public static TimeSpan GetErrorRetryDelay()
        => TimePolicyConstants.WorkerErrorRetryDelay;
}
