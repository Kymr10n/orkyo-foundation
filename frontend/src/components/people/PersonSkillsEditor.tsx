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
} from '@foundation/src/components/ui/dialog';
import { ScrollArea } from '@foundation/src/components/ui/scroll-area';
import { Separator } from '@foundation/src/components/ui/separator';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@foundation/src/components/ui/select';
import { getCriteria } from '@foundation/src/lib/api/criteria-api';
import {
  deleteResourceCapability,
  getResourceCapabilities,
  upsertResourceCapability,
} from '@foundation/src/lib/api/resource-capabilities-api';
import { getDataTypeColor } from '@foundation/src/lib/utils';
import type { Criterion, CriterionValue } from '@foundation/src/types/criterion';
import { Plus, Sparkles, Trash2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { CriterionRequirementInput } from '../requests/CriterionRequirementInput';
import { CreateCriterionDialog } from '../settings/CreateCriterionDialog';
import { logger } from '@foundation/src/lib/core/logger';

interface PersonSkillsEditorProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  resourceId: string;
  personName: string;
}

/**
 * Per-person skill (criterion) assignment editor. Mirrors SpaceCapabilitiesEditor
 * but talks to the generic /api/resources/{id}/capabilities endpoints and
 * filters the criterion catalog to person-applicable criteria.
 */
export function PersonSkillsEditor({
  open,
  onOpenChange,
  resourceId,
  personName,
}: PersonSkillsEditorProps) {
  const [assignments, setAssignments] = useState(new Map<string, CriterionValue | null>());
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [selectedCriterionId, setSelectedCriterionId] = useState('');
  const [saveError, setSaveError] = useState<string | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const queryClient = useQueryClient();

  const { data: caps, isLoading: capsLoading, error: capsError } = useQuery({
    queryKey: ['resource-capabilities', resourceId],
    queryFn: () => getResourceCapabilities(resourceId),
    enabled: open,
  });

  const { data: availableCriteria = [], isLoading: criteriaLoading } = useQuery({
    queryKey: ['criteria', { resourceType: 'person' }],
    queryFn: () => getCriteria({ resourceType: 'person' }),
    enabled: open,
  });

  const isLoading = capsLoading || criteriaLoading;
  const error = capsError ? (capsError instanceof Error ? capsError.message : 'Failed to load skills') : null;

  // Sync server capabilities into local editing state when they arrive
  useEffect(() => {
    if (!caps) return;
    const map = new Map<string, CriterionValue | null>();
    for (const cap of caps) map.set(cap.criterionId, cap.value);
    setAssignments(map);
  }, [caps]);

  const handleAdd = () => {
    if (!selectedCriterionId) return;
    const criterion = availableCriteria.find((c) => c.id === selectedCriterionId);
    if (!criterion) return;
    const next = new Map(assignments);
    next.set(selectedCriterionId, criterion.dataType === 'Boolean' ? false : null);
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

  const handleSave = async () => {
    setIsSubmitting(true);
    setSaveError(null);
    try {
      const existing = await getResourceCapabilities(resourceId);
      const existingByCriterion = new Map(existing.map((c) => [c.criterionId, c.id]));

      const upserts: Promise<unknown>[] = [];
      const deletes: Promise<unknown>[] = [];

      assignments.forEach((value, criterionId) => {
        if (value === null) return;
        upserts.push(upsertResourceCapability(resourceId, { criterionId, value }));
      });

      existingByCriterion.forEach((capabilityId, criterionId) => {
        if (!assignments.has(criterionId)) {
          deletes.push(deleteResourceCapability(resourceId, capabilityId));
        }
      });

      await Promise.all([...upserts, ...deletes]);
      onOpenChange(false);
    } catch (err) {
      logger.error('Failed to save person skills:', err);
      setSaveError(err instanceof Error ? err.message : 'Failed to save skills');
    } finally {
      setIsSubmitting(false);
    }
  };

  const selectableCriteria = availableCriteria.filter((c) => !assignments.has(c.id));

  const handleCriterionCreated = async (criterion: Criterion) => {
    // Refresh the person-applicable criteria list so the newly created one
    // appears in the select. Pre-select it as a UX shortcut.
    await queryClient.invalidateQueries({ queryKey: ['criteria', { resourceType: 'person' }] });
    setSelectedCriterionId(criterion.id);
    setCreateOpen(false);
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-2xl max-h-[90vh] flex flex-col p-0">
        <DialogHeader className="px-6 pt-6 pb-4">
          <DialogTitle>Skills for {personName}</DialogTitle>
          <DialogDescription className="sr-only">
            Manage skill assignments for this person.
          </DialogDescription>
        </DialogHeader>

        <ScrollArea className="flex-1 px-6">
          <div className="space-y-6 pb-6">
            <p className="text-sm text-muted-foreground">
              Assign criterion values describing this person's skills and qualifications.
            </p>

            <Separator />

            <div className="space-y-4">
              <div className="flex items-center justify-between">
                <h3 className="text-sm font-medium">Skills</h3>
                <Badge variant="outline" className="text-xs">{assignments.size} active</Badge>
              </div>

              <div className="flex gap-2">
                {selectableCriteria.length > 0 ? (
                  <>
                    <Select
                      value={selectedCriterionId}
                      onValueChange={setSelectedCriterionId}
                      disabled={isLoading || isSubmitting}
                    >
                      <SelectTrigger className="flex-1">
                        <SelectValue placeholder="Select a skill to add" />
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
                      disabled={!selectedCriterionId || isSubmitting}
                      size="sm"
                    >
                      <Plus className="h-4 w-4" />
                    </Button>
                  </>
                ) : (
                  <p className="flex-1 text-xs text-muted-foreground py-2">
                    All person-applicable criteria are already assigned, or none exist yet.
                  </p>
                )}
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() => setCreateOpen(true)}
                  disabled={isSubmitting}
                >
                  <Sparkles className="h-4 w-4 mr-2" />
                  Add Skill Criterion
                </Button>
              </div>

              {assignments.size === 0 ? (
                <div className="text-center py-8 text-sm text-muted-foreground border rounded-lg border-dashed">
                  No skills assigned yet.
                </div>
              ) : (
                <div className="space-y-4 border rounded-lg p-4">
                  {Array.from(assignments.entries()).map(([criterionId, value]) => {
                    const criterion = availableCriteria.find((c) => c.id === criterionId);
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
                          disabled={isSubmitting}
                        >
                          <Trash2 className="h-4 w-4 text-destructive" />
                        </Button>
                      </div>
                    );
                  })}
                </div>
              )}
            </div>

            <ErrorAlert message={error ?? saveError ?? null} />
          </div>
        </ScrollArea>

        <Separator />
        <DialogFooter className="px-6 py-4">
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={isSubmitting}>
            Cancel
          </Button>
          <Button onClick={handleSave} disabled={isSubmitting}>
            {isSubmitting ? 'Saving...' : 'Save Changes'}
          </Button>
        </DialogFooter>
      </DialogContent>

      <CreateCriterionDialog
        open={createOpen}
        onOpenChange={setCreateOpen}
        onSuccess={handleCriterionCreated}
        defaultResourceType="person"
      />
    </Dialog>
  );
}
