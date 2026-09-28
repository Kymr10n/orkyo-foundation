import type { useRequestForm, RequirementEntry } from "@foundation/src/hooks/useRequestForm";
import type { Criterion } from "@foundation/src/types/criterion";
import type { Conflict } from "@foundation/src/types/requests";
import { CriterionRequirementInput } from "./CriterionRequirementInput";
import { CriterionRequirementList } from "./CriterionRequirementList";
import { ConflictIndicator } from "./ConflictIndicator";

interface RequestRequirementsSectionProps {
  state: ReturnType<typeof useRequestForm>['state'];
  availableCriteria: Criterion[];
  /** Resource-type keys a requirement can be asked of: the request's targets plus people. */
  requirementTypeKeys: ReadonlySet<string>;
  selectedCriterionId: string;
  setSelectedCriterionId: (id: string) => void;
  isLoading: boolean;
  /** Saved conflicts keyed by the criterion they're about — flags the matching requirement row. */
  conflictsByCriterionId?: Map<string, Conflict[]>;
  /** View mode: disable inputs and hide the add/remove controls (values still shown). */
  readOnly?: boolean;
  onAddRequirement: () => void;
  onRemoveRequirement: (criterionId: string) => void;
  onRequirementChange: (criterionId: string, patch: Partial<RequirementEntry>) => void;
}

export function RequestRequirementsSection({
  state,
  availableCriteria,
  requirementTypeKeys,
  selectedCriterionId,
  setSelectedCriterionId,
  isLoading,
  conflictsByCriterionId,
  readOnly = false,
  onAddRequirement,
  onRemoveRequirement,
  onRequirementChange,
}: RequestRequirementsSectionProps) {
  // Offer only criteria some resource on this request can carry: a mill's tolerance is
  // demanded of the mill, never of the person, so a criterion no target type applies to
  // could never be satisfied. Rows below still render from the unfiltered list, so a
  // requirement added before its type was unticked stays visible.
  const unusedCriteria = availableCriteria.filter(
    (c) => !state.requirements.has(c.id) && c.resourceTypeKeys.some((k) => requirementTypeKeys.has(k))
  );

  return (
    <CriterionRequirementList
      title="Requirements"
      addLabel="Add requirement"
      emptyMessage="No requirements added yet. Add criteria to specify requirements."
      unusedCriteria={unusedCriteria}
      selectedCriterionId={selectedCriterionId}
      onSelectCriterion={setSelectedCriterionId}
      onAdd={onAddRequirement}
      availableCriteria={availableCriteria}
      requirements={state.requirements}
      renderLeading={(criterion) => (
        <ConflictIndicator conflicts={conflictsByCriterionId?.get(criterion.id) ?? []} className="mt-9" />
      )}
      renderInput={(criterion, entry) => (
        <fieldset disabled={readOnly} className="min-w-0 border-0 p-0 m-0">
          <CriterionRequirementInput
            criterion={criterion}
            value={entry.value}
            operator={entry.operator}
            onChange={(newValue) => onRequirementChange(criterion.id, { value: newValue })}
            onOperatorChange={(newOperator) => onRequirementChange(criterion.id, { operator: newOperator })}
          />
        </fieldset>
      )}
      onRemove={onRemoveRequirement}
      disabled={isLoading}
      readOnly={readOnly}
    />
  );
}
