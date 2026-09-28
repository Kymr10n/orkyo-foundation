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
 * Rules the server cannot state as a disabled Save button: both carry a reason the
 * user has to read. Thrown from the save so the dialog renders them in its own
 * ErrorAlert, the same place a failed request lands.
 */
function validate(draft: CriterionDraft): string | null {
  if (!draft.name.trim()) return "Name is required";
  if (draft.dataType === "Enum" && draft.enumValues.length === 0) {
    return "At least one enum value is required";
  }
  if (draft.resourceTypeKeys.length === 0) {
    return "At least one applicability scope must be selected";
  }
  return null;
}

async function saveCriterion({ draft, criterion }: SaveCriterionVariables): Promise<Criterion> {
  const validationError = validate(draft);
  if (validationError) throw new Error(validationError);

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

export interface SaveCriterionVariables {
  draft: CriterionDraft;
  /** The criterion being edited, or null to create one. */
  criterion: Criterion | null;
}

/** The edit dialog's save: create, or update details and applicability as they changed. */
export const useSaveCriterion = () =>
  useMutation({
    mutationFn: saveCriterion,
    meta: {
      successMessage: (_data, variables) =>
        (variables as SaveCriterionVariables).criterion ? "Criterion updated" : "Criterion created",
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
