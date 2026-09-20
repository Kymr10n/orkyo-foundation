import { useMutation, useQuery } from "@tanstack/react-query";
import {
  createTemplate,
  deleteTemplate,
  getTemplates,
  updateTemplate,
} from "@foundation/src/lib/api/template-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import type {
  CreateTemplateRequest,
  Template,
  UpdateTemplateRequest,
} from "@foundation/src/types/templates";

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

/**
 * Create or update, by whether an existing template was handed in. Both modes feed the
 * same `templates-${entityType}` list query (TemplateSettings).
 */
export const useSaveTemplate = (
  template: Template | null,
  entityType: TemplateEntityType,
  options: { onSuccess: () => void; onError: (err: Error) => void },
) =>
  useMutation({
    mutationFn: async (request: CreateTemplateRequest | UpdateTemplateRequest) => {
      if (template) await updateTemplate(template.id, request);
      else await createTemplate(request as CreateTemplateRequest);
    },
    meta: {
      successMessage: template ? "Template updated" : "Template created",
      suppressErrorToast: true,
      invalidates: [qk.templates(entityType)],
    },
    ...options,
  });
