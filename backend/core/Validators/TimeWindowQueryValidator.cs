using Api.Models;
using Api.Services.Insights;
using FluentValidation;

namespace Api.Validators;

/// <summary>
/// Bounds every member-readable window (utilization series, the site's scheduled bars, the
/// assignments of a type, a resource's candidate requests) with the Insights caps, so no single
/// call can ask for day buckets over nine thousand years. The cap follows the bucket size: the
/// Insights cap for week and coarser, the week cap (the one the per-day bottleneck ranking uses)
/// for a day, and tighter ones for the sub-day buckets the calendar grid asks for. A window with
/// no buckets is a row list, capped like a month series.
/// </summary>
public class TimeWindowQueryValidator : AbstractValidator<TimeWindowQuery>
{
    /// <summary>The bucket sizes <c>UtilizationService</c> understands, with their cap in days.</summary>
    private static readonly IReadOnlyDictionary<string, int> MaxDaysByGranularity =
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

    public TimeWindowQueryValidator()
    {
        RuleFor(x => x).Custom((q, ctx) =>
        {
            if (q.From is null || q.To is null)
            {
                ctx.AddFailure("from", "'from' and 'to' are required.");
                return;
            }
            if (q.From >= q.To)
            {
                ctx.AddFailure("from", "'from' must be before 'to'.");
                return;
            }

            int maxDays;
            if (q.Granularity is null)
                maxDays = InsightsBuckets.MaxRangeDays("month");
            else if (!MaxDaysByGranularity.TryGetValue(q.Granularity, out maxDays))
            {
                ctx.AddFailure("granularity",
                    $"Invalid granularity '{q.Granularity}'. Expected {string.Join('|', MaxDaysByGranularity.Keys)}.");
                return;
            }

            if ((q.To.Value - q.From.Value).TotalDays > maxDays)
                ctx.AddFailure("to", q.Granularity is null
                    ? $"Date range too large: at most {maxDays} days."
                    : $"Date range too large for granularity '{q.Granularity}': at most {maxDays} days.");
        });
    }
}
