import type { CriterionValue } from '@foundation/src/types/criterion';

/**
 * How a capability/skill save reconciles the desired assignments against what
 * already exists on the server.
 * - `upsert`: persist every desired (non-null) value, creating or updating —
 *   used where the backend POST upserts (person resources, spaces).
 * - `add-new`: persist only criteria not already present — used where the
 *   backend POST is insert-only and rejects duplicates (resource groups).
 */
export type CapabilitySaveMode = 'upsert' | 'add-new';

export interface ExistingCapability {
  criterionId: string;
  /** The capability row id, used to delete. */
  id: string;
}

export interface CapabilityDiff {
  toPersist: { criterionId: string; value: CriterionValue }[];
  toDeleteIds: string[];
}

/**
 * Computes the create/update + delete sets needed to make the server's
 * capabilities match `desired`. Null desired values are skipped (an assignment
 * without a value is not persisted). Existing capabilities whose criterion is no
 * longer desired are deleted.
 */
export function diffCapabilityAssignments(
  existing: ExistingCapability[],
  desired: Map<string, CriterionValue | null>,
  mode: CapabilitySaveMode,
): CapabilityDiff {
  const existingByCriterion = new Map(existing.map((c) => [c.criterionId, c.id]));

  const toPersist: { criterionId: string; value: CriterionValue }[] = [];
  desired.forEach((value, criterionId) => {
    if (value === null) return;
    if (mode === 'add-new' && existingByCriterion.has(criterionId)) return;
    toPersist.push({ criterionId, value });
  });

  const toDeleteIds: string[] = [];
  existingByCriterion.forEach((id, criterionId) => {
    if (!desired.has(criterionId)) toDeleteIds.push(id);
  });

  return { toPersist, toDeleteIds };
}
