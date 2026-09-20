import { useMutation, useQuery } from "@tanstack/react-query";
import {
  addAvailabilityEventScope,
  deleteAvailabilityEventScope,
  type ScopeEffect,
  type ScopeTargetType,
} from "@foundation/src/lib/api/availability-events-api";
import { getResources } from "@foundation/src/lib/api/resources-api";
import { getResourceGroups } from "@foundation/src/lib/api/resource-groups-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import { useResourceTypes } from "@foundation/src/hooks/useResourceTypes";
import { STALE } from "@foundation/src/lib/core/query-client";

/** One scope override as the picker builds it, before it reaches the server. */
export interface ScopeDraft {
  targetType: ScopeTargetType;
  targetId: string;
  effect: ScopeEffect;
}

/** Everything an availability event can be scoped to: resources, groups and types. */
export function useScopePickerOptions() {
  const { data: resources } = useQuery({
    queryKey: qk.resources.allFlat(),
    queryFn: () => getResources({ isActive: true }).then((r) => r.items),
    staleTime: STALE.OPERATIONAL,
  });
  const { data: resourceTypes } = useResourceTypes();
  const { data: groups } = useQuery({
    queryKey: qk.resourceGroups.allFlat(),
    queryFn: async () => {
      const allGroups = await Promise.all(
        (resourceTypes ?? [])
          .filter((t) => t.isActive)
          .map((t) => getResourceGroups(t.key)),
      );
      return allGroups.flat();
    },
    // The group list is assembled per active type, so it waits for the type list.
    enabled: resourceTypes !== undefined,
    staleTime: STALE.OPERATIONAL,
  });
  return { resources, groups, resourceTypes };
}

export const useAddAvailabilityEventScope = (
  siteId: string,
  eventId: string,
  options: { onSuccess: () => void; onError: (err: Error) => void },
) =>
  useMutation({
    mutationFn: (req: ScopeDraft) => addAvailabilityEventScope(siteId, eventId, req),
    meta: { invalidates: [qk.scheduling.availabilityEventsAll()] },
    ...options,
  });

export const useDeleteAvailabilityEventScope = (siteId: string, eventId: string, scopeId: string) =>
  useMutation({
    mutationFn: () => deleteAvailabilityEventScope(siteId, eventId, scopeId),
    meta: { invalidates: [qk.scheduling.availabilityEventsAll()] },
  });
