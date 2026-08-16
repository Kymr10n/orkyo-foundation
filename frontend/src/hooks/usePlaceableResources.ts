import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  createResource,
  deleteResource,
  getResources,
  updateResource,
  type CreateResourceRequest,
  type ResourceInfo,
  type UpdateResourceRequest,
} from "@foundation/src/lib/api/resources-api";
import type { ResourceGeometry } from "@foundation/src/types/geometry";
import { qk } from "@foundation/src/lib/api/query-keys";
import { DEFAULT_TARGET_RESOURCE_TYPE_KEYS } from "@foundation/src/constants";
import { useResourceTypes } from "./useResourceTypes";
import { useMemo } from "react";
import { errorMessage } from "./mutation-utils";

/**
 * The type keys that can be placed on a floorplan.
 *
 * Assignments are recorded under the resource's own type, so anything that asks "which resource
 * does this request occupy?" has to match against this set rather than the `space` key alone.
 *
 * While the types are still loading this answers with the built-in placeable type rather than an
 * empty set. An empty set is not a neutral answer: it says "nothing is placeable", which makes
 * every existing placement invisible — an optimistic reschedule would fail to clear the old
 * assignment and write a blank type key over it. Falling back keeps the pre-generalization
 * behaviour for the one type that always exists, and the real set replaces it a tick later.
 */
export function usePlaceableTypeKeys(): ReadonlySet<string> {
  const { data: resourceTypes = [], isSuccess } = useResourceTypes(true);
  return useMemo(() => {
    if (!isSuccess) return new Set(DEFAULT_TARGET_RESOURCE_TYPE_KEYS);
    return new Set(resourceTypes.filter((t) => t.hasGeometry).map((t) => t.key));
  }, [resourceTypes, isSuccess]);
}

/**
 * The resources a floorplan holds: every active resource at the site whose type declares
 * geometry, not just spaces. One plan carries them all, so this is a single type-agnostic query
 * rather than one per placeable type.
 *
 * The site scope reproduces the old site-scoped space read exactly. `siteId` on the generic list
 * is the wider "home or current site" predicate, but a placeable resource is created
 * `crossSiteAllowed: false` and cannot be assigned away, so its current site is always its home
 * site. A backend test pins that equivalence.
 */
export function usePlaceableResources(siteId: string | null) {
  return useQuery({
    queryKey: qk.resources.placeable(siteId),
    queryFn: async () => {
      const response = await getResources({ hasGeometry: true, isActive: true, siteId: siteId! });
      // The generic list orders by name; the floorplan and grid read by code first, which is what
      // the site-scoped space route used to return (ORDER BY code, name).
      return [...response.data].sort((a, b) =>
        (a.code ?? a.name).localeCompare(b.code ?? b.name),
      );
    },
    enabled: !!siteId,
  });
}

// Placeable mutations invalidate the whole resource namespace, not just this site's plan: the
// same rows are listed by the generic per-type pages, so a create on the floorplan has to show up
// there too. The request feed follows because assignments reference these resources.
const placeableInvalidates = (siteId: string) =>
  [qk.resources.all(), qk.resources.placeable(siteId), qk.requests.all()] as const;

export function useCreatePlaceableResource(siteId: string) {
  return useMutation({
    mutationFn: (data: CreateResourceRequest) => createResource(data),
    meta: {
      successMessage: "Resource created",
      errorMessage: "Failed to create resource",
      invalidates: placeableInvalidates(siteId),
    },
  });
}

export function useUpdatePlaceableResource(siteId: string) {
  return useMutation({
    mutationFn: ({ resourceId, data }: { resourceId: string; data: UpdateResourceRequest }) =>
      updateResource(resourceId, data),
    meta: {
      successMessage: "Resource updated",
      errorMessage: "Failed to update resource",
      invalidates: placeableInvalidates(siteId),
    },
  });
}

export function useDeletePlaceableResource(siteId: string) {
  // Optimistic: kept hand-rolled because the meta convention can't express onMutate
  // rollback. Invalidation is fired manually in onSettled to mirror the meta hooks.
  const queryClient = useQueryClient();
  const key = qk.resources.placeable(siteId);
  const invalidate = () => {
    for (const queryKey of placeableInvalidates(siteId)) {
      queryClient.invalidateQueries({ queryKey });
    }
  };
  return useMutation({
    mutationFn: (resourceId: string) => deleteResource(resourceId),
    onMutate: async (resourceId: string) => {
      // Cancel any in-flight refetches so they don't overwrite the optimistic update.
      await queryClient.cancelQueries({ queryKey: key });
      const previous = queryClient.getQueryData<ResourceInfo[]>(key);
      queryClient.setQueryData<ResourceInfo[]>(key, (old) =>
        old ? old.filter((r) => r.id !== resourceId) : [],
      );
      return { previous };
    },
    onError: (err, _resourceId, context) => {
      if (context?.previous !== undefined) {
        queryClient.setQueryData(key, context.previous);
      }
      // eslint-disable-next-line no-restricted-syntax -- optimistic-rollback mutation: meta can't express onMutate rollback, feedback stays hand-rolled (docs/dialog-feedback.md)
      toast.error("Failed to delete resource", { description: errorMessage(err) });
    },
    onSuccess: () => {
      // eslint-disable-next-line no-restricted-syntax -- optimistic-rollback mutation: meta can't express onMutate rollback, feedback stays hand-rolled (docs/dialog-feedback.md)
      toast.success("Resource deleted");
    },
    onSettled: invalidate,
  });
}

export function useMovePlaceableResource(siteId: string) {
  return useMutation({
    // Geometry only. The generic update writes just the fields the request names, so the space
    // route's habit of re-sending name/code/description/isPhysical on every drag was dead weight
    // — and re-sending them raced a concurrent rename back to its old value.
    mutationFn: ({ resourceId, geometry }: { resourceId: string; geometry: ResourceGeometry }) =>
      updateResource(resourceId, { geometry }),
    // Move/resize is a visible drag — silent on success, so no successMessage. The
    // error toast still routes through the meta convention (errorMessage opts in).
    meta: {
      errorMessage: "Failed to move resource",
      invalidates: placeableInvalidates(siteId),
    },
  });
}
