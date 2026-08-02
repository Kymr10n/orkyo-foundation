import {
  deleteResourceType,
  getResourceTypes,
} from "@foundation/src/lib/api/resource-types-api";
import { useMutation, useQuery } from "@tanstack/react-query";
import { qk } from "@foundation/src/lib/api/query-keys";

/**
 * A type's shape drives resource forms and the nav, so type edits invalidate the
 * resource lists as well as the type list itself. Shared with the edit dialogs,
 * which persist through `useEntityFormDialog`.
 */
export const RESOURCE_TYPE_INVALIDATES = [
  qk.resourceTypes.all(),
  qk.resources.allFlat(),
] as const;

// Types are slow-moving reference data, so this inherits the standard staleTime
// rather than declaring one.
export const useResourceTypes = (isActive?: boolean) =>
  useQuery({
    queryKey: [...qk.resourceTypes.all(), { isActive: isActive ?? null }],
    queryFn: () => getResourceTypes(isActive),
  });


/** Removes the type, or deactivates it server-side when resources still reference it. */
export const useDeleteResourceType = () =>
  useMutation({
    mutationFn: (id: string) => deleteResourceType(id),
    meta: {
      successMessage: "Resource type removed",
      errorMessage: "Failed to remove resource type",
      invalidates: RESOURCE_TYPE_INVALIDATES,
    },
  });
