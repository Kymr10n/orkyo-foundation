import { useMutation, useQuery } from "@tanstack/react-query";
import { deleteResource, getResources } from "@foundation/src/lib/api/resources-api";
import type { ResourceTypeInfo } from "@foundation/src/lib/api/resource-types-api";
import {
  deleteResourceCapability,
  getResourceCapabilities,
  upsertResourceCapability,
} from "@foundation/src/lib/api/resource-capabilities-api";
import { diffCapabilityAssignments } from "@foundation/src/components/capabilities/capability-diff";
import type { CriterionValue } from "@foundation/src/types/criterion";
import { qk } from "@foundation/src/lib/api/query-keys";
import { STALE } from "@foundation/src/lib/core/query-client";

/**
 * One type's resources, scoped by the top-bar site picker. The backend reads site membership as
 * "home site, or the site it is currently assigned to", so a resource with neither is not listed
 * under any site — "All sites" (`null`) is what shows it.
 */
export const useResourcesOfType = (resourceTypeKey: string, siteId: string | null) =>
  useQuery({
    queryKey: [...qk.resources.byType(resourceTypeKey), { siteId }],
    queryFn: () => getResources({ resourceTypeKey, siteId: siteId ?? undefined }),
  });

/**
 * Resources of one type backing that type's utilization grid — name/metadata lookup, tenant-wide.
 * Its own key on purpose (see `qk.resources.utilizationGrid`).
 */
export const useResourcesForUtilizationGrid = (resourceTypeKey: string) =>
  useQuery({
    queryKey: qk.resources.utilizationGrid(resourceTypeKey),
    queryFn: () => getResources({ resourceTypeKey, isActive: true }),
    staleTime: STALE.OPERATIONAL,
  });

/** Deactivation, not deletion: the row stops appearing in planning and its history is kept. */
export const useDeleteResource = (resourceType: ResourceTypeInfo) =>
  useMutation({
    mutationFn: (id: string) => deleteResource(id),
    meta: {
      successMessage: `${resourceType.displayName} deactivated`,
      errorMessage: `Failed to deactivate ${resourceType.displayName.toLowerCase()}`,
      invalidates: [qk.resources.byType(resourceType.key), qk.resources.allFlat()],
    },
  });

/** Criterion values assigned to one resource. */
export const useResourceCapabilities = (resourceId: string, enabled: boolean) =>
  useQuery({
    queryKey: qk.resources.capabilities(resourceId),
    queryFn: () => getResourceCapabilities(resourceId),
    enabled,
  });

/**
 * Persist the whole assignment set of one resource: re-reads the stored values so the diff is
 * against what is there now, then upserts what changed and deletes what was dropped.
 */
export const useSaveResourceCapabilities = (
  resourceId: string,
  valueLabel: { plural: string; singular: string },
) =>
  useMutation({
    mutationFn: async (desired: Map<string, CriterionValue | null>) => {
      const existing = await getResourceCapabilities(resourceId);
      const { toPersist, toDeleteIds } = diffCapabilityAssignments(existing, desired, 'upsert');
      await Promise.all([
        ...toPersist.map((cap) => upsertResourceCapability(resourceId, cap)),
        ...toDeleteIds.map((id) => deleteResourceCapability(resourceId, id)),
      ]);
    },
    meta: {
      successMessage: `${valueLabel.plural} saved`,
      suppressErrorToast: true,
      invalidates: [qk.resources.capabilities(resourceId)],
    },
  });
