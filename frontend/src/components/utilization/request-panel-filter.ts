import type { FlatTreeEntry } from "@foundation/src/domain/request-tree";
import type { RequestStatus } from "@foundation/src/types/requests";
import { REQUEST_STATUS, PLANNING_MODE } from "@foundation/src/constants";

export type ScheduledFilter = "all" | "scheduled" | "unscheduled";

export interface PanelFilterOptions {
  searchQuery: string;
  statusFilter: RequestStatus | "all";
  scheduledFilter: ScheduledFilter;
  collapsedIds: Set<string>;
}

/**
 * Selects the flat tree entries the Requests panel should show, given the active filters and the
 * tree's collapse state.
 *
 * When a search term or a specific status filter is active we **ignore collapse**: every matching
 * request is revealed regardless of where it sits in the tree. Otherwise a match nested under a
 * collapsed parent (e.g. an `in_progress` job under a collapsed summary) would stay hidden — the user
 * filters precisely to find such requests. The scheduled toggle stays collapse-aware because it is the
 * panel's default view dimension, not a search. `deferred` is parked: hidden under "All Statuses",
 * shown only when explicitly selected.
 */
export function filterPanelEntries(
  flatEntries: FlatTreeEntry[],
  { searchQuery, statusFilter, scheduledFilter, collapsedIds }: PanelFilterOptions,
): FlatTreeEntry[] {
  const result: FlatTreeEntry[] = [];
  const collapsedAncestorDepths: number[] = [];
  const ignoreCollapse = searchQuery.trim() !== "" || statusFilter !== "all";

  for (const entry of flatEntries) {
    if (!ignoreCollapse) {
      // Skip children of collapsed nodes.
      while (
        collapsedAncestorDepths.length > 0 &&
        entry.depth <= collapsedAncestorDepths[collapsedAncestorDepths.length - 1]
      ) {
        collapsedAncestorDepths.pop();
      }
      if (collapsedAncestorDepths.length > 0) continue;
    }

    const { request } = entry;

    if (searchQuery && !request.name.toLowerCase().includes(searchQuery.toLowerCase())) {
      continue;
    }

    if (statusFilter === "all") {
      if (request.status === REQUEST_STATUS.DEFERRED) continue;
    } else if (request.status !== statusFilter) {
      continue;
    }

    const isScheduled = !!request.isScheduled;
    if (scheduledFilter === "scheduled" && !isScheduled && request.planningMode !== PLANNING_MODE.CONTAINER) {
      continue;
    }
    if (scheduledFilter === "unscheduled" && isScheduled) {
      continue;
    }

    result.push(entry);

    // Only relevant when honouring collapse — track this node to skip its children.
    if (!ignoreCollapse && entry.hasChildren && collapsedIds.has(request.id)) {
      collapsedAncestorDepths.push(entry.depth);
    }
  }

  return result;
}
