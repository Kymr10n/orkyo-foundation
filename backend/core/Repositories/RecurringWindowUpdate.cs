using Api.Models;

namespace Api.Repositories;

/// <summary>
/// The partial update of the columns an availability event and a resource absence share:
/// <c>title</c>, <c>start_ts</c>, <c>end_ts</c>, <c>is_recurring</c>, <c>recurrence_rule</c>
/// and <c>enabled</c>. Only the fields a request carries are written. The builder owns every
/// parameter it reads, so <see cref="UpdateBuilder.Apply"/> is the only binding step.
/// </summary>
internal static class RecurringWindowUpdate
{
    public static UpdateBuilder Build(IRecurringWindowUpdate request)
    {
        var update = new UpdateBuilder()
            .SetIfNotNull("title", request.Title)
            .SetIfNotNull("start_ts", request.StartTs)
            .SetIfNotNull("end_ts", request.EndTs)
            .SetIfNotNull("is_recurring", request.IsRecurring)
            .SetIfNotNull("enabled", request.Enabled);
        // A rule belongs only to a recurring window: switching recurrence off clears it, and a
        // new rule applies only when the window is (or stays) recurring. The right-hand side
        // reads the row's values from before this UPDATE, so no prior read is needed.
        if (request.IsRecurring.HasValue || request.RecurrenceRule is not null)
            update.SetExpression(
                "recurrence_rule = CASE WHEN COALESCE(@window_is_recurring::boolean, is_recurring) "
                + "THEN COALESCE(@window_recurrence_rule::text, recurrence_rule) END",
                ("window_is_recurring", request.IsRecurring),
                ("window_recurrence_rule", request.RecurrenceRule));
        return update;
    }
}
