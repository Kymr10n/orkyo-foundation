import { Badge } from "@foundation/src/components/ui/badge";
import { Button } from "@foundation/src/components/ui/button";
import {
    Dialog,
    DialogContent,
    DialogDescription,
    DialogHeader,
    DialogTitle,
} from "@foundation/src/components/ui/dialog";
import { ErrorAlert } from "@foundation/src/components/ui/ErrorAlert";
import { DialogFormFooter } from "@foundation/src/components/ui/DialogFormFooter";
import { Input } from "@foundation/src/components/ui/input";
import { Label } from "@foundation/src/components/ui/label";
import { ScrollArea } from "@foundation/src/components/ui/scroll-area";
import {
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
} from "@foundation/src/components/ui/select";
import { Separator } from "@foundation/src/components/ui/separator";
import { Textarea } from "@foundation/src/components/ui/textarea";
import { getCriteria } from "@foundation/src/lib/api/criteria-api";
import { createTemplate, updateTemplate } from "@foundation/src/lib/api/template-api";
import { getDataTypeColor } from "@foundation/src/lib/utils";
import type { Criterion, CriterionValue } from "@foundation/src/types/criterion";
import type { Template } from "@foundation/src/types/templates";
import type { DurationUnit } from "@foundation/src/types/requests";
import { useQueryClient } from "@tanstack/react-query";
import { Plus, Trash2 } from "lucide-react";
import { useEffect, useState } from "react";
import { CriterionRequirementInput } from "../requests/CriterionRequirementInput";
import { useTemplateForm } from "@foundation/src/hooks/useTemplateForm";
import { logger } from "@foundation/src/lib/core/logger";

interface TemplateDialogBaseProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSuccess: () => void;
  /** null = create mode, a Template object = edit mode */
  template: Template | null;
  entityType?: 'request' | 'space' | 'group';
}

export function TemplateDialogBase({
  open,
  onOpenChange,
  onSuccess,
  template,
  entityType = 'request',
}: TemplateDialogBaseProps) {
  const isEditMode = template !== null;
  const queryClient = useQueryClient();

  const {
    state,
    setField,
    addRequirement,
    removeRequirement,
    updateRequirement,
    reset,
  } = useTemplateForm(template, open);

  const [availableCriteria, setAvailableCriteria] = useState<Criterion[]>([]);
  const [isLoadingCriteria, setIsLoadingCriteria] = useState(false);
  const [selectedCriterionId, setSelectedCriterionId] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Load criteria when dialog opens
  useEffect(() => {
    const loadCriteria = async () => {
      if (!open) return;

      setIsLoadingCriteria(true);
      try {
        const criteriaData = await getCriteria();
        setAvailableCriteria(criteriaData);
      } catch (err) {
        logger.error("Failed to load criteria:", err);
      } finally {
        setIsLoadingCriteria(false);
      }
    };

    loadCriteria();
  }, [open]);

  const handleAddRequirement = () => {
    if (!selectedCriterionId) return;

    const criterion = availableCriteria.find((c) => c.id === selectedCriterionId);
    if (!criterion) return;

    addRequirement(selectedCriterionId, criterion);
    setSelectedCriterionId("");
  };

  const handleRemoveRequirement = (criterionId: string) => {
    removeRequirement(criterionId);
  };

  const handleRequirementValueChange = (criterionId: string, value: CriterionValue | null) => {
    updateRequirement(criterionId, value);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);

    if (!state.name.trim()) {
      setError("Name is required");
      return;
    }

    const durationVal = parseInt(state.durationValue, 10);
    if (isNaN(durationVal) || durationVal < 1) {
      setError("Minimal duration must be a positive number");
      return;
    }

    try {
      setIsSubmitting(true);

      if (isEditMode) {
        await updateTemplate(template.id, {
          name: state.name.trim(),
          description: state.description.trim() || undefined,
          entityType: 'request',
          durationValue: durationVal,
          durationUnit: state.durationUnit,
          items: state.requirements.size > 0
            ? Array.from(state.requirements.entries()).map(([criterionId, value]) => ({
                id: `${template.id}-${criterionId}`,
                templateId: template.id,
                criterionId,
                value: String(value ?? ''),
              }))
            : undefined,
        });
        queryClient.invalidateQueries({ queryKey: ["request-templates"] });
      } else {
        await createTemplate({
          name: state.name.trim(),
          description: state.description.trim() || undefined,
          entityType,
          durationValue: durationVal,
          durationUnit: state.durationUnit,
        });
        queryClient.invalidateQueries({ queryKey: [`templates-${entityType}`] });
        reset();
        setSelectedCriterionId("");
      }

      setError(null);
      onSuccess();
    } catch (err) {
      setError(err instanceof Error ? err.message : `Failed to ${isEditMode ? 'update' : 'create'} template`);
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleOpenChange = (newOpen: boolean) => {
    if (!newOpen && !isSubmitting) {
      reset();
      setSelectedCriterionId("");
      setError(null);
    }
    onOpenChange(newOpen);
  };

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent className="max-w-2xl max-h-[90vh] flex flex-col p-0">
        <DialogHeader className="px-6 pt-6 pb-4">
          <DialogTitle>
            {isEditMode ? "Edit Request Template" : "Create Request Template"}
          </DialogTitle>
          <DialogDescription className="sr-only">
            {isEditMode ? "Edit an existing request template" : "Create a new request template"}
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="flex flex-col flex-1 overflow-hidden">
          <ScrollArea className="flex-1 px-6">
            <div className="space-y-6 pb-6">
              {/* Name */}
              <div className="space-y-2">
                <Label htmlFor="name">
                  Name <span className="text-destructive">*</span>
                </Label>
                <Input
                  id="name"
                  value={state.name}
                  onChange={(e) => setField('name', e.target.value)}
                  placeholder="e.g., Standard Week, Long Project"
                  disabled={isSubmitting}
                  autoFocus
                />
              </div>

              {/* Description */}
              <div className="space-y-2">
                <Label htmlFor="description">Description</Label>
                <Textarea
                  id="description"
                  value={state.description}
                  onChange={(e) => setField('description', e.target.value)}
                  placeholder="Optional description..."
                  rows={3}
                  disabled={isSubmitting}
                />
              </div>

              {/* Minimal Duration */}
              <div className="space-y-2">
                <Label>
                  Minimal Duration <span className="text-destructive">*</span>
                </Label>
                <div className="flex gap-2">
                  <Input
                    type="text"
                    inputMode="numeric"
                    pattern="[0-9]*"
                    value={state.durationValue}
                    onChange={(e) => setField('durationValue', e.target.value.replace(/[^0-9]/g, ''))}
                    placeholder="1"
                    disabled={isSubmitting}
                    className="w-24"
                  />
                  <Select
                    value={state.durationUnit}
                    onValueChange={(value) => setField('durationUnit', value as DurationUnit)}
                    disabled={isSubmitting}
                  >
                    <SelectTrigger className="flex-1">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="minutes">Minutes</SelectItem>
                      <SelectItem value="hours">Hours</SelectItem>
                      <SelectItem value="days">Days</SelectItem>
                      <SelectItem value="weeks">Weeks</SelectItem>
                      <SelectItem value="months">Months</SelectItem>
                      <SelectItem value="years">Years</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
              </div>

              {/* Criteria */}
              <div className="space-y-4">
                <div className="flex items-center justify-between">
                  <h3 className="text-sm font-medium">Criteria</h3>
                  <Badge variant="outline" className="text-xs">
                    {state.requirements.size} active
                  </Badge>
                </div>

                {/* Add Criterion */}
                {availableCriteria.filter((c) => !state.requirements.has(c.id)).length > 0 && (
                  <div className="flex gap-2">
                    <Select
                      value={selectedCriterionId}
                      onValueChange={setSelectedCriterionId}
                      disabled={isLoadingCriteria || isSubmitting}
                    >
                      <SelectTrigger className="flex-1">
                        <SelectValue placeholder="Select a criterion to add" />
                      </SelectTrigger>
                      <SelectContent>
                        {availableCriteria
                          .filter((c) => !state.requirements.has(c.id))
                          .map((criterion) => (
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
                      onClick={handleAddRequirement}
                      disabled={!selectedCriterionId || isSubmitting}
                      size="sm"
                    >
                      <Plus className="h-4 w-4" />
                    </Button>
                  </div>
                )}

                {/* Active Criteria */}
                {state.requirements.size === 0 ? (
                  <div className="text-center py-8 text-sm text-muted-foreground border rounded-lg border-dashed">
                    No criteria added yet. Add criteria to define values for this template.
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
                                handleRequirementValueChange(criterionId, newValue)
                              }
                            />
                          </div>
                          <Button
                            type="button"
                            variant="ghost"
                            size="sm"
                            onClick={() => handleRemoveRequirement(criterionId)}
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

              {/* Error Message */}
              <ErrorAlert message={error} />
            </div>
          </ScrollArea>

          <Separator />
          <DialogFormFooter
            onCancel={() => handleOpenChange(false)}
            isSubmitting={isSubmitting}
            submitLabel={isEditMode ? "Save Changes" : "Create Template"}
            submittingLabel={isEditMode ? undefined : "Creating..."}
            className="px-6 py-4"
          />
        </form>
      </DialogContent>
    </Dialog>
  );
}
