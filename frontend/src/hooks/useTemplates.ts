import { useMutation, useQuery } from "@tanstack/react-query";
import {
  createTemplate,
  deleteTemplate,
  getTemplates,
  updateTemplate,
} from "@foundation/src/lib/api/template-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import type { CreateTemplateRequest, UpdateTemplateRequest } from "@foundation/src/types/templates";
import { savedMessage, type SaveVariables } from "@foundation/src/hooks/mutation-utils";

type TemplateEntityType = "request" | "space" | "group";

/** Templates of one entity type. `enabled` lets a dialog fetch only while it is open. */
export const useTemplates = (entityType: TemplateEntityType, enabled = true) =>
  useQuery({
    queryKey: qk.templates(entityType),
    queryFn: () => getTemplates(entityType),
    enabled,
  });

export const useDeleteTemplate = (entityType: TemplateEntityType) =>
  useMutation({
    mutationFn: (id: string) => deleteTemplate(id),
    meta: {
      successMessage: "Template deleted",
      errorMessage: "Failed to delete template",
      invalidates: [qk.templates(entityType)],
    },
  });

export type SaveTemplateVariables = SaveVariables<CreateTemplateRequest, UpdateTemplateRequest>;

/**
 * Create or update, by the variables' id. Both modes feed the same `templates-${entityType}`
 * list query (TemplateSettings).
 */
export const useSaveTemplate = (
  entityType: TemplateEntityType,
  options: { onSuccess: () => void; onError: (err: Error) => void },
) =>
  useMutation({
    mutationFn: async (v: SaveTemplateVariables) => {
      if (v.id === null) await createTemplate(v.data);
      else await updateTemplate(v.id, v.data);
    },
    meta: {
      successMessage: savedMessage("Template created", "Template updated"),
      suppressErrorToast: true,
      invalidates: [qk.templates(entityType)],
    },
    ...options,
  });
