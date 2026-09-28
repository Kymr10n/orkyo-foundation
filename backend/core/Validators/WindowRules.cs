using FluentValidation;

namespace Api.Validators;

/// <summary>
/// The shape rule every <c>[from, to)</c> window shares, for the Insights and time-window
/// validators alike: both bounds present, and <c>from</c> before <c>to</c>. The cap on the
/// span is the caller's, since it follows the bucket size.
/// </summary>
internal static class WindowRules
{
    /// <summary>
    /// True when the window is well-formed, with its bounds; otherwise the failure is added to
    /// <paramref name="ctx"/> and the caller stops, since the cap check needs both bounds.
    /// </summary>
    internal static bool HasOrderedBounds<T>(
        DateTime? from, DateTime? to, ValidationContext<T> ctx, out DateTime fromValue, out DateTime toValue)
    {
        fromValue = from ?? default;
        toValue = to ?? default;
        if (from is null || to is null)
        {
            ctx.AddFailure("from", "'from' and 'to' are required.");
            return false;
        }
        if (from >= to)
        {
            ctx.AddFailure("from", "'from' must be before 'to'.");
            return false;
        }
        return true;
    }

    /// <summary>True when the window is wider than <paramref name="maxDays"/>.</summary>
    internal static bool Exceeds(DateTime from, DateTime to, int maxDays) => (to - from).TotalDays > maxDays;
}
