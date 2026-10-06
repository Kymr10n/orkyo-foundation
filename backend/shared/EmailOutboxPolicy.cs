namespace Orkyo.Shared;

/// <summary>
/// Retry and retention rules for the mail outbox. One place, so the API's immediate
/// attempt and the worker's drain agree on when a mail is given up on and how long its
/// record stays.
/// </summary>
public static class EmailOutboxPolicy
{
    /// <summary>Delivery attempts before a mail is marked dead. Ten attempts on the schedule
    /// below span about a day and a half, long enough for a mail relay outage to pass.</summary>
    public const int MaxAttempts = 10;

    /// <summary>How many due rows one drain pass claims at a time.</summary>
    public const int DrainBatchSize = 50;

    /// <summary>
    /// How long a claim keeps a row away from other senders. The API's immediate attempt and
    /// the worker both claim before they send, so a row is never delivered twice; a claimer
    /// that dies mid-send releases the row when the lease runs out.
    /// </summary>
    public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(2);

    /// <summary>Sent rows are pruned after this. Bodies are already cleared on delivery; the
    /// row itself stays a day for support questions ("did the invitation go out?").</summary>
    public static readonly TimeSpan SentRetention = TimeSpan.FromDays(1);

    /// <summary>Dead rows keep their body for inspection and are pruned after this.</summary>
    public static readonly TimeSpan DeadRetention = TimeSpan.FromDays(30);

    /// <summary>
    /// Delay before the next attempt, by the number of attempts already made (1-based):
    /// 1, 5, 15 minutes, then 1 hour, then 4 hours for every further attempt.
    /// </summary>
    public static TimeSpan RetryDelay(int attemptsMade) => attemptsMade switch
    {
        <= 1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(5),
        3 => TimeSpan.FromMinutes(15),
        4 => TimeSpan.FromHours(1),
        _ => TimeSpan.FromHours(4),
    };
}
