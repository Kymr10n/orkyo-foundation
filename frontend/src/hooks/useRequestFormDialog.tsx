import { useCriteria } from "@foundation/src/hooks/useCriteria";
import { useResourceTypes } from "@foundation/src/hooks/useResourceTypes";
import { useTemplates } from "@foundation/src/hooks/useTemplates";
import { getRequestChildren } from "@foundation/src/lib/api/request-api";
import { useSites, useIsMultiSite } from "@foundation/src/hooks/useSites";
import { type Template } from "@foundation/src/types/templates";
import { useSiteStore } from "@foundation/src/store/site-store";
import { combineDateTimeToISO, durationToMinutes } from "@foundation/src/lib/utils";
import { formatDateDisplay } from "@foundation/src/lib/formatters";
import {
  computeDerivedValues,
  getAncestorIds,
  getDirectChildren,
} from "@foundation/src/domain/request-tree";
import type { Criterion } from "@foundation/src/types/criterion";
import type { RequirementEntry } from "@foundation/src/hooks/useRequestForm";
import type { Conflict, PlanningMode, Request, RequestFormData } from "@foundation/src/types/requests";
import { conflictDotClass } from "@foundation/src/components/requests/ConflictIndicator";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { toast } from "sonner";

import { useRequestForm, type DefaultResource, type DefaultSchedule } from "@foundation/src/hooks/useRequestForm";
import { useRequestChildrenTab } from "@foundation/src/hooks/useRequestChildrenTab";
import {
  isRequestFormValidationError,
  validateRequestForm,
  type RequestFormTab,
} from "@foundation/src/domain/request-form-validation";
import { useCanEdit } from "@foundation/src/hooks/usePermissions";
import { logger } from "@foundation/src/lib/core/logger";
import { errorMessage } from "@foundation/src/hooks/mutation-utils";

const EMPTY_CRITERIA: Criterion[] = [];
const EMPTY_TEMPLATES: Template[] = [];

export interface UseRequestFormDialogOptions {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  request?: Request | null;
  parentRequest?: Request | null;
  defaultPlanningMode?: PlanningMode;
  /** Seed start/end (e.g. a calendar slot selection). Overrides any schedule on `request`. */
  defaultSchedule?: DefaultSchedule;
  /** Seed a targeted+selected resource in create mode (e.g. a grid cell click). */
  defaultResource?: DefaultResource;
  /**
   * Site whose calendar/schedule this request is being placed on. Pre-selects a
   * site-neutral request to it and warns when the chosen site won't surface here.
   */
  scheduleSiteId?: string | null;
  /** Saved conflicts for this request (from the registry); surfaced as form indicators. */
  conflicts?: Conflict[];
  /**
   * The full request tree. Drives the ancestor breadcrumb, the Children tab, and
   * the group derived-schedule rollups. When undefined those features hide.
   */
  allRequests?: Request[];
  /** Re-target the dialog to another request (breadcrumb / children click). */
  onNavigate?: (requestId: string) => void;
  /** Opens the dependency planner for a group. Absent hides the entry point. */
  onOpenPlan?: (requestId: string) => void;
  /**
   * Create-mode consumers should return the created Request so queued children
   * from the Children tab can be created under it. May return void otherwise.
   */
  onSave: (data: RequestFormData) => void | Request | Promise<void | Request>;
}



export function computeSiteScopeWarning(
  isMultiSite: boolean,
  scheduleSiteId: string | null | undefined,
  currentSiteId: string,
  scheduleSiteName: string | undefined,
): string | null {
  if (!isMultiSite || !scheduleSiteId || (currentSiteId || '') === scheduleSiteId) return null;
  const where = scheduleSiteName ?? "this site";
  return currentSiteId
    ? `This request is scoped to another site, so it won't appear on ${where}'s schedule.`
    : `“Any site” requests aren't placed on a specific calendar, so this won't appear on ${where}'s schedule until you assign it a site.`;
}

/**
 * Everything `RequestFormDialog` does that is not rendering: the form state and its
 * dirty tracking, the reference-data queries, the derived flags each of the six tabs
 * reads, the tree-derived breadcrumb / children / rollups, the Children tab's
 * queue-and-commit flows, the validation chain and the save.
 *
 * It exists so the dialog is a composition of its sections instead of 1,250 lines with
 * the markup buried in the middle of the orchestration. The hook owns state and
 * behaviour; the component owns layout and nothing else. The return is wide by design
 * — it is the dialog's whole vocabulary, named once here rather than re-derived per
 * section.
 */
export function useRequestFormDialog({
  open,
  onOpenChange,
  request,
  parentRequest,
  defaultPlanningMode,
  defaultSchedule,
  defaultResource,
  scheduleSiteId,
  conflicts = [],
  allRequests,
  onOpenPlan,
  onSave,
}: UseRequestFormDialogOptions) {
  const selectedSiteId = useSiteStore((state) => state.selectedSiteId);
  const { data: sites = [] } = useSites();
  const isMultiSite = useIsMultiSite();
  const isChildCreation = !request && !!parentRequest;
  // A Viewer gets a read-only VIEW surface: every field is disabled, the footer is a single
  // Close button, and the mutation controls (Children add/remove, People add/remove) are hidden.
  const readOnly = !useCanEdit();

  // Use the custom hook for form state management
  const {
    state,
    setField: setFieldRaw,
    addRequirement: addRequirementRaw,
    removeRequirement: removeRequirementRaw,
    updateRequirement: updateRequirementRaw,
    applyTemplate: applyTemplateRaw,
  } = useRequestForm(request, parentRequest?.id, defaultPlanningMode, defaultSchedule, selectedSiteId, scheduleSiteId, defaultResource);

  // Map saved conflicts onto the form: by assigned resource (people + space) and by requirement
  // criterion, so each row can flag itself. The banner above the tabs shows the full list.
  const conflictsByResourceId = useMemo(() => {
    const map = new Map<string, Conflict[]>();
    for (const c of conflicts) {
      if (!c.resourceId) continue;
      const list = map.get(c.resourceId) ?? [];
      list.push(c);
      map.set(c.resourceId, list);
    }
    return map;
  }, [conflicts]);
  const conflictsByCriterionId = useMemo(() => {
    const map = new Map<string, Conflict[]>();
    for (const c of conflicts) {
      if (!c.criterionId) continue;
      const list = map.get(c.criterionId) ?? [];
      list.push(c);
      map.set(c.criterionId, list);
    }
    return map;
  }, [conflicts]);
  // Tab dot colour reflects the worst severity of conflicts owned by that tab
  // (error → red, warning-only → amber), matching the per-row indicators.
  const resourceConflictDot = conflictDotClass(conflicts.filter((c) => c.resourceId));
  const requirementConflictDot = conflictDotClass(conflicts.filter((c) => c.criterionId));

  // Reference data for the form — fetched only while the dialog is open and
  // cached under the shared keys, so other surfaces reuse the same data.
  const { data: availableCriteria = EMPTY_CRITERIA, isLoading: criteriaLoading } =
    useCriteria(open);
  const { data: availableTemplates = EMPTY_TEMPLATES, isLoading: templatesLoading } =
    useTemplates('request', open);
  const isLoading = criteriaLoading || templatesLoading;

  // The resource types a requirement can be asked of: the targeted ones plus every
  // directory type. People are staffed from the People section, never through Needs, so
  // their skills would otherwise never be offered. Same query key the form already uses.
  const { data: resourceTypes } = useResourceTypes(true);
  const requirementTypeKeys = useMemo(() => new Set([
    ...state.targetResourceTypeKeys,
    ...(resourceTypes ?? []).filter((t) => t.hasDirectoryProfile).map((t) => t.key),
  ]), [state.targetResourceTypeKeys, resourceTypes]);

  // Additional state not managed by the form hook
  const [isSaving, setIsSaving] = useState(false);
  const [validationError, setValidationError] = useState<string | null>(null);
  const [selectedCriterionId, setSelectedCriterionId] = useState("");
  const [hasPeopleBlockers, setHasPeopleBlockers] = useState(false);
  const [activeTab, setActiveTab] = useState<RequestFormTab>('details');
  const nameInputRef = useRef<HTMLInputElement>(null);
  // True when the edited request already has child requests — the backend rejects
  // converting such a request to a leaf (Task), so that option is disabled below.
  // Fetched from the API only when the caller didn't pass the full tree; when
  // `allRequests` is present, `directChildren` (derived from it) already answers this.
  // Keyed by request id, so a result never answers for a different request.
  const [childrenLookup, setChildrenLookup] = useState<{ requestId: string; hasChildren: boolean } | null>(null);

  // Track unsaved-changes state. Flips to true on first user interaction with
  // any form input; reset when the dialog is reopened. Render-phase reset, not an
  // effect (see useEntityFormDialog.ts).
  const [isDirty, setIsDirty] = useState(false);
  const [syncedOpen, setSyncedOpen] = useState(open);
  if (syncedOpen !== open) {
    setSyncedOpen(open);
    if (open) {
      setIsDirty(false);
      setActiveTab('details');
    }
  }

  // Dirty tracking happens at the state layer, not just via DOM event bubbling:
  // Radix Selects/Checkboxes and the icon selector don't emit native input/change
  // events, so the form-level onInput/onChange alone would miss them (silent
  // unsaved-change loss). Every user-driven form mutation goes through these
  // wrappers; the hook's own init on open never does.
  const setField: typeof setFieldRaw = (field, value) => {
    setIsDirty(true);
    setFieldRaw(field, value);
  };
  const addRequirement: typeof addRequirementRaw = (...args) => {
    setIsDirty(true);
    addRequirementRaw(...args);
  };
  const removeRequirement: typeof removeRequirementRaw = (...args) => {
    setIsDirty(true);
    removeRequirementRaw(...args);
  };
  const updateRequirement: typeof updateRequirementRaw = (...args) => {
    setIsDirty(true);
    updateRequirementRaw(...args);
  };
  const applyTemplate: typeof applyTemplateRaw = (...args) => {
    setIsDirty(true);
    applyTemplateRaw(...args);
  };

  // Opening the planner leaves this route, so it has to pass the same discard prompt closing
  // does — otherwise a click on "Sequence these tasks" throws away every unsaved field without
  // asking, which is precisely what the guard exists to prevent. The dialog queues the plan id
  // here and then asks FormDialog for a guarded close; the id is used once the close happens.
  const pendingPlanId = useRef<string | null>(null);

  const handleDialogClose = useCallback((open: boolean) => {
    onOpenChange(open);
    if (open) return;

    const planId = pendingPlanId.current;
    pendingPlanId.current = null;
    if (planId) onOpenPlan?.(planId);
  }, [onOpenChange, onOpenPlan]);

  const queuePlanNavigation = useCallback((id: string) => {
    pendingPlanId.current = id;
  }, []);

  // "Keep editing" leaves the dialog open, so the queued navigation must be dropped — otherwise
  // it would fire later, on an ordinary close the user meant as a close.
  const dropPlanNavigation = useCallback(() => {
    pendingPlanId.current = null;
  }, []);

  // Leaf: fully editable schedule.
  // Summary/Container: structural nodes; schedule is derived from children and not editable.
  const isLeaf = state.planningMode === 'leaf';

  // Gated on the SAVED request, not the form: an edge points at a persisted row, and the backend
  // decides leaf-ness from the database. Reading `state.planningMode` here would offer the tab the
  // moment somebody picked "Task" in the form, and every write would 409 until they saved.
  //
  // `allRequests` is required for the same reason the Children tab requires it: without it the
  // picker has nothing to offer and the peer names are unclickable, which reads as a broken tab
  // rather than a missing prop.
  const showDependenciesTab = request?.planningMode === 'leaf' && !!allRequests;

  // Only leaves can be linked, and a request never waits for itself.
  const dependencyCandidates = useMemo(
    () => (allRequests ?? []).filter((r) => r.planningMode === 'leaf' && r.id !== request?.id),
    [allRequests, request?.id],
  );
  const isContainer = state.planningMode === 'container';
  const isGroup = !isLeaf;
  const hasEditableSchedule = isLeaf;


  // Tree-derived surfaces — only available when the caller passes the full tree.
  // Breadcrumb (ancestors), direct children (Children tab), and the group
  // derived-schedule rollup all hide gracefully when `allRequests` is absent.
  // Shared id lookup for the tree-derived memos below — built once per
  // allRequests change instead of once per memo.
  const requestsById = useMemo(
    () => new Map((allRequests ?? []).map((r) => [r.id, r])),
    [allRequests],
  );

  const { derivedValues, directChildren, breadcrumb } = useMemo(() => {
    if (!allRequests || !request) {
      return { derivedValues: null, directChildren: [] as Request[], breadcrumb: [] as Request[] };
    }
    const _derived = isGroup ? computeDerivedValues(request.id, allRequests) : null;
    const _children = getDirectChildren(request.id, allRequests);
    const _breadcrumb = getAncestorIds(request.id, allRequests, requestsById)
      .reverse()
      .map((id) => requestsById.get(id))
      .filter(Boolean) as Request[];
    return { derivedValues: _derived, directChildren: _children, breadcrumb: _breadcrumb };
  }, [allRequests, request, isGroup, requestsById]);

  // Edit mode needs the tree to list existing children; create mode only queues
  // names locally, so the tab is always available for a new group.
  const showChildrenTab = isGroup && (request ? !!allRequests : true);

  const childrenTab = useRequestChildrenTab({
    open,
    request,
    allRequests,
    requestsById,
    setError: setValidationError,
  });

  // Children-tab actions are immediate & committed in edit mode (not "unsaved form changes"), so
  // they don't set `isDirty` — see the Children TabsContent's event-propagation guard. In create
  // mode, though, queued children/existing requests ARE unsaved state that Discard legitimately
  // drops, so fold them in here.
  const effectiveDirty = isDirty || childrenTab.hasPending;

  const typeChoice: 'leaf' | 'group' = isLeaf ? 'leaf' : 'group';

  const setTypeChoice = (value: 'leaf' | 'group') => {
    if (value === 'leaf') {
      setField('planningMode', 'leaf');
      // A task can't have children — drop any names queued while it was a group.
      childrenTab.setPendingChildren([]);
      return;
    }

    // Default new groups to derived behavior; boundary can be enabled via switch.
    setField('planningMode', 'summary');
  };

  const setGroupBoundaryMode = (enabled: boolean) => {
    setField('planningMode', enabled ? 'container' : 'summary');
  };

  // Duration warning: fires when the chosen start–end window is shorter than the minimal duration.
  // Non-blocking — user may still save; backend IntrinsicConflicts.below_min_duration persists it.
  const { hasDurationWarning, windowMinutes } = useMemo(() => {
    if (!state.durationValue || !state.startDate || !state.startTime || !state.endDate || !state.endTime)
      return { hasDurationWarning: false, windowMinutes: 0 };
    const startMs = new Date(combineDateTimeToISO(state.startDate, state.startTime)).getTime();
    const endMs   = new Date(combineDateTimeToISO(state.endDate,   state.endTime)).getTime();
    const mins = (endMs - startMs) / 60_000;
    return { hasDurationWarning: mins < durationToMinutes(state.durationValue, state.durationUnit), windowMinutes: mins };
  }, [state.durationValue, state.durationUnit, state.startDate, state.startTime, state.endDate, state.endTime]);

  // Scheduling-context warning: the chosen site won't surface on the calendar the
  // user is scheduling from when it's a different site, or site-neutral. Pre-selection
  // aims to avoid this, but the user may override it.
  const scheduleSiteName = scheduleSiteId ? sites.find((s) => s.id === scheduleSiteId)?.name : undefined;
  const siteScopeWarning = computeSiteScopeWarning(isMultiSite, scheduleSiteId, state.siteId, scheduleSiteName);

  // Determine whether the edited request has children, to gate the leaf (Task) option.
  // Only groups can have children, so we skip the lookup for create mode and leaf
  // requests — and skip it entirely when the caller passed the full tree, since
  // `directChildren` (derived from it) already answers this without a round trip.
  const needsChildrenLookup = open && !!request && request.planningMode !== 'leaf' && !allRequests;
  const lookupRequestId = needsChildrenLookup ? request.id : null;
  useEffect(() => {
    if (!lookupRequestId) return;
    let cancelled = false;
    getRequestChildren(lookupRequestId)
      .then((children) => {
        if (!cancelled) setChildrenLookup({ requestId: lookupRequestId, hasChildren: children.length > 0 });
      })
      .catch((error: unknown) => { logger.error("Failed to load request children:", error); });
    return () => { cancelled = true; };
  }, [lookupRequestId]);
  const hasChildrenFromApi =
    !!lookupRequestId && childrenLookup?.requestId === lookupRequestId && childrenLookup.hasChildren;

  const hasChildren = allRequests ? directChildren.length > 0 : hasChildrenFromApi;

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

  const handleRequirementChange = (criterionId: string, patch: Partial<RequirementEntry>) => {
    updateRequirement(criterionId, patch);
  };

  const handleSubmit = async (e: React.SyntheticEvent<HTMLFormElement>) => {
    e.preventDefault();
    setValidationError(null);

    const formData = validateRequestForm(state);
    if (isRequestFormValidationError(formData)) {
      // Surface the error on the tab that owns the offending field.
      setActiveTab(formData.tab);
      setValidationError(formData.message);
      return;
    }

    setIsSaving(true);
    try {
      const saved = await onSave(formData);
      const { startTs } = formData;
      // The backend snaps a start that falls outside working time, on a weekend, or into a
      // resource's absence — and used to do it silently, so a request typed for Sunday simply
      // appeared on Monday. Said once here, where every typed date is submitted.
      if (saved && typeof saved === 'object' && startTs && saved.startTs
          && new Date(saved.startTs).getTime() !== new Date(startTs).getTime()) {
        toast.info(`Moved to ${formatDateDisplay(saved.startTs)}`, {
          description: "The time you chose is outside working hours or overlaps time off.",
        });
      }
      // Create mode, group: create the queued children and reparent the queued existing
      // requests under the new group.
      if (!request && isGroup && saved && typeof saved === 'object') {
        await childrenTab.commitPending(saved.id);
      }
      onOpenChange(false);
    } catch (error) {
      logger.error("Failed to save request:", error);
      setValidationError(errorMessage(error));
    } finally {
      setIsSaving(false);
    }
  };


  return {
    state, setField, sites, isMultiSite, availableCriteria, requirementTypeKeys, availableTemplates, isLoading,
    isChildCreation, readOnly, isLeaf, isGroup, isContainer, typeChoice, setTypeChoice,
    setGroupBoundaryMode, hasEditableSchedule, hasChildren, showChildrenTab, showDependenciesTab,
    activeTab, setActiveTab, validationError, isSaving, handleSubmit, nameInputRef,
    conflictsByResourceId, conflictsByCriterionId, resourceConflictDot, requirementConflictDot,
    selectedCriterionId, setSelectedCriterionId, handleAddRequirement, handleRemoveRequirement,
    handleRequirementChange, handleApplyTemplate, hasPeopleBlockers, setHasPeopleBlockers,
    breadcrumb, directChildren, derivedValues, dependencyCandidates, childrenTab,
    hasDurationWarning, windowMinutes, siteScopeWarning,
    isDirty, setIsDirty, effectiveDirty, handleDialogClose, queuePlanNavigation, dropPlanNavigation,
  };
}
