using Api.Services.Insights;

namespace Api.Services;

/// <summary>
/// The one table of the bucket sizes a utilization series can be asked in: each one's step
/// (<see cref="AddStep"/>, which <c>UtilizationService</c> walks) and its cap in days (which
/// <c>TimeWindowQueryValidator</c> enforces), so the validator and the service cannot disagree
/// on what a granularity is. The cap follows the bucket size: the Insights cap for week and
/// coarser, the week cap (the one the per-day bottleneck ranking uses) for a day, and tighter
/// ones for the sub-day buckets the calendar grid asks for.
/// </summary>
public static class UtilizationGranularity
{
    /// <summary>Every granularity with its cap in days, in the order the validator's message lists them.</summary>
    public static readonly IReadOnlyDictionary<string, int> MaxDays =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["minute"] = 31,
            ["hour"] = 92,
            ["day"] = InsightsBuckets.MaxRangeDays("week"),
            ["week"] = InsightsBuckets.MaxRangeDays("week"),
            ["month"] = InsightsBuckets.MaxRangeDays("month"),
            ["quarter"] = InsightsBuckets.MaxRangeDays("quarter"),
            ["year"] = InsightsBuckets.MaxRangeDays("year"),
        };

    /// <summary>
    /// The start of the bucket after the one starting at <paramref name="start"/>. A "minute"
    /// bucket is the calendar grid's quarter hour. An unknown granularity is a programming
    /// error: the validator refuses it before any service sees it.
    /// </summary>
    public static DateTime AddStep(DateTime start, string granularity) => granularity.ToLowerInvariant() switch
    {
        "minute" => start.AddMinutes(15),
        "hour" => start.AddHours(1),
        "day" => start.AddDays(1),
        "week" => start.AddDays(7),
        "month" => start.AddMonths(1),
        "quarter" => start.AddMonths(3),
        "year" => start.AddYears(1),
        _ => throw new ArgumentOutOfRangeException(nameof(granularity), granularity, "Unknown granularity."),
    };
}
