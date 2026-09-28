import type { ReactNode } from "react";
import { Badge } from "@foundation/src/components/ui/badge";
import { Button } from "@foundation/src/components/ui/button";
import { EmptyState } from "@foundation/src/components/ui/EmptyState";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@foundation/src/components/ui/select";
import { getDataTypeColor } from "@foundation/src/lib/utils";
import type { Criterion } from "@foundation/src/types/criterion";
import { Plus, Trash2 } from "lucide-react";

/** One chosen criterion: its value input, and anything shown before it (e.g. a conflict flag). */
export interface CriterionRequirementRow {
  criterion: Criterion;
  input: ReactNode;
  leading?: ReactNode;
}

interface CriterionRequirementListProps {
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
  /** How many criteria are chosen. Can exceed `rows` while the criterion list still loads. */
  count: number;
  rows: CriterionRequirementRow[];
  onRemove: (criterionId: string) => void;
  /** Blocks the picker and the buttons, e.g. while criteria load or a save runs. */
  disabled?: boolean;
  /** View mode: no picker and no remove buttons; the rows still show their values. */
  readOnly?: boolean;
}

/**
 * The pick-a-criterion-then-set-its-value list that a request and a template share. The caller
 * owns the rows' state and decides which criteria are still on offer.
 */
export function CriterionRequirementList({
  title,
  addLabel,
  emptyMessage,
  unusedCriteria,
  selectedCriterionId,
  onSelectCriterion,
  onAdd,
  count,
  rows,
  onRemove,
  disabled = false,
  readOnly = false,
}: CriterionRequirementListProps) {
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
            {rows.map(({ criterion, input, leading }) => (
              <div key={criterion.id} className="flex gap-3">
                {leading}
                <div className="flex-1 min-w-0">{input}</div>
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
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
