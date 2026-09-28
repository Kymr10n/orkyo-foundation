import {
  createResourceCustomField,
  deleteResourceCustomField,
  getResourceCustomFields,
  updateResourceCustomField,
  type CreateResourceCustomFieldRequest,
  type UpdateResourceCustomFieldRequest,
} from "@foundation/src/lib/api/resource-custom-fields-api";
import { useMutation, useQuery } from "@tanstack/react-query";
import { qk } from "@foundation/src/lib/api/query-keys";
import { savedMessage, type SaveVariables } from "@foundation/src/hooks/mutation-utils";

/**
 * Definitions drive the resource form, so a change to them invalidates the resource lists
 * alongside the definition list itself.
 */
const CUSTOM_FIELD_INVALIDATES = (resourceTypeId: string) =>
  [
    qk.resourceTypes.customFields(resourceTypeId),
    // Both roots: `all()` covers the per-type lists and the floorplan, `allFlat()` is its own
    // namespace by design and is not reached by the first.
    qk.resources.all(),
    qk.resources.allFlat(),
  ] as const;

export const useResourceCustomFields = (resourceTypeId: string, enabled = true) =>
  useQuery({
    queryKey: qk.resourceTypes.customFields(resourceTypeId),
    queryFn: () => getResourceCustomFields(resourceTypeId),
    enabled,
  });

/** Create (`id: null`) or update one field definition of the type. */
export const useSaveResourceCustomField = (resourceTypeId: string) =>
  useMutation({
    mutationFn: (
      v: SaveVariables<CreateResourceCustomFieldRequest, UpdateResourceCustomFieldRequest>,
    ) =>
      v.id === null
        ? createResourceCustomField(resourceTypeId, v.data)
        : updateResourceCustomField(resourceTypeId, v.id, v.data),
    meta: {
      successMessage: savedMessage("Custom field created", "Custom field updated"),
      suppressErrorToast: true,
      invalidates: CUSTOM_FIELD_INVALIDATES(resourceTypeId),
    },
  });

/** Deletes the field and discards the values resources hold for it. */
export const useDeleteResourceCustomField = (resourceTypeId: string) =>
  useMutation({
    mutationFn: (fieldId: string) => deleteResourceCustomField(resourceTypeId, fieldId),
    meta: {
      successMessage: "Custom field removed",
      errorMessage: "Failed to remove custom field",
      invalidates: CUSTOM_FIELD_INVALIDATES(resourceTypeId),
    },
  });
