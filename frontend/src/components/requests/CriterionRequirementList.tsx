import { useMemo, type ReactNode } from "react";
import { Badge } from "@foundation/src/components/ui/badge";
import { Button } from "@foundation/src/components/ui/button";
import { EmptyState } from "@foundation/src/components/ui/EmptyState";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@foundation/src/components/ui/select";
import { getDataTypeColor } from "@foundation/src/lib/utils";
import type { Criterion } from "@foundation/src/types/criterion";
import { Plus, Trash2 } from "lucide-react";

interface CriterionRequirementListProps<TValue> {
  /** "Requirements" on a request, "Criteria" on a template. */
  title: string;
  /** Names the "+" button for a screen reader, e.g. "Add requirement". */
  addLabel: string;
  emptyMessage: string;
  /** The criteria still offered in the picker. */
  unusedCriteria: Criterion[];
  selectedCriterionId: string;
  onSelectCriterion: (id: string) => void;
  onAdd: () => void;
  /** Every criterion a requirement can name. A requirement whose criterion is not here yet (the list still loads) renders no row. */
  availableCriteria: Criterion[];
  /** The chosen criteria and their values, keyed by criterion id. */
  requirements: ReadonlyMap<string, TValue>;
  /** The value input of one chosen criterion. */
  renderInput: (criterion: Criterion, value: TValue) => ReactNode;
  /** Anything shown before the input, e.g. a conflict flag. */
  renderLeading?: (criterion: Criterion, value: TValue) => ReactNode;
  onRemove: (criterionId: string) => void;
  /** Blocks the picker and the buttons, e.g. while criteria load or a save runs. */
  disabled?: boolean;
  /** View mode: no picker and no remove buttons; the rows still show their values. */
  readOnly?: boolean;
}

/**
 * The pick-a-criterion-then-set-its-value list that a request and a template share. The caller
 * owns the requirements' state and decides which criteria are still on offer.
 */
export function CriterionRequirementList<TValue>({
  title,
  addLabel,
  emptyMessage,
  unusedCriteria,
  selectedCriterionId,
  onSelectCriterion,
  onAdd,
  availableCriteria,
  requirements,
  renderInput,
  renderLeading,
  onRemove,
  disabled = false,
  readOnly = false,
}: CriterionRequirementListProps<TValue>) {
  const count = requirements.size;
  const criteriaById = useMemo(
    () => new Map(availableCriteria.map((c) => [c.id, c])),
    [availableCriteria],
  );
  return (
    <div>
      <div className="flex items-center gap-2">
        <h4 className="text-sm font-medium">{title}</h4>
        <Badge variant="outline" className="text-xs">
          {count} active
        </Badge>
      </div>
      <div className="space-y-4 pt-4">
        {!readOnly && unusedCriteria.length > 0 && (
          <div className="flex gap-2">
            <Select value={selectedCriterionId} onValueChange={onSelectCriterion} disabled={disabled}>
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
              onClick={onAdd}
              disabled={!selectedCriterionId || disabled}
              size="sm"
              aria-label={addLabel}
            >
              <Plus className="h-4 w-4" />
            </Button>
          </div>
        )}

        {count === 0 ? (
          <EmptyState message={emptyMessage} className="text-sm border rounded-lg border-dashed" />
        ) : (
          <div className="space-y-4 border rounded-lg p-4">
            {Array.from(requirements, ([criterionId, value]) => {
              const criterion = criteriaById.get(criterionId);
              if (!criterion) return null;
              return (
                <div key={criterion.id} className="flex gap-3">
                  {renderLeading?.(criterion, value)}
                  <div className="flex-1 min-w-0">{renderInput(criterion, value)}</div>
                  {!readOnly && (
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      onClick={() => onRemove(criterion.id)}
                      className="mt-7"
                      disabled={disabled}
                      aria-label={`Remove ${criterion.name}`}
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
