import { useEffect, useMemo } from "react";
import { useAppStore } from "@foundation/src/store/app-store";
import { buildPreviewSchedule } from "@foundation/src/domain/scheduling/schedule-preview";
import { evaluateSchedule } from "@foundation/src/domain/scheduling/schedule-validator";
import { validateSpaceRequirements } from "@foundation/src/domain/scheduling/capability-matcher";
import { getSpaceCapabilities } from "@foundation/src/lib/api/space-capability-api";
import type { Request } from "@foundation/src/types/requests";
import type { Space } from "@foundation/src/types/space";

/**
 * Manages all conflict detection for the utilization page:
 *   1. Scheduling conflicts (overlap/capacity, below-min-duration) — synchronous, pure
 *   2. Capability conflicts (connector_mismatch, load_exceeded) — async, debounced
 *
 * Returns the set of conflicting request IDs for downstream consumers (e.g. floorplan).
 */
export function useSchedulingConflicts(
  requests: Request[],
  spaces: Space[],
  selectedSiteId: string | null,
) {
  const setConflicts = useAppStore((s) => s.setConflicts);

  // Build space capacity map for capacity-aware conflict detection.
  const spaceCapacities = useMemo(
    () => new Map(spaces.map((s) => [s.id, s.capacity ?? 1])),
    [spaces],
  );

  // Scheduling validation via the domain pipeline (overlap/capacity + below-min-duration).
  const schedulingValidation = useMemo(
    () => evaluateSchedule(buildPreviewSchedule(requests, null), spaceCapacities),
    [requests, spaceCapacities],
  );

  // Set of request IDs with at least one scheduling conflict.
  const conflictingRequestIds = useMemo(
    () => new Set(schedulingValidation.keys()),
    [schedulingValidation],
  );

  // Sync scheduling validation to the Zustand store so ConflictsPage can display them.
  useEffect(() => {
    for (const request of requests) {
      setConflicts(request.id, schedulingValidation.get(request.id) ?? []);
    }
  }, [schedulingValidation, requests, setConflicts]);

  // Capability conflict detection (connector_mismatch, load_exceeded, etc.).
  // Async (API call per space), merged with scheduling conflicts already in store.
  // Debounced at 500 ms to avoid flooding the API during rapid edits.
  useEffect(() => {
    if (!selectedSiteId || !requests.length || !spaces.length) return;

    let isCancelled = false;

     
    const timer = setTimeout(async () => {
      const scheduledRequests = requests.filter((r) => r.spaceId && r.requirements?.length);
      const spaceIds = [...new Set(scheduledRequests.map((r) => r.spaceId!))];

      const capsBySpace = new Map<string, Awaited<ReturnType<typeof getSpaceCapabilities>>>();
      const results = await Promise.allSettled(
        spaceIds.map(async (spaceId) => {
          const caps = await getSpaceCapabilities(selectedSiteId, spaceId);
          return { spaceId, caps };
        }),
      );
      if (isCancelled) return;

      for (const result of results) {
        if (result.status === "fulfilled") {
          capsBySpace.set(result.value.spaceId, result.value.caps);
        }
      }

      for (const request of scheduledRequests) {
        if (isCancelled) return;
        const capabilities = capsBySpace.get(request.spaceId!);
        if (!capabilities) continue;

        const capConflicts = validateSpaceRequirements(request, capabilities);
        if (capConflicts.length > 0) {
          const existing = useAppStore.getState().conflicts.get(request.id) ?? [];
          setConflicts(request.id, [...existing, ...capConflicts]);
        }
      }
    }, 500);

    return () => {
      isCancelled = true;
      clearTimeout(timer);
    };
  }, [requests, spaces, selectedSiteId, setConflicts]);

  return { schedulingValidation, conflictingRequestIds, spaceCapacities };
}
