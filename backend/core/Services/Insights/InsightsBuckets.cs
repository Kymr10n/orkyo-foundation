namespace Api.Services.Insights;

/// <summary>
/// The single time-bucketing strategy for every Insights series, so request, conflict, and
/// utilization charts always share identical, calendar-aligned bucket boundaries (doc 04: consistent
/// buckets across charts). Buckets are half-open [Start, End): Start inclusive, End exclusive.
///
/// week = ISO week (Monday start); month/quarter/year = calendar boundaries — matching the
/// granularity arms in <c>UtilizationService.BuildBucketShells</c> so utilization buckets line up.
/// </summary>
public static class InsightsBuckets
{
    public static readonly IReadOnlySet<string> ValidBuckets =
        new HashSet<string>(StringComparer.Ordinal) { "week", "month", "quarter", "year" };

    public static readonly IReadOnlySet<string> ValidResourceTypes =
        new HashSet<string>(StringComparer.Ordinal) { "space", "person", "tool" };

    /// <summary>
    /// Maximum span per bucket (doc 03) — prevents unbounded analytics scans. Expressed in days,
    /// generously rounded up so leap years never trip a legitimate request.
    /// </summary>
    public static int MaxRangeDays(string bucket) => bucket switch
    {
        "week" => 2 * 366,
        "month" => 5 * 366,
        "quarter" => 10 * 366,
        "year" => 20 * 366,
        _ => throw new ArgumentException($"Unknown bucket '{bucket}'."),
    };

    /// <summary>Truncate a timestamp down to the start of its bucket, in UTC.</summary>
    public static DateTime AlignStart(DateTime ts, string bucket)
    {
        var utc = DateTime.SpecifyKind(ts.ToUniversalTime(), DateTimeKind.Utc);
        var date = utc.Date;
        return bucket switch
        {
            // ISO week: Monday=0 … Sunday=6
            "week" => date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
            "month" => new DateTime(date.Year, date.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            "quarter" => new DateTime(date.Year, ((date.Month - 1) / 3) * 3 + 1, 1, 0, 0, 0, DateTimeKind.Utc),
            "year" => new DateTime(date.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => throw new ArgumentException($"Unknown bucket '{bucket}'."),
        };
    }

    public static DateTime AddStep(DateTime alignedStart, string bucket) => bucket switch
    {
        "week" => alignedStart.AddDays(7),
        "month" => alignedStart.AddMonths(1),
        "quarter" => alignedStart.AddMonths(3),
        "year" => alignedStart.AddYears(1),
        _ => throw new ArgumentException($"Unknown bucket '{bucket}'."),
    };

    /// <summary>
    /// Full, calendar-aligned buckets covering [from, to]: starts at the bucket containing
    /// <paramref name="from"/> and ends at the bucket boundary at or after <paramref name="to"/>, so the
    /// period containing <paramref name="to"/> is always represented (empty buckets included).
    /// </summary>
    public static List<(DateTime Start, DateTime End)> Generate(DateTime from, DateTime to, string bucket)
    {
        var start = AlignStart(from, bucket);
        var end = AlignStart(to, bucket);
        if (end < to) end = AddStep(end, bucket);

        var buckets = new List<(DateTime, DateTime)>();
        var cursor = start;
        while (cursor < end)
        {
            var next = AddStep(cursor, bucket);
            buckets.Add((cursor, next));
            cursor = next;
        }
        return buckets;
    }

    /// <summary>Index of the bucket containing <paramref name="ts"/>, or -1 if outside the range.</summary>
    public static int IndexOf(IReadOnlyList<(DateTime Start, DateTime End)> buckets, DateTime ts)
    {
        for (var i = 0; i < buckets.Count; i++)
            if (ts >= buckets[i].Start && ts < buckets[i].End) return i;
        return -1;
    }
}
