import type { Request } from "@foundation/src/types/requests";
import type { ShellGroup } from "./TimelineGridShell";

/**
 * Row selection and grouping for the Requests canvas — the task-centric utilization grid.
 *
 * Pure and separate from the canvas so the ordering rules are testable without rendering. The
 * canvas puts requests on the rows and their parents on the group headers; the other two grids
 * put resources there and group by resource group (`groupRowsByResourceGroup`), which is a
 * different question with a different source of names, so this is its own helper.
 */

/** Group id for requests with no parent. Trails every named group. */
export const UNGROUPED_ID = "ungrouped";
export const UNGROUPED_NAME = "Ungrouped";

/**
 * Header for a parent the canvas cannot name: the row says which group it is in, but the parent
 * is neither in the page's request feeds nor in the request list (it may have been deleted since
 * the child was fetched, or the list is still loading).
 */
export const UNKNOWN_PARENT_NAME = "Unknown group";

/**
 * The requests that draw a bar in the window: both dates set and [start, end) overlapping
 * [viewStartMs, viewEndMs). The same half-open rule `isOutsideView` applies to scheduler entries,
 * so a request ending exactly at the window start is out, one starting exactly at its end too.
 */
export function selectCanvasRequests(
  requests: readonly Request[],
  viewStartMs: number,
  viewEndMs: number,
): Request[] {
  return requests.filter((r) => {
    if (!r.startTs || !r.endTs) return false;
    const startMs = new Date(r.startTs).getTime();
    const endMs = new Date(r.endTs).getTime();
    return endMs > viewStartMs && startMs < viewEndMs;
  });
}

/** Rows read top-to-bottom in time order; sortOrder then name settle simultaneous starts. */
function compareRows(a: Request, b: Request): number {
  const startDiff = new Date(a.startTs!).getTime() - new Date(b.startTs!).getTime();
  if (startDiff !== 0) return startDiff;
  if (a.sortOrder !== b.sortOrder) return a.sortOrder - b.sortOrder;
  return a.name.localeCompare(b.name);
}

interface GroupMeta {
  id: string;
  name: string;
  sortOrder: number;
  known: boolean;
}

/**
 * Buckets the rows by `parentRequestId` into groups for `TimelineGridShell`.
 *
 * Groups are keyed by the parent's id, so a collapse survives re-sorting and refetches. Named
 * groups come first in the parent's sortOrder, then name, then id; parents that cannot be
 * resolved follow them as "Unknown group"; rows with no parent close the list as "Ungrouped",
 * which is only appended when it has rows. `sortOrder` is meaningful among siblings only, so the
 * name is the real tie-breaker for parents from different subtrees.
 *
 * @param resolveParent Looks a parent up by id — the page's request feeds first, then the request
 *   list — and returns undefined when neither knows it.
 */
export function groupRequestsByParent(
  rows: readonly Request[],
  resolveParent: (parentId: string) => Request | undefined,
): ShellGroup<Request>[] {
  const sortedRows = [...rows].sort(compareRows);

  const byParent = new Map<string, Request[]>();
  const ungrouped: Request[] = [];
  for (const row of sortedRows) {
    if (!row.parentRequestId) {
      ungrouped.push(row);
      continue;
    }
    const list = byParent.get(row.parentRequestId);
    if (list) list.push(row);
    else byParent.set(row.parentRequestId, [row]);
  }

  const metas: GroupMeta[] = [...byParent.keys()].map((id) => {
    const parent = resolveParent(id);
    return {
      id,
      name: parent?.name ?? UNKNOWN_PARENT_NAME,
      sortOrder: parent?.sortOrder ?? Number.POSITIVE_INFINITY,
      known: parent !== undefined,
    };
  });
  metas.sort((a, b) => {
    if (a.known !== b.known) return a.known ? -1 : 1;
    if (a.sortOrder !== b.sortOrder) return a.sortOrder - b.sortOrder;
    const byName = a.name.localeCompare(b.name);
    if (byName !== 0) return byName;
    return a.id.localeCompare(b.id);
  });

  const groups: ShellGroup<Request>[] = metas.map((m) => ({
    id: m.id,
    name: m.name,
    rows: byParent.get(m.id)!,
  }));
  if (ungrouped.length > 0) {
    groups.push({ id: UNGROUPED_ID, name: UNGROUPED_NAME, rows: ungrouped });
  }
  return groups;
}
