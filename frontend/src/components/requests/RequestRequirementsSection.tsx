import { Badge } from "@foundation/src/components/ui/badge";
import { Button } from "@foundation/src/components/ui/button";
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@foundation/src/components/ui/collapsible";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@foundation/src/components/ui/select";
import { getDataTypeColor } from "@foundation/src/lib/utils";
import type { useRequestForm } from "@foundation/src/hooks/useRequestForm";
import type { Criterion, CriterionValue } from "@foundation/src/types/criterion";
import { ChevronDown, Plus, Trash2 } from "lucide-react";
import { CriterionRequirementInput } from "./CriterionRequirementInput";

interface RequestRequirementsSectionProps {
  state: ReturnType<typeof useRequestForm>['state'];
  toggleSection: ReturnType<typeof useRequestForm>['toggleSection'];
  availableCriteria: Criterion[];
  selectedCriterionId: string;
  setSelectedCriterionId: (id: string) => void;
  isLoading: boolean;
  onAddRequirement: () => void;
  onRemoveRequirement: (criterionId: string) => void;
  onRequirementValueChange: (criterionId: string, value: CriterionValue | null) => void;
}

export function RequestRequirementsSection({
  state,
  toggleSection,
  availableCriteria,
  selectedCriterionId,
  setSelectedCriterionId,
  isLoading,
  onAddRequirement,
  onRemoveRequirement,
  onRequirementValueChange,
}: RequestRequirementsSectionProps) {
  const unusedCriteria = availableCriteria.filter(
    (c) => !state.requirements.has(c.id)
  );

  return (
    <Collapsible open={state.openSections.requirements} onOpenChange={() => toggleSection('requirements')}>
      <CollapsibleTrigger className="flex items-center justify-between w-full p-3 hover:bg-muted/50 rounded-lg">
        <div className="flex items-center gap-2">
          <h3 className="text-sm font-medium">Requirements</h3>
          <Badge variant="outline" className="text-xs">
            {state.requirements.size} active
          </Badge>
        </div>
        <ChevronDown className={`h-4 w-4 transition-transform ${state.openSections.requirements ? 'rotate-180' : ''}`} />
      </CollapsibleTrigger>
      <CollapsibleContent className="pt-4">
        <div className="space-y-4">

          {/* Add Requirement */}
          {unusedCriteria.length > 0 && (
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
              {Array.from(state.requirements.entries()).map(([criterionId, value]) => {
                const criterion = availableCriteria.find((c) => c.id === criterionId);
                if (!criterion) return null;

                return (
                  <div key={criterionId} className="flex gap-3">
                    <div className="flex-1">
                      <CriterionRequirementInput
                        criterion={criterion}
                        value={value}
                        onChange={(newValue) =>
                          onRequirementValueChange(criterionId, newValue)
                        }
                      />
                    </div>
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      onClick={() => onRemoveRequirement(criterionId)}
                      className="mt-7"
                    >
                      <Trash2 className="h-4 w-4 text-destructive" />
                    </Button>
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </CollapsibleContent>
    </Collapsible>
  );
}
