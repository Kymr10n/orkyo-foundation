namespace Orkyo.Shared;

/// <summary>
/// Shared time-based constants used across API, Worker, and tests.
/// Keep values here to avoid drift between services.
/// </summary>
public static class TimePolicyConstants
{
    // Generic infra/service timings
    public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan BreakGlassSessionDefaultDuration = TimeSpan.FromHours(1);

    /// <summary>Absolute cap from the session's CreatedAt. Renewals cannot extend beyond this — preserves audit bound.</summary>
    public static readonly TimeSpan BreakGlassSessionAbsoluteCap = TimeSpan.FromHours(8);
    public static readonly TimeSpan BffPkceStateTtl = TimeSpan.FromMinutes(10);

    // Worker loop timings
    public static readonly TimeSpan WorkerTenantLifecycleInterval = TimeSpan.FromHours(1);
    public static readonly TimeSpan WorkerUserLifecycleInterval = TimeSpan.FromHours(24);
    public static readonly TimeSpan WorkerLoopDelay = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan WorkerErrorRetryDelay = TimeSpan.FromSeconds(30);
}

/// <summary>
/// User and tenant lifecycle policy constants.
/// SQL interval literals are centralized so queries and user-facing text stay aligned.
/// </summary>
public static class LifecyclePolicyConstants
{
    public const int TenantSuspendAfterDormantDays = 30;
    public const int TenantDeleteGraceDays = 7;

    public const int UserInactiveWarningAfterMonths = 12;
    public const int UserWarningReminderDays = 14;
    public const int UserPurgeAfterDormantDays = 90;

    public const string TenantSuspendAfterDormantSqlInterval = "30 days";
    public const string TenantDeleteGraceSqlInterval = "7 days";
    public const string UserInactiveWarningSqlInterval = "12 months";
    public const string UserWarningReminderSqlInterval = "14 days";
    public const string UserPurgeAfterDormantSqlInterval = "90 days";
}
