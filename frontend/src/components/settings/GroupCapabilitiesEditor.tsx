import { useState } from "react";
import type { Criterion, CriterionValue } from "@foundation/src/types/criterion";
import { logger } from "@foundation/src/lib/core/logger";
import { CriterionAssignmentEditor } from "../capabilities/CriterionAssignmentEditor";
import {
  useGroupCapabilitiesData,
  useSaveGroupCapabilities,
} from "@foundation/src/hooks/useGroupCapabilities";
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
  const [saveError, setSaveError] = useState<string | null>(null);

  const { criteria, initialAssignments, isLoading, loadError } = useGroupCapabilitiesData(groupId, open);

  const saveMutation = useSaveGroupCapabilities(groupId, {
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
