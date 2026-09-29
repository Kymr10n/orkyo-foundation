import {
  createCriterion,
  deleteCriterion,
  getCriteria,
  updateCriterion,
  updateCriterionApplicability,
} from "@foundation/src/lib/api/criteria-api";
import type {
  CreateCriterionRequest,
  Criterion,
  CriterionDataType,
  UpdateCriterionRequest,
} from "@foundation/src/types/criterion";
import { useMutation, useQuery } from "@tanstack/react-query";
import { qk } from "@foundation/src/lib/api/query-keys";
import { savedMessage, type SaveVariables } from "@foundation/src/hooks/mutation-utils";
import { STALE } from "@foundation/src/lib/core/query-client";
import { useInvalidateKeys } from "@foundation/src/hooks/useInvalidateKeys";

// Criteria drive request requirements; mutating them invalidates the request feed too.
const CRITERIA_INVALIDATES = [qk.criteria.all(), qk.requests.all()] as const;

/**
 * The criterion catalog.
 *
 * `enabled` lets a dialog fetch only while it is open, on the same key and the same freshness
 * tier as the eager callers, so the dialog shares their cache. Two near-copies of this hook
 * existed for exactly that — one of them omitted the freshness tier and so disagreed with the
 * others about staleness on a key all three shared.
 */
export const useCriteria = (enabled = true) =>
  useQuery({
    queryKey: qk.criteria.all(),
    queryFn: () => getCriteria(),
    staleTime: STALE.OPERATIONAL,
    enabled,
  });

/**
 * The criteria applicable to one resource type. Loaded only while the editor that assigns them
 * is open, so opening a resource list costs nothing.
 */
export const useCriteriaForResourceType = (resourceType: string, enabled: boolean) =>
  useQuery({
    queryKey: qk.criteria.byResourceType(resourceType),
    queryFn: () => getCriteria({ resourceType }),
    enabled,
  });

/** Refresh that list after a criterion was created for the type from inside the editor. */
export const useInvalidateCriteriaForResourceType = (resourceType: string): (() => Promise<void>) =>
  useInvalidateKeys(qk.criteria.byResourceType(resourceType));

export const useCreateCriterion = () =>
  useMutation({
    mutationFn: (data: CreateCriterionRequest) => createCriterion(data),
    meta: {
      successMessage: "Criterion created",
      errorMessage: "Failed to create criterion",
      invalidates: CRITERIA_INVALIDATES,
    },
  });

/** What the criterion edit dialog collects; `saveCriterion` turns it into one to three calls. */
export interface CriterionDraft {
  name: string;
  dataType: CriterionDataType;
  description: string;
  unit: string;
  enumValues: string[];
  resourceTypeKeys: string[];
}

/**
 * Create, or update details and applicability as they changed. The dialog validates the
 * draft before it gets here.
 */
async function saveCriterion(variables: SaveCriterionVariables): Promise<Criterion> {
  const draft = variables.id === null ? variables.data : variables.data.draft;
  const criterion = variables.id === null ? null : variables.data.previous;

  const name = draft.name.trim();
  const description = draft.description.trim() || undefined;
  const enumValues = draft.dataType === "Enum" ? draft.enumValues : undefined;
  const unit = draft.dataType === "Number" && draft.unit.trim() ? draft.unit.trim() : undefined;

  if (!criterion) {
    return createCriterion({
      name,
      description,
      dataType: draft.dataType,
      enumValues,
      unit,
      resourceTypeKeys: draft.resourceTypeKeys,
    });
  }

  const detailData: UpdateCriterionRequest = { description, enumValues, unit };
  // Name and DataType are sent only when actually changed.
  if (name !== criterion.name) detailData.name = name;
  if (draft.dataType !== criterion.dataType) detailData.dataType = draft.dataType;

  // Only PUT the criterion when there's a detail field to update — otherwise the
  // backend rejects an empty update with 400 "No fields to update" (e.g. a Boolean
  // criterion with no description, where the user only changed applicability).
  const hasDetailChanges =
    detailData.name !== undefined ||
    detailData.dataType !== undefined ||
    detailData.description !== undefined ||
    detailData.enumValues !== undefined ||
    detailData.unit !== undefined;

  let updated = criterion;
  if (hasDetailChanges) {
    updated = await updateCriterion(criterion.id, detailData);
  }

  const currentKeys = [...(criterion.resourceTypeKeys ?? [])].sort().join(",");
  const newKeys = [...draft.resourceTypeKeys].sort().join(",");
  if (currentKeys !== newKeys) {
    await updateCriterionApplicability(criterion.id, {
      resourceTypeKeys: draft.resourceTypeKeys,
    });
  }

  return updated;
}

/** An update carries the criterion as it was, so only the changed details are sent. */
export type SaveCriterionVariables = SaveVariables<
  CriterionDraft,
  { draft: CriterionDraft; previous: Criterion }
>;

/** The edit dialog's save: create, or update details and applicability as they changed. */
export const useSaveCriterion = () =>
  useMutation({
    mutationFn: saveCriterion,
    meta: {
      successMessage: savedMessage("Criterion created", "Criterion updated"),
      suppressErrorToast: true,
      invalidates: CRITERIA_INVALIDATES,
    },
  });

export const useDeleteCriterion = () =>
  useMutation({
    mutationFn: (id: string) => deleteCriterion(id),
    meta: {
      successMessage: "Criterion deleted",
      errorMessage: "Failed to delete criterion",
      invalidates: CRITERIA_INVALIDATES,
    },
  });
