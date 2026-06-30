using Api.Models;

namespace Api.Helpers;

/// <summary>
/// Computes a request's EFFECTIVE workflow status from its stored status and its schedule.
///
/// The active lifecycle (<c>new → in_progress → done</c>) is derived from the scheduled window
/// against the current time, so a scheduled request reports as <c>in_progress</c> exactly while it
/// is running and <c>done</c> once its window has passed — without anyone manually advancing it.
/// The manual states (<c>cancelled</c>, <c>deferred</c>) are authoritative and never derived.
/// Unscheduled (or not-yet-started) work is always <c>new</c>.
///
/// Derivation happens on read only; the stored <c>status</c> column is left untouched. This is the
/// single source of truth for effective status — apply it wherever a stored status is turned into a
/// status that is displayed, filtered, or counted (the read model and the insights trend).
/// </summary>
public static class RequestStatusCalculator
{
    public static RequestStatus Effective(RequestStatus stored, DateTime? startTs, DateTime? endTs, DateTime now)
    {
        // Manual states win: a cancelled or deferred request stays so regardless of its schedule.
        if (stored is RequestStatus.Cancelled or RequestStatus.Deferred)
            return stored;

        // Unscheduled work — or scheduled work that hasn't started yet — is "new".
        if (startTs is not { } start || endTs is not { } end || now < start)
            return RequestStatus.New;

        // Inside the scheduled window → in progress; past the window → done.
        return now < end ? RequestStatus.InProgress : RequestStatus.Done;
    }
}
