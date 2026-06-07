import { useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { useRequests, useSpaces } from "@foundation/src/hooks/useUtilization";
import { useAppStore } from "@foundation/src/store/app-store";
import { buildPreviewSchedule } from "@foundation/src/domain/scheduling/schedule-preview";
import { evaluateSchedule } from "@foundation/src/domain/scheduling/schedule-validator";
import { capabilityConflictsFromValidation } from "@foundation/src/domain/scheduling/assignment-conflicts";
import {
  validateAssignmentsBatch,
  type ValidateResourceAssignmentRequest,
} from "@foundation/src/lib/api/resource-assignments-api";
import { getSpaceAssignment } from "@foundation/src/domain/scheduling/request-assignments";
import type { Conflict } from "@foundation/src/types/requests";

/**
 * Computes scheduling and capability conflicts for the current site.
 *
 * - **Scheduling** conflicts (overlap, capacity, duration) are evaluated
 *   client-side (`evaluateSchedule`) so the grid can reflect them live while a
 *   bar is being dragged, before anything is committed.
 * - **Capability** conflicts (does the space satisfy the request's
 *   requirements) are computed by the backend — the single source of truth —
 *   via the batch validate endpoint. The client no longer re-implements
 *   requirement matching, which is why requests no longer need their
 *   `requirements` hydrated for conflict detection.
 *
 * All consumers (ConflictsPage, RequestsPage, UtilizationPage) call this hook
 * directly; React Query deduplicates the validation fetch.
 */
export function useConflicts() {
  const selectedSiteId = useAppStore((s) => s.selectedSiteId);
  const { data: requests = [] } = useRequests();
  const { data: spaces = [] } = useSpaces(selectedSiteId);

  const spaceCapacities = useMemo(
    () => new Map(spaces.map((s) => [s.id, s.capacity ?? 1])),
    [spaces],
  );

  // Pure scheduling validation — no async needed
  const schedulingValidation = useMemo(
    () => evaluateSchedule(buildPreviewSchedule(requests, null), spaceCapacities),
    [requests, spaceCapacities],
  );

  // One validation item per request that holds a committed space assignment.
  // excludeAssignmentId keeps the backend overbook check from flagging the
  // assignment against itself (irrelevant to the capability conflicts we
  // consume here, but keeps the request semantically correct).
  const validationItems = useMemo<ValidateResourceAssignmentRequest[]>(() => {
    return requests.flatMap((r) => {
      const assignment = getSpaceAssignment(r);
      if (!assignment?.startUtc || !assignment.endUtc) return [];
      return [{
        requestId: r.id,
        resourceId: assignment.resourceId,
        startUtc: assignment.startUtc,
        endUtc: assignment.endUtc,
        excludeAssignmentId: assignment.id,
      }];
    });
  }, [requests]);

  // Stable, deterministic query key derived from the items' identity.
  const validationKey = useMemo(
    () => validationItems
      .map((i) => `${i.requestId}:${i.resourceId}:${i.startUtc}:${i.endUtc}`)
      .sort(),
    [validationItems],
  );

  // Async capability validation — backend authoritative; React Query caches it.
  const { data: capabilityConflicts } = useQuery({
    queryKey: ["assignment-capability-validation", selectedSiteId, validationKey],
    queryFn: async () => {
      const results = await validateAssignmentsBatch(validationItems);
      const map = new Map<string, Conflict[]>();
      for (const item of results) {
        const conflicts = capabilityConflictsFromValidation(item);
        if (conflicts.length > 0 && item.requestId) {
          map.set(item.requestId, conflicts);
        }
      }
      return map;
    },
    enabled: validationItems.length > 0,
    staleTime: 500,
  });

  // Merge scheduling + capability conflicts into a single map
  const conflicts = useMemo((): Map<string, Conflict[]> => {
    const merged = new Map<string, Conflict[]>();
    for (const [requestId, reqConflicts] of schedulingValidation) {
      merged.set(requestId, [...reqConflicts]);
    }
    if (capabilityConflicts) {
      for (const [requestId, capConflicts] of capabilityConflicts) {
        const existing = merged.get(requestId) ?? [];
        merged.set(requestId, [...existing, ...capConflicts]);
      }
    }
    return merged;
  }, [schedulingValidation, capabilityConflicts]);

  const conflictingRequestIds = useMemo(
    () => new Set(conflicts.keys()),
    [conflicts],
  );

  return { conflicts, conflictingRequestIds, schedulingValidation, spaceCapacities, capabilityConflicts: capabilityConflicts ?? new Map() };
}
