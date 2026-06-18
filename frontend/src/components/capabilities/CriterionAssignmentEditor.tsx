import { type ReactNode, useEffect, useState } from 'react';
import { Badge } from '@foundation/src/components/ui/badge';
import { Button } from '@foundation/src/components/ui/button';
import { ErrorAlert } from '@foundation/src/components/ui/ErrorAlert';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  ScrollableDialogBody,
} from '@foundation/src/components/ui/dialog';
import { Separator } from '@foundation/src/components/ui/separator';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@foundation/src/components/ui/select';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';
import { getDataTypeColor } from '@foundation/src/lib/utils';
import type { Criterion, CriterionValue } from '@foundation/src/types/criterion';
import { Plus, Trash2 } from 'lucide-react';
import { CriterionRequirementInput } from '../requests/CriterionRequirementInput';

export interface CriterionAssignmentLabels {
  /** Dialog title, e.g. "Skills for Ada" or 'Group Capabilities: "Team A"'. */
  title: ReactNode;
  /** Screen-reader-only dialog description. */
  srDescription: string;
  /** Optional intro paragraph shown under the header. */
  intro?: ReactNode;
  /** Section heading + the active-count noun context, e.g. "Skills" / "Capabilities". */
  sectionLabel: string;
  /** Placeholder for the add-criterion select. */
  selectPlaceholder: string;
  /** Empty-state text when nothing is assigned. */
  emptyText: string;
  /** Optional message shown in place of the select when every criterion is already assigned. */
  selectableEmptyText?: string;
}

export interface CriterionAssignmentEditorProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** The criterion catalog to choose from. */
  criteria: Criterion[];
  /** Loading state for criteria/existing assignments. */
  isLoading: boolean;
  /** Error loading data (shown inline). */
  loadError: string | null;
  /** Error from the save attempt (shown inline). */
  saveError: string | null;
  isSaving: boolean;
  /** The persisted assignments to seed the editor with (memoize in the caller). */
  initialAssignments: Map<string, CriterionValue | null>;
  /** Called with the desired assignments when Save is clicked. */
  onSave: (desired: Map<string, CriterionValue | null>) => void;
  labels: CriterionAssignmentLabels;
  /** Default value for a newly-added criterion. Defaults to false for Boolean, else null. */
  defaultValueFor?: (criterion: Criterion) => CriterionValue | null;
  /** Controlled selection (optional); uncontrolled internal state by default. */
  selectedCriterionId?: string;
  onSelectedCriterionIdChange?: (id: string) => void;
  /** Optional control rendered beside the add row (e.g. a "create criterion" button). */
  addSlot?: ReactNode;
}

const defaultValueForCriterion = (criterion: Criterion): CriterionValue | null =>
  criterion.dataType === 'Boolean' ? false : null;

/**
 * Shared editor for assigning criterion values to a resource (person skills,
 * space/group capabilities). Owns the editing state and UI; callers supply the
 * data, labels, and a save handler. See docs/dialog-feedback.md for the save
 * conventions used by the thin wrappers.
 */
export function CriterionAssignmentEditor({
  open,
  onOpenChange,
  criteria,
  isLoading,
  loadError,
  saveError,
  isSaving,
  initialAssignments,
  onSave,
  labels,
  defaultValueFor = defaultValueForCriterion,
  selectedCriterionId: controlledSelectedId,
  onSelectedCriterionIdChange,
  addSlot,
}: CriterionAssignmentEditorProps) {
  const canEdit = useCanEdit();
  const [assignments, setAssignments] = useState<Map<string, CriterionValue | null>>(
    new Map(initialAssignments),
  );
  const [internalSelectedId, setInternalSelectedId] = useState('');

  const selectedCriterionId = controlledSelectedId ?? internalSelectedId;
  const setSelectedCriterionId = (id: string) => {
    onSelectedCriterionIdChange?.(id);
    if (controlledSelectedId === undefined) setInternalSelectedId(id);
  };

  // Reseed the working copy whenever the persisted assignments arrive/change.
  useEffect(() => {
    setAssignments(new Map(initialAssignments));
  }, [initialAssignments]);

  const handleAdd = () => {
    if (!selectedCriterionId) return;
    const criterion = criteria.find((c) => c.id === selectedCriterionId);
    if (!criterion) return;
    const next = new Map(assignments);
    next.set(selectedCriterionId, defaultValueFor(criterion));
    setAssignments(next);
    setSelectedCriterionId('');
  };

  const handleRemove = (criterionId: string) => {
    const next = new Map(assignments);
    next.delete(criterionId);
    setAssignments(next);
  };

  const handleValueChange = (criterionId: string, value: CriterionValue | null) => {
    const next = new Map(assignments);
    next.set(criterionId, value);
    setAssignments(next);
  };

  const selectableCriteria = criteria.filter((c) => !assignments.has(c.id));
  const showAddRow = selectableCriteria.length > 0 || !!labels.selectableEmptyText || !!addSlot;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-2xl max-h-[90vh] flex flex-col p-0">
        <DialogHeader className="px-6 pt-6 pb-4">
          <DialogTitle>{labels.title}</DialogTitle>
          <DialogDescription className="sr-only">{labels.srDescription}</DialogDescription>
        </DialogHeader>

        <ScrollableDialogBody className="px-6">
          <div className="space-y-6 pb-6">
            {labels.intro && (
              <p className="text-sm text-muted-foreground">{labels.intro}</p>
            )}

            <Separator />

            <div className="space-y-4">
              <div className="flex items-center justify-between">
                <h3 className="text-sm font-medium">{labels.sectionLabel}</h3>
                <Badge variant="outline" className="text-xs">{assignments.size} active</Badge>
              </div>

              {showAddRow && (
                <div className="flex gap-2">
                  {selectableCriteria.length > 0 ? (
                    <>
                      <Select
                        value={selectedCriterionId}
                        onValueChange={setSelectedCriterionId}
                        disabled={isLoading || isSaving}
                      >
                        <SelectTrigger className="flex-1">
                          <SelectValue placeholder={labels.selectPlaceholder} />
                        </SelectTrigger>
                        <SelectContent>
                          {selectableCriteria.map((criterion) => (
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
                        onClick={handleAdd}
                        disabled={!selectedCriterionId || isSaving}
                        size="sm"
                      >
                        <Plus className="h-4 w-4" />
                      </Button>
                    </>
                  ) : (
                    labels.selectableEmptyText && (
                      <p className="flex-1 text-xs text-muted-foreground py-2">
                        {labels.selectableEmptyText}
                      </p>
                    )
                  )}
                  {addSlot}
                </div>
              )}

              {assignments.size === 0 ? (
                <div className="text-center py-8 text-sm text-muted-foreground border rounded-lg border-dashed">
                  {labels.emptyText}
                </div>
              ) : (
                <div className="space-y-4 border rounded-lg p-4">
                  {Array.from(assignments.entries()).map(([criterionId, value]) => {
                    const criterion = criteria.find((c) => c.id === criterionId);
                    if (!criterion) return null;
                    return (
                      <div key={criterionId} className="flex gap-3">
                        <div className="flex-1">
                          <CriterionRequirementInput
                            criterion={criterion}
                            value={value}
                            onChange={(newValue) => handleValueChange(criterionId, newValue)}
                          />
                        </div>
                        <Button
                          type="button"
                          variant="ghost"
                          size="sm"
                          onClick={() => handleRemove(criterionId)}
                          className="mt-7"
                          disabled={isSaving}
                        >
                          <Trash2 className="h-4 w-4 text-destructive" />
                        </Button>
                      </div>
                    );
                  })}
                </div>
              )}
            </div>

            <ErrorAlert message={loadError ?? saveError ?? null} />
          </div>
        </ScrollableDialogBody>

        <Separator />
        <DialogFooter className="px-6 py-4">
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={isSaving}>
            Cancel
          </Button>
          <Button onClick={() => onSave(assignments)} disabled={isSaving || !canEdit}>
            {isSaving ? 'Saving...' : 'Save Changes'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
