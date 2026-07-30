import {
  deactivateResourceTypeField,
  deleteResourceType,
  getResourceTypeFields,
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

// Types and their field definitions are slow-moving reference data, so both queries
// inherit the standard staleTime rather than declaring one.
export const useResourceTypes = (isActive?: boolean) =>
  useQuery({
    queryKey: [...qk.resourceTypes.all(), { isActive: isActive ?? null }],
    queryFn: () => getResourceTypes(isActive),
  });

/** Custom field definitions of one type. Idle until a type id is known. */
export const useResourceTypeFields = (resourceTypeId: string | undefined, includeInactive = false) =>
  useQuery({
    queryKey: [...qk.resourceTypes.fields(resourceTypeId ?? ""), { includeInactive }],
    queryFn: () => getResourceTypeFields(resourceTypeId!, includeInactive),
    enabled: Boolean(resourceTypeId),
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

export const useDeactivateResourceTypeField = () =>
  useMutation({
    mutationFn: ({ resourceTypeId, fieldId }: { resourceTypeId: string; fieldId: string }) =>
      deactivateResourceTypeField(resourceTypeId, fieldId),
    meta: {
      successMessage: "Field deactivated",
      errorMessage: "Failed to deactivate field",
      invalidates: RESOURCE_TYPE_INVALIDATES,
    },
  });
