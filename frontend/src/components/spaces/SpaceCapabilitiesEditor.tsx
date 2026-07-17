import { useEffect, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { getCriteria } from "@foundation/src/lib/api/criteria-api";
import {
  addSpaceCapability,
  deleteSpaceCapability,
  getSpaceCapabilities,
} from "@foundation/src/lib/api/space-capability-api";
import type { Criterion, CriterionValue } from "@foundation/src/types/criterion";
import { logger } from "@foundation/src/lib/core/logger";
import { CriterionAssignmentEditor } from "../capabilities/CriterionAssignmentEditor";
import { diffCapabilityAssignments } from "../capabilities/capability-diff";
import { errorMessage } from "@foundation/src/hooks/mutation-utils";

interface SpaceCapabilitiesEditorProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  siteId: string;
  resourceId: string;
  spaceName: string;
}

/**
 * Per-space capability (criterion) assignment editor. Thin wrapper over the
 * shared CriterionAssignmentEditor.
 */
export function SpaceCapabilitiesEditor({
  open,
  onOpenChange,
  siteId,
  resourceId,
  spaceName,
}: SpaceCapabilitiesEditorProps) {
  const [criteria, setCriteria] = useState<Criterion[]>([]);
  const [initialAssignments, setInitialAssignments] = useState<Map<string, CriterionValue | null>>(new Map());
  const [isLoading, setIsLoading] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);

  // Load data when dialog opens.
  useEffect(() => {
    const loadData = async () => {
      if (!open) return;
      setIsLoading(true);
      setLoadError(null);
      try {
        const [capsData, criteriaData] = await Promise.all([
          getSpaceCapabilities(siteId, resourceId),
          getCriteria(),
        ]);
        const map = new Map<string, CriterionValue | null>();
        capsData.forEach((cap) => map.set(cap.criterionId, cap.value));
        setInitialAssignments(map);
        setCriteria(criteriaData);
      } catch (err) {
        logger.error("Failed to load data:", err);
        setLoadError(err instanceof Error ? err.message : "Failed to load capabilities");
      } finally {
        setIsLoading(false);
      }
    };

    loadData();
  }, [open, siteId, resourceId]);

  const saveMutation = useMutation({
    mutationFn: async (desired: Map<string, CriterionValue | null>) => {
      const existing = await getSpaceCapabilities(siteId, resourceId);
      // 'add-new': the space backend POST upserts, but we preserve the historical
      // add-new behavior here; see docs/dialog-feedback.md and capability-diff.ts.
      const { toPersist, toDeleteIds } = diffCapabilityAssignments(existing, desired, "add-new");
      await Promise.all([
        ...toPersist.map((cap) => addSpaceCapability(siteId, resourceId, cap)),
        ...toDeleteIds.map((id) => deleteSpaceCapability(siteId, resourceId, id)),
      ]);
    },
    meta: {
      successMessage: "Capabilities saved",
      errorMessage: "Failed to save capabilities",
    },
    onSuccess: () => {
      setSaveError(null);
      onOpenChange(false);
    },
    onError: (err) => {
      logger.error("Failed to save capabilities:", err);
      setSaveError(errorMessage(err));
    },
  });

  return (
    <CriterionAssignmentEditor
      open={open}
      onOpenChange={onOpenChange}
      criteria={criteria}
      isLoading={isLoading}
      loadError={loadError}
      saveError={saveError}
      isSaving={saveMutation.isPending}
      initialAssignments={initialAssignments}
      onSave={(desired) => saveMutation.mutate(desired)}
      labels={{
        title: `Capabilities for ${spaceName}`,
        srDescription: "Manage capability assignments for this space.",
        intro: "Define criterion values that describe this space's characteristics",
        sectionLabel: "Capabilities",
        selectPlaceholder: "Select a criterion to add",
        emptyText: "No capabilities added yet. Add criteria to specify capabilities.",
      }}
    />
  );
}
