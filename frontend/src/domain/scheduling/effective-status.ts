import type { Request, RequestStatus } from "@foundation/src/types/requests";
import { REQUEST_STATUS } from "@foundation/src/constants";

/**
 * Client mirror of the backend `RequestStatusCalculator.Effective`
 * (orkyo-foundation/backend/core/Helpers/RequestStatusCalculator.cs).
 *
 * The active lifecycle (`new → in_progress → done`) is derived from the schedule window vs `nowMs`;
 * `cancelled`/`deferred` are manual and pass through; unscheduled (or not-yet-started) work is `new`.
 *
 * The backend already computes this, but its value is fixed at fetch time. Recomputing on the client
 * against a live clock keeps the operational views (the "In Progress" filter, status badges) in lockstep
 * with the grid's live "Now" marker, instead of going stale until the next refetch. Keep in sync with
 * the C# rule above.
 */
export function effectiveRequestStatus(
  stored: RequestStatus,
  startTs: string | null | undefined,
  endTs: string | null | undefined,
  nowMs: number,
): RequestStatus {
  // Manual states win — a cancelled or deferred request stays so regardless of its schedule.
  if (stored === REQUEST_STATUS.CANCELLED || stored === REQUEST_STATUS.DEFERRED) return stored;
  if (!startTs || !endTs) return REQUEST_STATUS.NEW;

  const start = new Date(startTs).getTime();
  const end = new Date(endTs).getTime();
  if (Number.isNaN(start) || Number.isNaN(end) || nowMs < start) return REQUEST_STATUS.NEW;
  return nowMs < end ? REQUEST_STATUS.IN_PROGRESS : REQUEST_STATUS.DONE;
}

/**
 * Apply {@link effectiveRequestStatus} across a list. Returns the SAME array reference when no status
 * actually changes, so consumers (and their memos) only re-render on a real status flip — not on every
 * clock tick.
 */
export function withEffectiveStatus(requests: Request[], nowMs: number): Request[] {
  let changed = false;
  const next = requests.map((r) => {
    const eff = effectiveRequestStatus(r.status, r.startTs, r.endTs, nowMs);
    if (eff === r.status) return r;
    changed = true;
    return { ...r, status: eff };
  });
  return changed ? next : requests;
}
