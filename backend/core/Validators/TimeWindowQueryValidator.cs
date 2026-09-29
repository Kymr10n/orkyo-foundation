using Api.Models;
using Api.Services;
using Api.Services.Insights;
using FluentValidation;

namespace Api.Validators;

/// <summary>
/// Bounds every member-readable window (utilization series, the site's scheduled bars, the
/// assignments of a type, a resource's candidate requests) with the Insights caps, so no single
/// call can ask for day buckets over nine thousand years. The cap per bucket size is
/// <see cref="UtilizationGranularity.MaxDays"/>. A window with no buckets is a row list, capped
/// like a month series.
/// </summary>
public class TimeWindowQueryValidator : AbstractValidator<TimeWindowQuery>
{
    public TimeWindowQueryValidator()
    {
        RuleFor(x => x).Custom((q, ctx) =>
        {
            if (!WindowRules.HasOrderedBounds(q.From, q.To, ctx, out var from, out var to))
                return;

            int maxDays;
            if (q.Granularity is null)
                maxDays = InsightsBuckets.MaxRangeDays("month");
            else if (!UtilizationGranularity.MaxDays.TryGetValue(q.Granularity, out maxDays))
            {
                ctx.AddFailure("granularity",
                    $"Invalid granularity '{q.Granularity}'. Expected {string.Join('|', UtilizationGranularity.MaxDays.Keys)}.");
                return;
            }

            if (WindowRules.Exceeds(from, to, maxDays))
                ctx.AddFailure("to", q.Granularity is null
                    ? $"Date range too large: at most {maxDays} days."
                    : $"Date range too large for granularity '{q.Granularity}': at most {maxDays} days.");
        });
    }
}
