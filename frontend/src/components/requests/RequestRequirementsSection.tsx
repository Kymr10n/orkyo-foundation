/* eslint-disable orkyo/ui-primitives -- F3 (2026-09 review): 1 legacy hand-rolled empty/loading site; converge on touch, then drop this line. */
import { Badge } from "@foundation/src/components/ui/badge";
import { Button } from "@foundation/src/components/ui/button";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@foundation/src/components/ui/select";
import { getDataTypeColor } from "@foundation/src/lib/utils";
import type { useRequestForm, RequirementEntry } from "@foundation/src/hooks/useRequestForm";
import type { Criterion } from "@foundation/src/types/criterion";
import type { Conflict } from "@foundation/src/types/requests";
import { Plus, Trash2 } from "lucide-react";
import { CriterionRequirementInput } from "./CriterionRequirementInput";
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
    <div>
      <div className="flex items-center gap-2">
        <h4 className="text-sm font-medium">Requirements</h4>
        <Badge variant="outline" className="text-xs">
          {state.requirements.size} active
        </Badge>
      </div>
      <div className="space-y-4 pt-4">

          {/* Add Requirement */}
          {!readOnly && unusedCriteria.length > 0 && (
            <div className="flex gap-2">
              <Select
                value={selectedCriterionId}
                onValueChange={setSelectedCriterionId}
                disabled={isLoading}
              >
                <SelectTrigger className="flex-1">
                  <SelectValue placeholder="Select a criterion to add" />
                </SelectTrigger>
                <SelectContent>
                  {unusedCriteria.map((criterion) => (
                    <SelectItem key={criterion.id} value={criterion.id}>
                      <div className="flex items-center gap-2">
                        <span>{criterion.name}</span>
                        <Badge
                          variant="outline"
                          className={`text-xs ${getDataTypeColor(criterion.dataType)}`}
                        >
                          {criterion.dataType}
                        </Badge>
                      </div>
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <Button
                type="button"
                onClick={onAddRequirement}
                disabled={!selectedCriterionId}
                size="sm"
              >
                <Plus className="h-4 w-4" />
              </Button>
            </div>
          )}

          {/* Active Requirements */}
          {state.requirements.size === 0 ? (
            <div className="text-center py-8 text-sm text-muted-foreground border rounded-lg border-dashed">
              No requirements added yet. Add criteria to specify requirements.
            </div>
          ) : (
            <div className="space-y-4 border rounded-lg p-4">
              {Array.from(state.requirements.entries()).map(([criterionId, entry]) => {
                const criterion = availableCriteria.find((c) => c.id === criterionId);
                if (!criterion) return null;

                return (
                  <div key={criterionId} className="flex gap-3">
                    <ConflictIndicator
                      conflicts={conflictsByCriterionId?.get(criterionId) ?? []}
                      className="mt-9"
                    />
                    <fieldset disabled={readOnly} className="flex-1 min-w-0 border-0 p-0 m-0">
                      <CriterionRequirementInput
                        criterion={criterion}
                        value={entry.value}
                        operator={entry.operator}
                        onChange={(newValue) => onRequirementChange(criterionId, { value: newValue })}
                        onOperatorChange={(newOperator) => onRequirementChange(criterionId, { operator: newOperator })}
                      />
                    </fieldset>
                    {!readOnly && (
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        onClick={() => onRemoveRequirement(criterionId)}
                        className="mt-7"
                      >
                        <Trash2 className="h-4 w-4 text-destructive" />
                      </Button>
                    )}
                  </div>
                );
              })}
            </div>
          )}
      </div>
    </div>
  );
}
