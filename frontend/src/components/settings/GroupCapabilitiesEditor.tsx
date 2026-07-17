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
import { CriterionAssignmentEditor } from "../capabilities/CriterionAssignmentEditor";
import { diffCapabilityAssignments } from "../capabilities/capability-diff";
import { errorMessage } from "@foundation/src/hooks/mutation-utils";

interface GroupCapabilitiesEditorProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  groupId: string;
  groupName: string;
  onSuccess?: () => void;
}

// Group capabilities seed concrete defaults (the backend is insert-only, so a new
// row must carry a value to persist).
const groupDefaultValue = (criterion: Criterion): CriterionValue | null => {
  switch (criterion.dataType) {
    case "Boolean":
      return false;
    case "Number":
      return 0;
    case "String":
      return "";
    case "Enum":
      return criterion.enumValues?.[0] || "";
    default:
      return "";
  }
};

/**
 * Group capability (criterion) assignment editor. Thin wrapper over the shared
 * CriterionAssignmentEditor; the backend POST is insert-only so saves use
 * 'add-new' mode (value edits to existing rows are backend-constrained).
 */
export function GroupCapabilitiesEditor({
  open,
  onOpenChange,
  groupId,
  groupName,
  onSuccess,
}: GroupCapabilitiesEditorProps) {
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

  const saveMutation = useMutation({
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
      errorMessage: "Failed to save group capabilities",
    },
    onSuccess: () => {
      setSaveError(null);
      onSuccess?.();
      onOpenChange(false);
    },
    onError: (err) => {
      logger.error("Failed to save group capabilities:", err);
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
      defaultValueFor={groupDefaultValue}
      labels={{
        title: `Group Capabilities: "${groupName}"`,
        srDescription: "Manage capability assignments for this group.",
        intro: "Capabilities defined here will be inherited by all spaces in this group.",
        sectionLabel: "Capabilities",
        selectPlaceholder: "Select a criterion to add",
        emptyText: "No capabilities added yet. Add criteria to specify group-level capabilities.",
      }}
    />
  );
}
