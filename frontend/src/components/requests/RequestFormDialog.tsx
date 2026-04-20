import { Button } from "@/components/ui/button";
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/components/ui/collapsible";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { ErrorAlert } from "@/components/ui/ErrorAlert";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Separator } from "@/components/ui/separator";
import { Textarea } from "@/components/ui/textarea";
import { getCriteria } from "@/lib/api/criteria-api";
import { getTemplates } from "@/lib/api/template-api";
import { type Template } from "@/types/templates";
import { getSpaces } from "@/lib/api/space-api";
import { useAppStore } from "@/store/app-store";
import { VALIDATION_MESSAGES, PLANNING_MODE_CONFIG } from "@/constants";
import { combineDateTimeToISO } from "@/lib/utils";
import type { Criterion, CriterionValue } from "@/types/criterion";
import type { Duration, DurationUnit, PlanningMode, Request } from "@/types/requests";
import type { Space } from "@/types/space";
import { ChevronDown, FileText, Layers } from "lucide-react";
import { useEffect, useState } from "react";
import { useRequestForm } from "@/hooks/useRequestForm";
import { Checkbox } from "@/components/ui/checkbox";
import { RequestScheduleSection } from "./RequestScheduleSection";
import { RequestConstraintsSection } from "./RequestConstraintsSection";
import { RequestRequirementsSection } from "./RequestRequirementsSection";
import { logger } from "@/lib/core/logger";

interface RequestFormDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  request?: Request | null;
  parentRequest?: Request | null;
  defaultPlanningMode?: PlanningMode;
  onSave: (data: RequestFormData) => void | Promise<void>;
}

export interface RequestFormData {
  name: string;
  description?: string;
  planningMode: PlanningMode;
  parentRequestId?: string;
  spaceId?: string;
  startTs?: string;
  endTs?: string;
  earliestStartTs?: string;
  latestEndTs?: string;
  duration: Duration;
  schedulingSettingsApply: boolean;
  requirements: {
    criterionId: string;
    value: CriterionValue;
  }[];
}

export function RequestFormDialog({
  open,
  onOpenChange,
  request,
  parentRequest,
  defaultPlanningMode,
  onSave,
}: RequestFormDialogProps) {
  const selectedSiteId = useAppStore((state) => state.selectedSiteId);
  const isCreateMode = !request;
  const isChildCreation = !request && !!parentRequest;
  
  // Use the custom hook for form state management
  const {
    state,
    setField,
    toggleSection,
    addRequirement,
    removeRequirement,
    updateRequirement,
    applyTemplate,
  } = useRequestForm(request, parentRequest?.id, defaultPlanningMode);

  // Additional state not managed by the form hook
  const [availableCriteria, setAvailableCriteria] = useState<Criterion[]>([]);
  const [availableTemplates, setAvailableTemplates] = useState<Template[]>([]);
  const [availableSpaces, setAvailableSpaces] = useState<Space[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [isSaving, setIsSaving] = useState(false);
  const [validationError, setValidationError] = useState<string | null>(null);
  const [selectedCriterionId, setSelectedCriterionId] = useState("");

  // Leaf: fully editable schedule.
  // Summary/Container: structural nodes; schedule is derived from children and not editable.
  const isLeaf = state.planningMode === 'leaf';
  const isSummary = state.planningMode === 'summary';
  const isContainer = state.planningMode === 'container';
  const isGroup = !isLeaf;
  const hasEditableSchedule = isLeaf;
  const hasEditableConstraints = isLeaf || isContainer;

  const planningModeOptions = Object.entries(PLANNING_MODE_CONFIG).map(
    ([value, config]) => ({ value: value as PlanningMode, ...config })
  );
  const activePlanningMode = planningModeOptions.find((o) => o.value === state.planningMode);
  const ActivePlanningModeIcon = activePlanningMode?.icon;

  const typeChoice: 'leaf' | 'group' = isLeaf ? 'leaf' : 'group';

  const setTypeChoice = (value: 'leaf' | 'group') => {
    if (value === 'leaf') {
      setField('planningMode', 'leaf');
      return;
    }

    // Default new groups to derived behavior; boundary can be enabled via switch.
    setField('planningMode', 'summary');
  };

  const setGroupBoundaryMode = (enabled: boolean) => {
    setField('planningMode', enabled ? 'container' : 'summary');
  };

  // Load criteria, templates, and spaces on mount
  useEffect(() => {
    const loadData = async () => {
      if (!open || !selectedSiteId) return;
      setIsLoading(true);
      try {
        const [criteriaData, templatesData, spacesData] = await Promise.all([
          getCriteria(),
          getTemplates('request'),
          getSpaces(selectedSiteId)
        ]);
        setAvailableCriteria(criteriaData);
        setAvailableTemplates(templatesData);
        setAvailableSpaces(spacesData);
      } catch (error) {
        logger.error("Failed to load data:", error);
      } finally {
        setIsLoading(false);
      }
    };
    loadData();
  }, [open, selectedSiteId]);

  const handleApplyTemplate = (templateId: string) => {
    const template = availableTemplates.find((t) => t.id === templateId);
    if (!template) return;

    applyTemplate(template);
  };

  const handleAddRequirement = () => {
    if (!selectedCriterionId) return;
    const criterion = availableCriteria.find((c) => c.id === selectedCriterionId);
    if (!criterion) return;

    addRequirement(selectedCriterionId, criterion.dataType === 'Boolean' ? false : null);
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
    setValidationError(null);

    if (!state.name.trim()) {
      setValidationError(VALIDATION_MESSAGES.REQUEST_NAME_REQUIRED);
      return;
    }

    if (hasEditableSchedule && (!state.durationValue || state.durationValue < 1)) {
      setValidationError(VALIDATION_MESSAGES.DURATION_REQUIRED);
      return;
    }

    // Validate scheduling dates if provided (leaf only)
    let startTs: string | undefined;
    let endTs: string | undefined;

    if (hasEditableSchedule && state.startDate && state.startTime) {
      startTs = combineDateTimeToISO(state.startDate, state.startTime);
    }

    if (hasEditableSchedule && state.endDate && state.endTime) {
      endTs = combineDateTimeToISO(state.endDate, state.endTime);
    }

    // If both are provided, validate order
    if (startTs && endTs && new Date(startTs) >= new Date(endTs)) {
      setValidationError(VALIDATION_MESSAGES.END_BEFORE_START);
      return;
    }

    // If one is provided but not the other, show error
    if ((startTs && !endTs) || (!startTs && endTs)) {
      setValidationError(VALIDATION_MESSAGES.DATES_MUST_BE_TOGETHER);
      return;
    }

    // Validate constraint dates if provided (leaf and boundary-group modes)
    let earliestStartTs: string | undefined;
    let latestEndTs: string | undefined;

    if (hasEditableConstraints && state.earliestStartDate && state.earliestStartTime) {
      earliestStartTs = combineDateTimeToISO(state.earliestStartDate, state.earliestStartTime);
    }

    if (hasEditableConstraints && state.latestEndDate && state.latestEndTime) {
      latestEndTs = combineDateTimeToISO(state.latestEndDate, state.latestEndTime);
    }

    // Validate constraint order
    if (earliestStartTs && latestEndTs && new Date(earliestStartTs) >= new Date(latestEndTs)) {
      setValidationError(VALIDATION_MESSAGES.CONSTRAINT_ORDER);
      return;
    }

    // Validate scheduled dates are within constraints
    if (earliestStartTs && startTs && new Date(startTs) < new Date(earliestStartTs)) {
      setValidationError(VALIDATION_MESSAGES.START_BEFORE_CONSTRAINT);
      return;
    }

    if (latestEndTs && endTs && new Date(endTs) > new Date(latestEndTs)) {
      setValidationError(VALIDATION_MESSAGES.END_AFTER_CONSTRAINT);
      return;
    }

    const formData: RequestFormData = {
      name: state.name.trim(),
      description: state.description.trim() || undefined,
      planningMode: state.planningMode,
      parentRequestId: state.parentRequestId || undefined,
      spaceId: isLeaf ? (state.selectedSpaceId || undefined) : undefined,
      startTs: hasEditableSchedule ? startTs : undefined,
      endTs: hasEditableSchedule ? endTs : undefined,
      earliestStartTs: hasEditableConstraints ? earliestStartTs : undefined,
      latestEndTs: hasEditableConstraints ? latestEndTs : undefined,
      duration: {
        value: state.durationValue,
        unit: state.durationUnit,
      },
      schedulingSettingsApply: state.schedulingSettingsApply,
      requirements: Array.from(state.requirements.entries())
        .filter(([, value]) => value !== null)
        .map(([criterionId, value]) => ({
          criterionId,
          value: value!,
        })),
    };

    setIsSaving(true);
    try {
      await onSave(formData);
      onOpenChange(false);
    } catch (error) {
      logger.error("Failed to save request:", error);
      setValidationError(error instanceof Error ? error.message : "Failed to save request");
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-2xl max-h-[85vh] flex flex-col p-0">
        <DialogHeader className="px-6 pt-6 pb-4 shrink-0">
          <DialogTitle className="text-xl">
            {request ? "Edit Request" : isChildCreation ? "Add Child Request" : "Create New Request"}
          </DialogTitle>
          <DialogDescription>
            {request
              ? "Update the request details below."
              : isChildCreation
                ? `Adding a child request under "${parentRequest.name}".`
                : "Fill in the details for your new space request."}
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="flex flex-col flex-1 min-h-0">
          <div className="flex-1 px-6 overflow-y-auto">
            <div className="space-y-6 pb-6">              {/* Template Selector - Only show in create mode */}
              {!request && availableTemplates.length > 0 && (
                <div className="space-y-2 p-4 border rounded-lg bg-muted/50">
                  <Label htmlFor="template" className="flex items-center gap-2">
                    <FileText className="h-4 w-4" />
                    Apply Template (Optional)
                  </Label>
                  <Select onValueChange={handleApplyTemplate}>
                    <SelectTrigger id="template">
                      <SelectValue placeholder="Select a template to pre-fill duration and constraints" />
                    </SelectTrigger>
                    <SelectContent>
                      {availableTemplates.map((template) => (
                        <SelectItem key={template.id} value={template.id}>
                          {template.name} ({template.durationValue} {template.durationUnit})
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                  <p className="text-xs text-muted-foreground">
                    Templates pre-fill duration and timing constraints. You can adjust them after applying.
                  </p>
                </div>
              )}
              {/* Basic Info */}
              <Collapsible open={state.openSections.basic} onOpenChange={() => toggleSection('basic')}>
                <CollapsibleTrigger className="flex items-center justify-between w-full p-3 hover:bg-muted/50 rounded-lg">
                  <h3 className="text-sm font-medium">Basic Information</h3>
                  <ChevronDown className={`h-4 w-4 transition-transform ${state.openSections.basic ? 'rotate-180' : ''}`} />
                </CollapsibleTrigger>
                <CollapsibleContent className="pt-4">
                  <div className="space-y-4">
                    <div className="space-y-2">
                      <Label htmlFor="name">
                        Name <span className="text-destructive">*</span>
                      </Label>
                      <Input
                        id="name"
                        value={state.name}
                        onChange={(e) => setField('name', e.target.value)}
                        placeholder="e.g., Product Launch Event"
                        required
                      />
                    </div>

                    <div className="space-y-2">
                      <Label htmlFor="description">Description</Label>
                      <Textarea
                        id="description"
                        value={state.description}
                        onChange={(e) => setField('description', e.target.value)}
                        placeholder="Optional description of the request"
                        rows={3}
                      />
                    </div>

                    {/* Type */}
                    <div className="space-y-2">
                      <Label htmlFor="planningMode">Type</Label>
                      {isCreateMode ? (
                        <div
                          id="planningMode"
                          className="h-10 px-3 rounded-md border bg-muted/30 flex items-center"
                          aria-readonly="true"
                        >
                          {activePlanningMode && ActivePlanningModeIcon && (
                            <span className="flex items-center gap-2">
                              <ActivePlanningModeIcon className="h-3.5 w-3.5" />
                              {activePlanningMode.label}
                            </span>
                          )}
                        </div>
                      ) : (
                        <Select
                          value={typeChoice}
                          onValueChange={(v) => setTypeChoice(v as 'leaf' | 'group')}
                        >
                          <SelectTrigger id="planningMode">
                            <SelectValue placeholder="Select type" />
                          </SelectTrigger>
                          <SelectContent>
                            <SelectItem value="leaf">
                              <span className="flex items-center gap-2">
                                <FileText className="h-3.5 w-3.5" />
                                Task
                              </span>
                            </SelectItem>
                            <SelectItem value="group">
                              <span className="flex items-center gap-2">
                                <Layers className="h-3.5 w-3.5" />
                                Group
                              </span>
                            </SelectItem>
                          </SelectContent>
                        </Select>
                      )}
                      <p className="text-xs text-muted-foreground">
                        {isLeaf
                          ? PLANNING_MODE_CONFIG.leaf.description
                          : 'Groups child tasks. Use boundary mode to enforce child timing limits.'}
                      </p>

                      {isGroup && (
                        <div className="flex items-center gap-2 pt-1">
                          <input
                            id="group-boundary-mode"
                            type="checkbox"
                            checked={isContainer}
                            onChange={(e) => setGroupBoundaryMode(e.target.checked)}
                            className="h-4 w-4 rounded border-input"
                          />
                          <Label htmlFor="group-boundary-mode" className="text-sm cursor-pointer">
                            Boundary mode (enforce child start/end within group constraints)
                          </Label>
                        </div>
                      )}
                    </div>
                  </div>
                </CollapsibleContent>
              </Collapsible>

              <Separator />

              {/* Summary: read-only derived info (schedule is computed from children) */}
              {isSummary && request && (
                <div className="p-4 border rounded-lg bg-muted/30">
                  <h3 className="text-sm font-medium mb-2">Derived Schedule (read-only)</h3>
                  <p className="text-xs text-muted-foreground">
                    Summary dates and duration are automatically calculated from child requests.
                  </p>
                </div>
              )}

              {/* Schedule — leaf only */}
              {hasEditableSchedule && (
                <>
                  <RequestScheduleSection
                    state={state}
                    setField={setField}
                    toggleSection={toggleSection}
                    availableSpaces={isLeaf ? availableSpaces : []}
                  />

                  <Separator />

                  <RequestConstraintsSection
                    state={state}
                    setField={setField}
                    toggleSection={toggleSection}
                  />

                  <Separator />
                </>
              )}

              {/* Constraints — boundary groups only */}
              {isContainer && (
                <>
                  <RequestConstraintsSection
                    state={state}
                    setField={setField}
                    toggleSection={toggleSection}
                  />

                  <Separator />
                </>
              )}

              {/* Minimal Duration — leaf only */}
              {hasEditableSchedule && (
                <>
              <Collapsible open={state.openSections.duration} onOpenChange={() => toggleSection('duration')}>
                <CollapsibleTrigger className="flex items-center justify-between w-full p-3 hover:bg-muted/50 rounded-lg">
                  <h3 className="text-sm font-medium">Minimal Duration *</h3>
                  <ChevronDown className={`h-4 w-4 transition-transform ${state.openSections.duration ? 'rotate-180' : ''}`} />
                </CollapsibleTrigger>
                <CollapsibleContent className="pt-4">
                  <div className="space-y-4">
                <div className="space-y-2">
                  <Label htmlFor="durationValue">
                    Minimal Duration <span className="text-destructive">*</span>
                  </Label>
                  <p className="text-xs text-muted-foreground">
                    {isContainer ? "Boundary duration for child requests" : "Minimum time needed for this request"}
                  </p>
                  <div className="flex gap-2">
                    <Input
                      id="durationValue"
                      type="text"
                      inputMode="numeric"
                      pattern="[0-9]*"
                      value={state.durationValue || ''}
                      onChange={(e) => {
                        const val = e.target.value.replace(/[^0-9]/g, '');
                        setField('durationValue', val === '' ? 0 : parseInt(val));
                      }}
                      className="flex-1"
                      required
                    />
                    <Select
                      value={state.durationUnit}
                      onValueChange={(value) => setField('durationUnit', value as DurationUnit)}
                    >
                      <SelectTrigger className="w-32">
                        <SelectValue placeholder="Unit">
                          {state.durationUnit.charAt(0).toUpperCase() + state.durationUnit.slice(1)}
                        </SelectValue>
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
              </div>
                </CollapsibleContent>
              </Collapsible>

              <Separator />
                </>
              )}

              {/* Scheduling Settings Apply — only for leaf mode */}
              {isLeaf && (
                <div className="flex items-center gap-2 p-3">
                  <Checkbox
                    id="schedulingSettingsApply"
                    checked={state.schedulingSettingsApply}
                    onCheckedChange={(checked) => setField('schedulingSettingsApply', !!checked)}
                  />
                  <Label htmlFor="schedulingSettingsApply" className="text-sm cursor-pointer">
                    Apply scheduling settings (working hours, off-times)
                  </Label>
                </div>
              )}

              <Separator />

              <RequestRequirementsSection
                state={state}
                toggleSection={toggleSection}
                availableCriteria={availableCriteria}
                selectedCriterionId={selectedCriterionId}
                setSelectedCriterionId={setSelectedCriterionId}
                isLoading={isLoading}
                onAddRequirement={handleAddRequirement}
                onRemoveRequirement={handleRemoveRequirement}
                onRequirementValueChange={handleRequirementValueChange}
              />
            </div>
          </div>

          {/* Footer Actions */}
          <Separator className="shrink-0" />
          <div className="px-6 pt-3">
            <ErrorAlert message={validationError} />
          </div>
          <div className="flex justify-end gap-3 px-6 py-4 shrink-0 bg-background">
            <Button
              type="button"
              variant="outline"
              onClick={() => onOpenChange(false)}
              disabled={isSaving}
            >
              Cancel
            </Button>
            <Button type="submit" disabled={isSaving}>
              {isSaving ? "Saving..." : request ? "Update Request" : "Create Request"}
            </Button>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  );
}
