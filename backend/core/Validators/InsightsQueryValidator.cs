using Api.Models.Insights;
using Api.Services.Insights;
using FluentValidation;

namespace Api.Validators;

/// <summary>
/// The shape rules and range caps of every Insights read, for the HTTP endpoints and the MCP
/// <c>analyze_capacity</c> tool alike. The caps bound the analytics scan: the finest granularity
/// on the widest window is what <see cref="InsightsBuckets.MaxRangeDays"/> prevents.
/// Tenant lookups (does the site or resource type exist) are not shape rules and stay with the caller.
/// </summary>
public class InsightsQueryValidator : AbstractValidator<InsightsQuery>
{
    public InsightsQueryValidator()
    {
        RuleFor(x => x).Custom((q, ctx) =>
        {
            if (!WindowRules.HasOrderedBounds(q.From, q.To, ctx, out var from, out var to))
                return;

            if (q.View == InsightsView.Trend)
            {
                if (string.IsNullOrWhiteSpace(q.Bucket))
                {
                    ctx.AddFailure("bucket", "'bucket' is required (week|month|quarter|year).");
                    return;
                }
                if (!InsightsBuckets.ValidBuckets.Contains(q.Bucket))
                {
                    ctx.AddFailure("bucket", $"Invalid bucket '{q.Bucket}'. Expected week|month|quarter|year.");
                    return;
                }
            }

            if (WindowRules.Exceeds(from, to, MaxRangeDays(q)))
                ctx.AddFailure("to", q.View == InsightsView.Trend
                    ? $"Date range too large for bucket '{q.Bucket}'."
                    : "Date range too large.");
        });
    }

    /// <summary>
    /// The overview aggregates by month, so it takes the month cap (five years). Bottlenecks
    /// measure per day, so they take the week cap (two years): the tightest one, and still wide
    /// enough for the dashboard's default "last 6 / next 12 months" filter.
    /// </summary>
    private static int MaxRangeDays(InsightsQuery q) => q.View switch
    {
        InsightsView.Overview => InsightsBuckets.MaxRangeDays("month"),
        InsightsView.Bottlenecks => InsightsBuckets.MaxRangeDays("week"),
        _ => InsightsBuckets.MaxRangeDays(q.Bucket!),
    };
}
