using Api.Models;
using Npgsql;
using NpgsqlTypes;

namespace Api.Repositories;

/// <summary>
/// The partial update of the window columns an availability event and a resource absence share:
/// <c>start_ts</c>, <c>end_ts</c>, <c>is_recurring</c>, <c>recurrence_rule</c> and <c>enabled</c>.
/// Only the fields a request carries are written.
/// </summary>
internal static class RecurringWindowUpdate
{
    public static UpdateBuilder Build(IRecurringWindowUpdate request, bool? enabled)
    {
        var update = new UpdateBuilder()
            .SetIfNotNull("start_ts", request.StartTs)
            .SetIfNotNull("end_ts", request.EndTs)
            .SetIfNotNull("is_recurring", request.IsRecurring)
            .SetIfNotNull("enabled", enabled);
        // A rule belongs only to a recurring window: switching recurrence off clears it, and a
        // new rule applies only when the window is (or stays) recurring. The right-hand side
        // reads the row's values from before this UPDATE, so no prior read is needed.
        if (TouchesRule(request))
            update.SetExpression(
                "recurrence_rule = CASE WHEN COALESCE(@window_is_recurring, is_recurring) "
                + "THEN COALESCE(@window_recurrence_rule, recurrence_rule) END");
        return update;
    }

    /// <summary>Binds the parameters of the <c>recurrence_rule</c> expression, when it is set.</summary>
    public static void Bind(NpgsqlParameterCollection parameters, IRecurringWindowUpdate request)
    {
        if (!TouchesRule(request)) return;
        parameters.Add(new NpgsqlParameter("window_is_recurring", NpgsqlDbType.Boolean)
        {
            Value = (object?)request.IsRecurring ?? DBNull.Value,
        });
        parameters.Add(new NpgsqlParameter("window_recurrence_rule", NpgsqlDbType.Text)
        {
            Value = (object?)request.RecurrenceRule ?? DBNull.Value,
        });
    }

    private static bool TouchesRule(IRecurringWindowUpdate request)
        => request.IsRecurring.HasValue || request.RecurrenceRule is not null;
}
