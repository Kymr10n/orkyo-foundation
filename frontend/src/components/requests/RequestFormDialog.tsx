import { useRequestFormDialog } from "@foundation/src/hooks/useRequestFormDialog";
import type { UseRequestFormDialogOptions } from "@foundation/src/hooks/useRequestFormDialog";
import { FormDialog } from "@foundation/src/components/ui/FormDialog";
import { DialogFormFooter } from "@foundation/src/components/ui/DialogFormFooter";
import { DialogFooter } from "@foundation/src/components/ui/dialog";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@foundation/src/components/ui/tabs";
import { ErrorAlert } from "@foundation/src/components/ui/ErrorAlert";
import { Button } from "@foundation/src/components/ui/button";
import { Input } from "@foundation/src/components/ui/input";
import { Label } from "@foundation/src/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@foundation/src/components/ui/select";
import { Separator } from "@foundation/src/components/ui/separator";
import { Textarea } from "@foundation/src/components/ui/textarea";
import { RequestIconSelector } from "@foundation/src/components/requests/RequestIconSelector";
import { PLANNING_MODE_CONFIG } from "@foundation/src/constants";
import { durationToMinutes, formatDuration, formatMinutesHuman } from "@foundation/src/lib/utils";
import { formatDateDisplay } from "@foundation/src/lib/formatters";
import { Alert, AlertDescription } from "@foundation/src/components/ui/alert";
import type { DurationUnit, RequestFormData } from "@foundation/src/types/requests";
import { ConflictBanner } from "./ConflictIndicator";
import { TabIndicatorDot } from "@foundation/src/components/ui/status-indicator";
import { AlertTriangle, ChevronRight, FileText, Layers } from "lucide-react";
import { Fragment } from "react";
import { Checkbox } from "@foundation/src/components/ui/checkbox";
import { RequestScheduleSection } from "./RequestScheduleSection";
import { RequestConstraintsSection } from "./RequestConstraintsSection";
import { RequestRequirementsSection } from "./RequestRequirementsSection";
import { RequestResourcesSection } from "./RequestResourcesSection";
import { RequestChildrenSection } from "./RequestChildrenSection";
import { RequestDependenciesSection } from "./RequestDependenciesSection";
// The hook owns the tab state, so it owns the union naming the tabs.
import type { RequestFormTab } from "@foundation/src/hooks/useRequestFormDialog";


/** Sentinel for the "Any site" (site-neutral) option — Radix Select disallows empty values. */
const ANY_SITE = '__any_site__';

type RequestFormDialogProps = UseRequestFormDialogOptions & {
  /** Re-target the dialog to another request (breadcrumb / children click). */
  onNavigate?: (requestId: string) => void;
};

// RequestFormData moved to the types layer (W2.5) so lib-level payload builders
// don't import from the components layer; re-exported to keep existing paths working.
export type { RequestFormData };

/**
 * Placeholder for the request name field.
 *
 * Exported so the tests select the input by the same string the component renders —
 * a dozen call sites used to hard-code a copy of it, and any wording change broke
 * them all. The example is deliberately shop-floor: the previous one ("Product
 * Launch Event") read as event-planning software to the manufacturing audience.
 */
export const REQUEST_NAME_PLACEHOLDER = "e.g., Bracket run \u2014 200 pcs";

export function RequestFormDialog(props: RequestFormDialogProps) {
  const { open, onOpenChange, request, parentRequest, conflicts = [], onNavigate, onOpenPlan } = props;
  const {
    state, setField, sites, isMultiSite, availableCriteria, requirementTypeKeys, availableTemplates, isLoading,
    isChildCreation, readOnly, isLeaf, isGroup, isContainer, typeChoice, setTypeChoice,
    setGroupBoundaryMode, hasEditableSchedule, hasChildren, showChildrenTab, showDependenciesTab,
    activeTab, setActiveTab, validationError, isSaving, handleSubmit, nameInputRef,
    conflictsByResourceId, conflictsByCriterionId, resourceConflictDot, requirementConflictDot,
    selectedCriterionId, setSelectedCriterionId, handleAddRequirement, handleRemoveRequirement,
    handleRequirementChange, handleApplyTemplate, hasPeopleBlockers, setHasPeopleBlockers,
    breadcrumb, directChildren, derivedValues, dependencyCandidates,
    newChildName, setNewChildName, isAddingChild, handleAddChild, handleRemoveChild,
    pendingChildren, setPendingChildren, setPendingExistingIds, pendingExistingRequests,
    addExistingOpen, setAddExistingOpen, addExistingSearch, setAddExistingSearch,
    addExistingSelected, toggleAddExistingSelected, addExistingCandidates,
    addExistingViewportRef, addExistingVirtualizer, isAddingExisting, handleAddExisting,
    hasDurationWarning, windowMinutes, siteScopeWarning,
    setIsDirty, effectiveDirty, confirmOpen, guardedOnOpenChange,
    ConfirmDiscardDialog, requestPlanNavigation,
  } = useRequestFormDialog(props);

  return (
    <>
    <FormDialog
      open={open}
      onOpenChange={guardedOnOpenChange}
      size="lg"
      title={
        <span className="text-xl">
          {readOnly
            ? "Request details"
            : request ? "Edit Request" : isChildCreation ? "Add Child Request" : "Create New Request"}
        </span>
      }
      description={
        readOnly
          ? "View request details."
          : request
            ? "Update the request details below."
            : isChildCreation
              ? `Adding a child request under "${parentRequest?.name}".`
              : "Fill in the details for your new space request."
      }
      contentProps={{
        onOpenAutoFocus: (e) => {
          // Land focus on the first field, not the active tab — otherwise the
          // tab's keyboard-focus ring flashes on open. The ring still shows for
          // real keyboard tab navigation.
          e.preventDefault();
          nameInputRef.current?.focus();
        },
        // Don't let outside interactions dismiss this dialog while there are
        // unsaved changes. Guarding on `isDirty` (stable across the whole
        // interaction) rather than `confirmOpen` (which flips to false the
        // instant "Keep editing" closes the confirm) avoids a race where a
        // trailing pointer/focus-outside event re-opens the prompt and traps
        // the user. When dirty, closing goes through the explicit X / Cancel /
        // Escape paths, which run the guarded prompt exactly once.
        onInteractOutside: (e) => { if (effectiveDirty || confirmOpen) e.preventDefault(); },
        onEscapeKeyDown: (e) => { if (confirmOpen) e.preventDefault(); },
      }}
      footer={null}
    >
        {breadcrumb.length > 0 && (
          <div className="px-6 pb-2 flex items-center gap-1 text-xs text-muted-foreground flex-wrap">
            {breadcrumb.map((ancestor, i) => (
              <Fragment key={ancestor.id}>
                {i > 0 && <ChevronRight className="h-3 w-3" />}
                <button
                  type="button"
                  className="hover:text-foreground hover:underline"
                  onClick={() => onNavigate?.(ancestor.id)}
                >
                  {ancestor.name}
                </button>
              </Fragment>
            ))}
            <ChevronRight className="h-3 w-3" />
            <span className="text-foreground font-medium">{request?.name}</span>
          </div>
        )}

        <ConflictBanner conflicts={conflicts} />

        <form
          onSubmit={handleSubmit}
          onInput={() => setIsDirty(true)}
          onChange={() => setIsDirty(true)}
          className="flex flex-col flex-1 min-h-0"
        >
          <Tabs
            value={activeTab}
            onValueChange={(v) => setActiveTab(v as RequestFormTab)}
            className="flex flex-col flex-1 min-h-0"
          >
            <TabsList className="mx-6 shrink-0">
              <TabsTrigger value="details">Details</TabsTrigger>
              <TabsTrigger value="timing" className="relative">
                Timing
                <TabIndicatorDot dotClass={hasDurationWarning ? "bg-amber-500" : null} label="timing warning" />
              </TabsTrigger>
              <TabsTrigger value="requirements" className="relative">
                Requirements
                <TabIndicatorDot dotClass={requirementConflictDot} label="requirement conflict" />
              </TabsTrigger>
              {isLeaf && (
                <TabsTrigger value="resources" className="relative">
                  Resources
                  <TabIndicatorDot dotClass={resourceConflictDot} label="resource conflict" />
                </TabsTrigger>
              )}
              {showChildrenTab && (
                <TabsTrigger value="children">Children</TabsTrigger>
              )}
              {showDependenciesTab && (
                <TabsTrigger value="dependencies">Dependencies</TabsTrigger>
              )}
            </TabsList>

            <div className="flex-1 min-h-0 overflow-y-auto px-6 py-4">
              <TabsContent value="details" className="mt-0 space-y-4">
                {/* Template Selector - Only show in create mode */}
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
                {/* Basic Information */}
                <h4 className="text-sm font-medium">Basic Information</h4>
                <div className="space-y-4">
                    <div className="space-y-2">
                      <Label htmlFor="name">
                        Name <span className="text-destructive">*</span>
                      </Label>
                      <div className="flex gap-2">
                        <RequestIconSelector
                          id="request-icon"
                          value={state.icon}
                          onChange={(next) => setField('icon', next)}
                          disabled={readOnly}
                        />
                        <Input
                          ref={nameInputRef}
                          id="name"
                          value={state.name}
                          onChange={(e) => setField('name', e.target.value)}
                          placeholder={REQUEST_NAME_PLACEHOLDER}
                          required
                          className="flex-1"
                          disabled={readOnly}
                        />
                      </div>
                    </div>

                    <div className="space-y-2">
                      <Label htmlFor="description">Description</Label>
                      <Textarea
                        id="description"
                        value={state.description}
                        onChange={(e) => setField('description', e.target.value)}
                        placeholder="Optional description of the request"
                        rows={3}
                        disabled={readOnly}
                      />
                    </div>

                    {/* Type */}
                    <div className="space-y-2">
                      <Label htmlFor="planningMode">Type</Label>
                      <Select
                        value={typeChoice}
                        onValueChange={(v) => setTypeChoice(v as 'leaf' | 'group')}
                        disabled={readOnly}
                      >
                        <SelectTrigger id="planningMode">
                          <SelectValue placeholder="Select type" />
                        </SelectTrigger>
                        <SelectContent>
                          {/* A request with children can't become a leaf — the backend
                              rejects it (RequestService.UpdateAsync). Disable rather than
                              let the user hit a 409 on save. */}
                          <SelectItem value="leaf" disabled={hasChildren}>
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
                      <p className="text-xs text-muted-foreground">
                        {isLeaf
                          ? PLANNING_MODE_CONFIG.leaf.description
                          : 'Groups child tasks. Use boundary mode to enforce child timing limits.'}
                      </p>
                      {hasChildren && (
                        <p className="text-xs text-muted-foreground">
                          This request has child requests — remove or reassign them before it can become a Task.
                        </p>
                      )}

                      {isGroup && (
                        <div className="flex items-center gap-2 pt-1">
                          <input
                            id="group-boundary-mode"
                            type="checkbox"
                            checked={isContainer}
                            onChange={(e) => setGroupBoundaryMode(e.target.checked)}
                            className="h-4 w-4 rounded border-input"
                            disabled={readOnly}
                          />
                          <Label htmlFor="group-boundary-mode" className="text-sm cursor-pointer">
                            Boundary mode (enforce child start/end within group constraints)
                          </Label>
                        </div>
                      )}
                    </div>

                    {/* Site scope — only meaningful with more than one site (free/single-site
                        tenants never see it; their requests default to the one site). */}
                    {isMultiSite && (
                      <div className="space-y-2">
                        <Label htmlFor="siteId">Site</Label>
                        <Select
                          value={state.siteId || ANY_SITE}
                          onValueChange={(v) => setField('siteId', v === ANY_SITE ? '' : v)}
                          disabled={readOnly}
                        >
                          <SelectTrigger id="siteId">
                            <SelectValue placeholder="Any site" />
                          </SelectTrigger>
                          <SelectContent>
                            <SelectItem value={ANY_SITE}>
                              <span className="text-muted-foreground">Any site</span>
                            </SelectItem>
                            {sites.map((site) => (
                              <SelectItem key={site.id} value={site.id}>
                                {site.name}
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                        <p className="text-xs text-muted-foreground">
                          Where can this request be fulfilled? “Any site” can be scheduled at any site.
                        </p>
                        {siteScopeWarning && (
                          <Alert variant="warning">
                            <AlertTriangle className="h-4 w-4" />
                            <AlertDescription>{siteScopeWarning}</AlertDescription>
                          </Alert>
                        )}
                      </div>
                    )}
                  </div>
              </TabsContent>

              {/* TIMING */}
              <TabsContent value="timing" className="mt-0 space-y-6">
                {/* Group: derived schedule/effort rolled up from children. Shows real
                    values when the tree is available, else the placeholder note
                    (always shown in create mode, where nothing is derived yet). */}
                {isGroup && (
                  <div className="p-4 border rounded-lg bg-muted/30 space-y-2">
                    <h4 className="text-sm font-medium">Derived Schedule (read-only)</h4>
                    {derivedValues && (derivedValues.startTs || derivedValues.endTs) ? (
                      <div className="text-sm space-y-1">
                        <div className="flex justify-between">
                          <span className="text-muted-foreground">Earliest</span>
                          <span className="italic">
                            {derivedValues.startTs ? formatDateDisplay(derivedValues.startTs) : '—'}
                          </span>
                        </div>
                        <div className="flex justify-between">
                          <span className="text-muted-foreground">Latest</span>
                          <span className="italic">
                            {derivedValues.endTs ? formatDateDisplay(derivedValues.endTs) : '—'}
                          </span>
                        </div>
                        <div className="flex justify-between">
                          <span className="text-muted-foreground">Sum of children</span>
                          <span className="italic">
                            {formatDuration(derivedValues.totalDurationValue, derivedValues.totalDurationUnit)}
                          </span>
                        </div>
                      </div>
                    ) : (
                      <p className="text-xs text-muted-foreground">
                        {isContainer
                          ? "Dates and duration roll up from children. The boundary window below limits when they can be scheduled."
                          : "Summary dates and duration are automatically calculated from child requests."}
                      </p>
                    )}
                  </div>
                )}

                {/* Schedule + Constraints + Duration — leaf only */}
                {hasEditableSchedule && (
                  <>
                    {/* Minimal Duration — first, since it's required and drives scheduling */}
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
                            disabled={readOnly}
                          />
                          <Select
                            value={state.durationUnit}
                            onValueChange={(value) => setField('durationUnit', value as DurationUnit)}
                            disabled={readOnly}
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
                        {hasDurationWarning && (
                          <Alert variant="warning" className="mt-2">
                            <AlertTriangle className="h-4 w-4" />
                            <AlertDescription>
                              Window ({formatMinutesHuman(Math.max(0, Math.round(windowMinutes)))}) is shorter than the minimal duration ({formatMinutesHuman(durationToMinutes(state.durationValue, state.durationUnit))}). You can still save — a conflict will be recorded and must be resolved before scheduling.
                            </AlertDescription>
                          </Alert>
                        )}
                    </div>

                    <Separator />

                    <RequestScheduleSection
                      state={state}
                      setField={setField}
                      readOnly={readOnly}
                    />

                    <Separator />

                    <RequestConstraintsSection
                      state={state}
                      setField={setField}
                      readOnly={readOnly}
                    />

                    <Separator />

                    {/* Scheduling Settings Apply */}
                    <div className="flex items-center gap-2">
                      <Checkbox
                        id="schedulingSettingsApply"
                        checked={state.schedulingSettingsApply}
                        onCheckedChange={(checked) => setField('schedulingSettingsApply', !!checked)}
                        disabled={readOnly}
                      />
                      <Label htmlFor="schedulingSettingsApply" className="text-sm cursor-pointer">
                        Apply scheduling settings (working hours, off-times)
                      </Label>
                    </div>
                  </>
                )}

                {/* Constraints — boundary groups only. Copy reframed: the window
                    bounds the children, it isn't the group's own schedule. */}
                {isContainer && (
                  <RequestConstraintsSection
                    state={state}
                    setField={setField}
                    readOnly={readOnly}
                    title="Boundary Window (Optional)"
                    description="Children must start and finish within this window."
                  />
                )}
              </TabsContent>

              {/* REQUIREMENTS */}
              <TabsContent value="requirements" className="mt-0">
                <RequestRequirementsSection
                  state={state}
                  availableCriteria={availableCriteria}
                  requirementTypeKeys={requirementTypeKeys}
                  selectedCriterionId={selectedCriterionId}
                  setSelectedCriterionId={setSelectedCriterionId}
                  isLoading={isLoading}
                  conflictsByCriterionId={conflictsByCriterionId}
                  readOnly={readOnly}
                  onAddRequirement={handleAddRequirement}
                  onRemoveRequirement={handleRemoveRequirement}
                  onRequirementChange={handleRequirementChange}
                />
              </TabsContent>

              {/* RESOURCES — leaf only. */}
              {isLeaf && (
                <RequestResourcesSection
                  activeTab={activeTab}
                  state={state}
                  setField={setField}
                  readOnly={readOnly}
                  requestId={request?.id}
                  siteId={state.siteId}
                  hasEditableSchedule={hasEditableSchedule}
                  onBlockersChange={setHasPeopleBlockers}
                  conflictsByResourceId={conflictsByResourceId}
                />
              )}

              {/* CHILDREN — groups only, needs the tree. */}
              {showChildrenTab && (
                <RequestChildrenSection
                  request={request}
                  readOnly={readOnly}
                  newChildName={newChildName}
                  setNewChildName={setNewChildName}
                  isAddingChild={isAddingChild}
                  handleAddChild={handleAddChild}
                  addExistingOpen={addExistingOpen}
                  setAddExistingOpen={setAddExistingOpen}
                  addExistingSearch={addExistingSearch}
                  setAddExistingSearch={setAddExistingSearch}
                  addExistingCandidates={addExistingCandidates}
                  addExistingSelected={addExistingSelected}
                  toggleAddExistingSelected={toggleAddExistingSelected}
                  addExistingViewportRef={addExistingViewportRef}
                  addExistingVirtualizer={addExistingVirtualizer}
                  isAddingExisting={isAddingExisting}
                  handleAddExisting={handleAddExisting}
                  pendingChildren={pendingChildren}
                  pendingExistingRequests={pendingExistingRequests}
                  setPendingChildren={setPendingChildren}
                  setPendingExistingIds={setPendingExistingIds}
                  directChildren={directChildren}
                  onNavigate={onNavigate}
                  onOpenPlan={onOpenPlan ? requestPlanNavigation : undefined}
                  handleRemoveChild={handleRemoveChild}
                />
              )}

              {/* DEPENDENCIES — leaves only: an edge on a group cannot be enforced. */}
              {showDependenciesTab && (
                <TabsContent value="dependencies" className="mt-0">
                  <RequestDependenciesSection
                    request={request}
                    readOnly={readOnly}
                    candidates={dependencyCandidates}
                    onNavigate={onNavigate}
                  />
                </TabsContent>
              )}
            </div>
          </Tabs>

          {/* Footer Actions */}
          <Separator className="shrink-0" />
          <div className="px-6 pt-3">
            <ErrorAlert message={validationError} />
          </div>
          {readOnly ? (
            <DialogFooter className="px-6 py-4 shrink-0 bg-background">
              <Button
                type="button"
                variant="outline"
                onClick={() => onOpenChange(false)}
                data-testid="view-close-btn"
              >
                Close
              </Button>
            </DialogFooter>
          ) : (
            <DialogFormFooter
              className="px-6 py-4 shrink-0 bg-background"
              onCancel={() => guardedOnOpenChange(false)}
              isSubmitting={isSaving}
              submitLabel={request ? "Update Request" : "Create Request"}
              submitDisabled={hasPeopleBlockers}
            />
          )}
        </form>
    </FormDialog>
    {ConfirmDiscardDialog}
    </>
  );
}
