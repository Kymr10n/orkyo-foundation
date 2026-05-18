import { useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { useRequests, useSpaces } from "@foundation/src/hooks/useUtilization";
import { useAppStore } from "@foundation/src/store/app-store";
import { buildPreviewSchedule } from "@foundation/src/domain/scheduling/schedule-preview";
import { evaluateSchedule } from "@foundation/src/domain/scheduling/schedule-validator";
import { validateSpaceRequirements } from "@foundation/src/domain/scheduling/capability-matcher";
import { getSpaceCapabilities } from "@foundation/src/lib/api/space-capability-api";
import { getSpaceResourceId } from "@foundation/src/domain/scheduling/request-assignments";
import type { Conflict } from "@foundation/src/types/requests";

/**
 * Computes scheduling and capability conflicts for the current site.
 *
 * Replaces the previous pattern where useSchedulingConflicts synced results
 * into Zustand. All consumers (ConflictsPage, RequestsPage, UtilizationPage)
 * call this hook directly; React Query deduplicates the capability fetch.
 *
 * - Scheduling conflicts (overlap, capacity, duration) — synchronous useMemo
 * - Capability conflicts (connector, load) — async useQuery, debounced at 500ms staleTime
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

  // Stable, sorted resource IDs for a deterministic query key
  const scheduledResourceIds = useMemo(() => {
    return [...new Set(
      requests
        .filter((r) => getSpaceResourceId(r) && r.requirements?.length)
        .map((r) => getSpaceResourceId(r)!),
    )].sort();
  }, [requests]);

  // Async capability fetch — React Query caches and deduplicates across consumers
  const { data: capabilitiesMap } = useQuery({
    queryKey: ["space-capabilities", selectedSiteId, scheduledResourceIds],
    queryFn: async () => {
      const map = new Map<string, Awaited<ReturnType<typeof getSpaceCapabilities>>>();
      const results = await Promise.allSettled(
        scheduledResourceIds.map(async (resourceId) => {
          const caps = await getSpaceCapabilities(selectedSiteId!, resourceId);
          return { resourceId, caps };
        }),
      );
      for (const result of results) {
        if (result.status === "fulfilled") {
          map.set(result.value.resourceId, result.value.caps);
        }
      }
      return map;
    },
    enabled: !!selectedSiteId && scheduledResourceIds.length > 0,
    staleTime: 500,
  });

  // Merge scheduling + capability conflicts into a single map
  const conflicts = useMemo((): Map<string, Conflict[]> => {
    const merged = new Map<string, Conflict[]>();
    for (const [requestId, reqConflicts] of schedulingValidation) {
      merged.set(requestId, [...reqConflicts]);
    }
    if (capabilitiesMap) {
      for (const request of requests) {
        const resourceId = getSpaceResourceId(request);
        if (!resourceId) continue;
        const capabilities = capabilitiesMap.get(resourceId);
        if (!capabilities) continue;
        const capConflicts = validateSpaceRequirements(request, capabilities);
        if (capConflicts.length > 0) {
          const existing = merged.get(request.id) ?? [];
          merged.set(request.id, [...existing, ...capConflicts]);
        }
      }
    }
    return merged;
  }, [schedulingValidation, capabilitiesMap, requests]);

  const conflictingRequestIds = useMemo(
    () => new Set(conflicts.keys()),
    [conflicts],
  );

  return { conflicts, conflictingRequestIds, schedulingValidation, spaceCapacities };
}
