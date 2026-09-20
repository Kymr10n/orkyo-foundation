import { useCallback, useEffect, useState, type Dispatch, type SetStateAction } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { getAssignmentsByResource } from "@foundation/src/lib/api/resource-assignments-api";
import { getRequests } from "@foundation/src/lib/api/request-api";
import {
  getResourceAssignmentOptions,
  type ResourceAssignmentOption,
} from "@foundation/src/lib/api/resource-candidate-requests-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import { STALE } from "@foundation/src/lib/core/query-client";
import { invalidateRequestData } from "@foundation/src/lib/core/invalidate-request-data";

/** One resource's assignments over a window — the blocks on its own calendar. */
export const useResourceAssignments = (
  resourceId: string,
  from: Date,
  to: Date,
  enabled: boolean,
) =>
  useQuery({
    queryKey: qk.resources.assignments(resourceId, from, to),
    queryFn: () => getAssignmentsByResource(resourceId, from, to),
    staleTime: STALE.OPERATIONAL,
    enabled,
  });

/**
 * Names only: an assignment carries a request id, and a block labelled by id tells nobody what
 * the resource is doing.
 */
export const useScheduleRequestNames = (enabled: boolean) =>
  useQuery({
    queryKey: qk.requests.list(),
    queryFn: () => getRequests(),
    staleTime: STALE.STANDARD,
    enabled,
  });

/**
 * Everything one resource's calendar shows after a block moved: its absences, its assignment
 * windows, and the request-derived feeds the board reads.
 */
export const useRefreshResourceSchedule = (resourceId: string): (() => void) => {
  const queryClient = useQueryClient();
  return useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: qk.resources.absences(resourceId) });
    void queryClient.invalidateQueries({ queryKey: qk.resources.assignmentsFor(resourceId) });
    invalidateRequestData(queryClient);
  }, [queryClient, resourceId]);
};

export interface ResourceAssignmentOptions {
  options: ResourceAssignmentOption[];
  setOptions: Dispatch<SetStateAction<ResourceAssignmentOption[]>>;
  isLoading: boolean;
  loadError: string | null;
  /** Whether the clicked period is already over — the empty state words itself differently. */
  periodPassed: boolean;
}

/**
 * The requests one resource can be booked onto in a window, assigned ones first.
 *
 * `onLoaded` runs against the freshly fetched options (and the effect's cancellation flag) for
 * the decorative work that follows the list — it is deliberately not a dependency, so a caller
 * need not memoize it.
 */
export function useResourceAssignmentOptions(
  open: boolean,
  resourceId: string,
  start: string,
  end: string,
  onLoaded: (options: ResourceAssignmentOption[], cancelled: boolean) => void,
): ResourceAssignmentOptions {
  const [options, setOptions] = useState<ResourceAssignmentOption[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [periodPassed, setPeriodPassed] = useState(false);

  // Clearing the working state on open / target change is a render-phase update, not an
  // effect (see useEntityFormDialog.ts); the fetch that follows is a real side effect and stays below.
  const [synced, setSynced] = useState<{
    open: boolean;
    resourceId: string;
    start: string;
    end: string;
  } | null>(null);
  if (
    synced?.open !== open ||
    synced.resourceId !== resourceId ||
    synced.start !== start ||
    synced.end !== end
  ) {
    setSynced({ open, resourceId, start, end });
    if (open) {
      setLoadError(null);
      setIsLoading(true);
    }
  }

  useEffect(() => {
    if (!open) return;
    let cancelled = false;
    getResourceAssignmentOptions(resourceId, start, end)
      .then((opts) => {
        if (cancelled) return;
        // Stamped here rather than read during render: the empty-state wording depends on
        // whether the period is already over, and reading the clock in render is impure.
        setPeriodPassed(new Date(end).getTime() <= Date.now());
        setOptions(
          [...opts].sort((a, b) => {
            const aAssigned = a.assignmentId !== null ? 0 : 1;
            const bAssigned = b.assignmentId !== null ? 0 : 1;
            if (aAssigned !== bAssigned) return aAssigned - bAssigned;
            return a.name.localeCompare(b.name);
          }),
        );
        // Surface existing conflicts on the already-assigned rows. Decorative — runs
        // in the background; each assignment is excluded from its own overbook check.
        onLoaded(opts, cancelled);
      })
      .catch((err: unknown) => {
        if (!cancelled) setLoadError(err instanceof Error ? err.message : "Failed to load");
      })
      .finally(() => {
        if (!cancelled) setIsLoading(false);
      });
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, resourceId, start, end]);

  return { options, setOptions, isLoading, loadError, periodPassed };
}
