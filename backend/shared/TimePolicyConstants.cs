namespace Orkyo.Shared;

/// <summary>
/// Shared time-based constants used across API, Worker, and tests.
/// Keep values here to avoid drift between services.
/// </summary>
public static class TimePolicyConstants
{
    // Generic infra/service timings
    public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>Read-through TTL for the pure read-aggregations behind the dashboard.</summary>
    public static readonly TimeSpan ShortCacheTtl = TimeSpan.FromSeconds(60);
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
    public const int TenantDeleteGraceDays = 7;
    public const int TenantSuspendAfterDormantDays = 30;
    // Warn the owner/admins this many days before auto-suspension kicks in.
    public const int TenantSuspendWarnBeforeDays = 7;

    public const int UserPurgeAfterDormantDays = 90;

    // The tenant and user-purge SQL intervals are derived from the day counts above, so the
    // query and the number a notice quotes cannot drift apart.
    public static readonly string TenantSuspendAfterDormantSqlInterval = $"{TenantSuspendAfterDormantDays} days";
    // Idle threshold at which the pre-suspension warning fires = (suspend - warn-before) days.
    public static readonly string TenantSuspendWarnAfterDormantSqlInterval =
        $"{TenantSuspendAfterDormantDays - TenantSuspendWarnBeforeDays} days";
    public static readonly string TenantDeleteGraceSqlInterval = $"{TenantDeleteGraceDays} days";
    public const string UserInactiveWarningSqlInterval = "12 months";
    public const string UserWarningReminderSqlInterval = "14 days";
    public static readonly string UserPurgeAfterDormantSqlInterval = $"{UserPurgeAfterDormantDays} days";
    public const string UserConfirmTokenValiditySqlInterval = "30 days";
}
