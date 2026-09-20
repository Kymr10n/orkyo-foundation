import { useEffect, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { getCriteria } from "@foundation/src/lib/api/criteria-api";
import {
  addGroupCapability,
  deleteGroupCapability,
  getGroupCapabilities,
} from "@foundation/src/lib/api/group-capability-api";
import type { Criterion, CriterionValue } from "@foundation/src/types/criterion";
import { logger } from "@foundation/src/lib/core/logger";
import { diffCapabilityAssignments } from "@foundation/src/components/capabilities/capability-diff";

/**
 * The group-capability editor's data.
 *
 * Deliberately not a `useQuery`: the backend POST is insert-only, so the editor must read
 * the rows as they stand each time it opens — a cached list would offer values that are no
 * longer there. The fetch therefore stays imperative, keyed to the dialog opening.
 */
export function useGroupCapabilitiesData(groupId: string, open: boolean) {
  const [criteria, setCriteria] = useState<Criterion[]>([]);
  const [initialAssignments, setInitialAssignments] = useState<Map<string, CriterionValue | null>>(new Map());
  const [isLoading, setIsLoading] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);

  useEffect(() => {
    const loadData = async () => {
      if (!open) return;
      setIsLoading(true);
      setLoadError(null);
      try {
        const criteriaData = await getCriteria();
        setCriteria(criteriaData);
        const existing = await getGroupCapabilities(groupId);
        const map = new Map<string, CriterionValue | null>();
        existing.forEach((cap) => map.set(cap.criterionId, cap.value));
        setInitialAssignments(map);
      } catch (err) {
        logger.error("Failed to load criteria:", err);
        setLoadError(err instanceof Error ? err.message : "Failed to load data");
      } finally {
        setIsLoading(false);
      }
    };

    loadData();
  }, [open, groupId]);

  return { criteria, initialAssignments, isLoading, loadError };
}

/** Persists the desired assignments: 'add-new' mode, because the backend POST is insert-only. */
export const useSaveGroupCapabilities = (
  groupId: string,
  options: { onSuccess: () => void; onError: (err: Error) => void },
) =>
  useMutation({
    mutationFn: async (desired: Map<string, CriterionValue | null>) => {
      const existing = await getGroupCapabilities(groupId);
      const { toPersist, toDeleteIds } = diffCapabilityAssignments(existing, desired, "add-new");
      await Promise.all([
        ...toPersist.map((cap) => addGroupCapability(groupId, cap)),
        ...toDeleteIds.map((id) => deleteGroupCapability(groupId, id)),
      ]);
    },
    meta: {
      successMessage: "Capabilities saved",
      suppressErrorToast: true,
    },
    ...options,
  });
