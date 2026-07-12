import { ScaffoldDialog } from "@foundation/src/components/ui/ScaffoldDialog";
import { DialogFormFooter } from "@foundation/src/components/ui/DialogFormFooter";
import { DialogFooter } from "@foundation/src/components/ui/dialog";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@foundation/src/components/ui/tabs";
import { ErrorAlert } from "@foundation/src/components/ui/ErrorAlert";
import { Badge } from "@foundation/src/components/ui/badge";
import { Button } from "@foundation/src/components/ui/button";
import { Input } from "@foundation/src/components/ui/input";
import { Label } from "@foundation/src/components/ui/label";
import { RequestStatusBadge } from "@foundation/src/components/ui/RequestStatusBadge";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@foundation/src/components/ui/select";
import { Separator } from "@foundation/src/components/ui/separator";
import { Textarea } from "@foundation/src/components/ui/textarea";
import { RequestIconSelector } from "@foundation/src/components/requests/RequestIconSelector";
import { getCriteria } from "@foundation/src/lib/api/criteria-api";
import { createRequest, getRequestChildren, moveRequest } from "@foundation/src/lib/api/request-api";
import { useSites, useIsMultiSite } from "@foundation/src/hooks/useSites";
import { getTemplates } from "@foundation/src/lib/api/template-api";
import { type Template } from "@foundation/src/types/templates";
import { useAppStore } from "@foundation/src/store/app-store";
import {
  VALIDATION_MESSAGES,
  PLANNING_MODE_CONFIG,
  SPACE_NONE_PLACEHOLDER,
  DEFAULT_DURATION_VALUE,
  DEFAULT_DURATION_UNIT,
  getPlanningModeIcon,
  getPlanningModeLabel,
  getRequestIcon,
} from "@foundation/src/constants";
import { combineDateTimeToISO, durationToMinutes, formatDuration, formatMinutesHuman } from "@foundation/src/lib/utils";
import { formatDateDisplay } from "@foundation/src/lib/formatters";
import {
  computeDerivedValues,
  getAncestorIds,
  getDirectChildren,
  getNextSortOrder,
} from "@foundation/src/domain/request-tree";
import { invalidateRequestData } from "@foundation/src/lib/core/invalidate-request-data";
import { Alert, AlertDescription } from "@foundation/src/components/ui/alert";
import type { Criterion } from "@foundation/src/types/criterion";
import type { RequirementEntry } from "@foundation/src/hooks/useRequestForm";
import type { Conflict, Duration, DurationUnit, PlanningMode, Request } from "@foundation/src/types/requests";
import { ConflictBanner, ConflictIndicator, conflictDotClass } from "./ConflictIndicator";
import { TabIndicatorDot } from "@foundation/src/components/ui/status-indicator";
import type { Space } from "@foundation/src/types/space";
import { AlertTriangle, ChevronRight, FileText, Layers, Link, MapPin, Plus, Search, Trash2 } from "lucide-react";
import { Fragment, useEffect, useMemo, useRef, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useVirtualizer } from "@tanstack/react-virtual";
import { toast } from "sonner";
import { qk } from "@foundation/src/lib/api/query-keys";
import { CRITERIA_QUERY_KEY } from "@foundation/src/hooks/useCriteria";
import { useSpaces } from "@foundation/src/hooks/useSpaces";

const EMPTY_CRITERIA: Criterion[] = [];
const EMPTY_TEMPLATES: Template[] = [];
const EMPTY_SPACES: Space[] = [];

type RequestFormTab = 'details' | 'timing' | 'requirements' | 'resources' | 'children';

/** Sentinel for the "Any site" (site-neutral) option — Radix Select disallows empty values. */
const ANY_SITE = '__any_site__';
import { useRequestForm, type DefaultSchedule } from "@foundation/src/hooks/useRequestForm";
import { useDialogDirtyGuard } from "@foundation/src/hooks/useDialogDirtyGuard";
import { Checkbox } from "@foundation/src/components/ui/checkbox";
import { ScrollArea } from "@foundation/src/components/ui/scroll-area";
import { RequestScheduleSection } from "./RequestScheduleSection";
import { RequestConstraintsSection } from "./RequestConstraintsSection";
import { RequestRequirementsSection } from "./RequestRequirementsSection";
import { RequestPeopleSection } from "./RequestPeopleSection";
import { logger } from "@foundation/src/lib/core/logger";

interface RequestFormDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  request?: Request | null;
  parentRequest?: Request | null;
  defaultPlanningMode?: PlanningMode;
  /** Seed start/end (e.g. a calendar slot selection). Overrides any schedule on `request`. */
  defaultSchedule?: DefaultSchedule;
  /**
   * Site whose calendar/schedule this request is being placed on. Pre-selects a
   * site-neutral request to it and warns when the chosen site won't surface here.
   */
  scheduleSiteId?: string | null;
  /** Saved conflicts for this request (from the registry); surfaced as form indicators. */
  conflicts?: Conflict[];
  /**
   * When false, the dialog is a read-only VIEW surface: every field is disabled,
   * the footer is a single Close button, and the mutation controls (Children
   * add/remove, People add/remove) are hidden. Defaults to true (edit mode).
   */
  canEdit?: boolean;
  /**
   * The full request tree. Drives the ancestor breadcrumb, the Children tab, and
   * the group derived-schedule rollups. When undefined those features hide.
   */
  allRequests?: Request[];
  /** Re-target the dialog to another request (breadcrumb / children click). */
  onNavigate?: (requestId: string) => void;
  /**
   * Create-mode consumers should return the created Request so queued children
   * from the Children tab can be created under it. May return void otherwise.
   */
  onSave: (data: RequestFormData) => void | Request | Promise<void | Request>;
}

export interface RequestFormData {
  name: string;
  description?: string;
  icon?: string | null;
  planningMode: PlanningMode;
  parentRequestId?: string;
  /** Site scope. null/undefined = site-neutral (Any site). */
  siteId?: string | null;
  resourceId?: string;
  startTs?: string;
  endTs?: string;
  earliestStartTs?: string;
  latestEndTs?: string;
  duration: Duration;
  schedulingSettingsApply: boolean;
  requirements: {
    criterionId: string;
    value: RequirementEntry['value'];
    operator?: string;
  }[];
}

/**
 * @internal Exported for unit testing. Returns a warning when the chosen site
 * won't surface on the schedule the user is placing this request on — a
 * different site, or site-neutral ("Any site" isn't placed on any calendar).
 * Returns null when there's nothing to warn about (single-site, no schedule
 * context, or the site already matches).
 */
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

export function RequestFormDialog({
  open,
  onOpenChange,
  request,
  parentRequest,
  defaultPlanningMode,
  defaultSchedule,
  scheduleSiteId,
  conflicts = [],
  canEdit = true,
  allRequests,
  onNavigate,
  onSave,
}: RequestFormDialogProps) {
  const selectedSiteId = useAppStore((state) => state.selectedSiteId);
  const { data: sites = [] } = useSites();
  const isMultiSite = useIsMultiSite();
  const isChildCreation = !request && !!parentRequest;
  const readOnly = !canEdit;
  const queryClient = useQueryClient();

  // Use the custom hook for form state management
  const {
    state,
    setField: setFieldRaw,
    addRequirement: addRequirementRaw,
    removeRequirement: removeRequirementRaw,
    updateRequirement: updateRequirementRaw,
    applyTemplate: applyTemplateRaw,
  } = useRequestForm(request, parentRequest?.id, defaultPlanningMode, defaultSchedule, selectedSiteId, scheduleSiteId);

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
  const spaceConflicts = state.selectedResourceId
    ? conflictsByResourceId.get(state.selectedResourceId) ?? []
    : [];
  // Tab dot colour reflects the worst severity of conflicts owned by that tab
  // (error → red, warning-only → amber), matching the per-row indicators.
  const resourceConflictDot = conflictDotClass(conflicts.filter((c) => c.resourceId));
  const requirementConflictDot = conflictDotClass(conflicts.filter((c) => c.criterionId));

  // Reference data for the form — fetched only while the dialog is open and
  // cached under the shared keys, so other surfaces reuse the same data.
  const { data: availableCriteria = EMPTY_CRITERIA, isLoading: criteriaLoading } = useQuery({
    queryKey: CRITERIA_QUERY_KEY,
    queryFn: () => getCriteria(),
    enabled: open,
  });
  const { data: availableTemplates = EMPTY_TEMPLATES, isLoading: templatesLoading } = useQuery({
    queryKey: qk.templates('request'),
    queryFn: () => getTemplates('request'),
    enabled: open,
  });
  const { data: availableSpaces = EMPTY_SPACES, isLoading: spacesLoading } =
    useSpaces(open ? selectedSiteId : null);
  const isLoading = criteriaLoading || templatesLoading || spacesLoading;

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
  const [hasChildrenFromApi, setHasChildrenFromApi] = useState(false);
  // Inline quick-add child name (Children tab).
  const [newChildName, setNewChildName] = useState("");
  const [isAddingChild, setIsAddingChild] = useState(false);
  // Create mode only: child names queued on the Children tab, created under the
  // new group right after it is saved.
  const [pendingChildren, setPendingChildren] = useState<string[]>([]);
  // Create mode only: existing request ids queued to reparent under the new
  // group once it is saved.
  const [pendingExistingIds, setPendingExistingIds] = useState<string[]>([]);
  // Children tab inline "Add existing" picker (edit mode only): pull parentless
  // requests into this group.
  const [addExistingOpen, setAddExistingOpen] = useState(false);
  const [addExistingSelected, setAddExistingSelected] = useState<Set<string>>(new Set());
  const [addExistingSearch, setAddExistingSearch] = useState("");
  const [isAddingExisting, setIsAddingExisting] = useState(false);

  /** Surface a validation error on the tab that owns the offending field. */
  const failValidation = (tab: RequestFormTab, message: string) => {
    setActiveTab(tab);
    setValidationError(message);
  };

  // Track unsaved-changes state. Flips to true on first user interaction with
  // any form input; reset when the dialog is reopened.
  const [isDirty, setIsDirty] = useState(false);
  useEffect(() => {
    if (open) {
      setIsDirty(false);
      setActiveTab('details');
      setPendingChildren([]);
      setPendingExistingIds([]);
      setNewChildName("");
      setAddExistingOpen(false);
      setAddExistingSelected(new Set());
      setAddExistingSearch("");
    }
  }, [open]);

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

  // Children-tab actions are immediate & committed in edit mode (not "unsaved
  // form changes"), so they don't set `isDirty` — see the Children TabsContent's
  // event-propagation guard below. In create mode, though, queued children/
  // existing requests ARE unsaved state that Discard legitimately drops, so
  // fold them in here.
  const hasPendingCreate = !request && (pendingChildren.length > 0 || pendingExistingIds.length > 0);
  const effectiveDirty = isDirty || hasPendingCreate;

  const { guardedOnOpenChange, confirmOpen, ConfirmDiscardDialog } = useDialogDirtyGuard({
    isDirty: effectiveDirty,
    onOpenChange,
  });

  // Leaf: fully editable schedule.
  // Summary/Container: structural nodes; schedule is derived from children and not editable.
  const isLeaf = state.planningMode === 'leaf';
  const isContainer = state.planningMode === 'container';
  const isGroup = !isLeaf;
  const hasEditableSchedule = isLeaf;
  const hasEditableConstraints = isLeaf || isContainer;

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

  /** Shared defaults for children created from the Children tab. */
  const createChildRequest = (parentRequestId: string, name: string, sortOrder: number) =>
    createRequest({
      parentRequestId,
      name,
      planningMode: 'leaf',
      sortOrder,
      minimalDurationValue: DEFAULT_DURATION_VALUE,
      minimalDurationUnit: DEFAULT_DURATION_UNIT as DurationUnit,
    });

  const handleAddChild = async () => {
    const name = newChildName.trim();
    if (!name) return;
    // Create mode: queue locally; children are created together with the group.
    if (!request) {
      setPendingChildren((prev) => [...prev, name]);
      setNewChildName("");
      return;
    }
    if (!allRequests) return;
    setIsAddingChild(true);
    setValidationError(null);
    try {
      await createChildRequest(request.id, name, getNextSortOrder(request.id, allRequests));
      invalidateRequestData(queryClient);
      setNewChildName("");
    } catch (error) {
      logger.error("Failed to add child request:", error);
      setValidationError(error instanceof Error ? error.message : "Failed to add child request");
    } finally {
      setIsAddingChild(false);
    }
  };

  // Eligible existing requests for the inline "Add existing" picker: parentless
  // ("not in a group yet"), not this request, not an ancestor (no cycle), and
  // not already queued. Parentless keeps both Tasks and Groups while excluding
  // current children. Computed only while the picker is open, and O(n): a
  // parentless candidate can only create a cycle if it's one of this request's
  // ancestors (its root), so we exclude the ancestor set instead of running a
  // per-candidate descendant walk.
  const addExistingBase = useMemo(() => {
    if (!addExistingOpen || !allRequests) return [] as Request[];
    const ancestors = request
      ? new Set(getAncestorIds(request.id, allRequests, requestsById))
      : new Set<string>();
    return allRequests.filter((r) =>
      !r.parentRequestId &&
      r.id !== request?.id &&
      !ancestors.has(r.id) &&
      !pendingExistingIds.includes(r.id),
    );
  }, [addExistingOpen, allRequests, request, pendingExistingIds, requestsById]);

  const addExistingCandidates = useMemo(() => {
    const q = addExistingSearch.trim().toLowerCase();
    if (!q) return addExistingBase;
    return addExistingBase.filter(
      (r) => r.name.toLowerCase().includes(q) || r.description?.toLowerCase().includes(q),
    );
  }, [addExistingBase, addExistingSearch]);

  // Virtualize the candidate list so the picker opens instantly regardless of
  // how many requests the tenant has (only ~15 rows mount at once).
  const addExistingViewportRef = useRef<HTMLDivElement>(null);
  const addExistingVirtualizer = useVirtualizer({
    count: addExistingCandidates.length,
    getScrollElement: () => addExistingViewportRef.current,
    estimateSize: () => 44,
    overscan: 8,
  });

  // Existing requests queued in create mode, resolved for display in the
  // "to be added" list. Order follows pendingExistingIds.
  const pendingExistingRequests = useMemo(() => {
    if (!pendingExistingIds.length || !allRequests) return [] as Request[];
    return pendingExistingIds.map((id) => requestsById.get(id)).filter(Boolean) as Request[];
  }, [pendingExistingIds, allRequests, requestsById]);

  const toggleAddExistingSelected = (id: string) => {
    setAddExistingSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const closeAddExisting = () => {
    setAddExistingSelected(new Set());
    setAddExistingSearch("");
    setAddExistingOpen(false);
  };

  const handleAddExisting = async () => {
    if (addExistingSelected.size === 0) return;
    // Create mode: no group id yet — queue and reparent on save.
    if (!request) {
      setPendingExistingIds((prev) => [...prev, ...[...addExistingSelected].filter((id) => !prev.includes(id))]);
      closeAddExisting();
      return;
    }
    if (!allRequests) return;
    setIsAddingExisting(true);
    setValidationError(null);
    try {
      // allRequests only refreshes after the post-loop invalidate, so compute
      // the base once and offset per item — otherwise every move gets the same
      // sortOrder.
      const base = getNextSortOrder(request.id, allRequests);
      for (const [index, id] of [...addExistingSelected].entries()) {
        await moveRequest(id, {
          newParentRequestId: request.id,
          sortOrder: base + index,
        });
      }
      invalidateRequestData(queryClient);
      closeAddExisting();
    } catch (error) {
      logger.error("Failed to add existing requests:", error);
      setValidationError(error instanceof Error ? error.message : "Failed to add existing requests");
    } finally {
      setIsAddingExisting(false);
    }
  };

  const handleRemoveChild = async (child: Request) => {
    if (!allRequests) return;
    setValidationError(null);
    // Reparent to root (reversible). sortOrder = end of the current root list.
    const rootSiblings = allRequests.filter((r) => !r.parentRequestId);
    const sortOrder = rootSiblings.length ? Math.max(...rootSiblings.map((r) => r.sortOrder)) + 1 : 0;
    try {
      await moveRequest(child.id, { newParentRequestId: null, sortOrder });
      invalidateRequestData(queryClient);
    } catch (error) {
      logger.error("Failed to remove child from group:", error);
      setValidationError(error instanceof Error ? error.message : "Failed to remove child from group");
    }
  };

  const typeChoice: 'leaf' | 'group' = isLeaf ? 'leaf' : 'group';

  const setTypeChoice = (value: 'leaf' | 'group') => {
    if (value === 'leaf') {
      setField('planningMode', 'leaf');
      // A task can't have children — drop any names queued while it was a group.
      setPendingChildren([]);
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
  useEffect(() => {
    if (!open || !request || request.planningMode === 'leaf' || allRequests) {
      setHasChildrenFromApi(false);
      return;
    }
    let cancelled = false;
    getRequestChildren(request.id)
      .then((children) => { if (!cancelled) setHasChildrenFromApi(children.length > 0); })
      .catch((error: unknown) => { logger.error("Failed to load request children:", error); });
    return () => { cancelled = true; };
  }, [open, request, allRequests]);

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

    if (!state.name.trim()) {
      failValidation('details', VALIDATION_MESSAGES.REQUEST_NAME_REQUIRED);
      return;
    }

    if (hasEditableSchedule && (!state.durationValue || state.durationValue < 1)) {
      failValidation('timing', VALIDATION_MESSAGES.DURATION_REQUIRED);
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
      failValidation('timing', VALIDATION_MESSAGES.END_BEFORE_START);
      return;
    }

    // If one is provided but not the other, show error
    if ((startTs && !endTs) || (!startTs && endTs)) {
      failValidation('timing', VALIDATION_MESSAGES.DATES_MUST_BE_TOGETHER);
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
      failValidation('timing', VALIDATION_MESSAGES.CONSTRAINT_ORDER);
      return;
    }

    // Validate scheduled dates are within constraints
    if (earliestStartTs && startTs && new Date(startTs) < new Date(earliestStartTs)) {
      failValidation('timing', VALIDATION_MESSAGES.START_BEFORE_CONSTRAINT);
      return;
    }

    if (latestEndTs && endTs && new Date(endTs) > new Date(latestEndTs)) {
      failValidation('timing', VALIDATION_MESSAGES.END_AFTER_CONSTRAINT);
      return;
    }

    const formData: RequestFormData = {
      name: state.name.trim(),
      description: state.description.trim() || undefined,
      icon: state.icon ?? null,
      planningMode: state.planningMode,
      parentRequestId: state.parentRequestId || undefined,
      siteId: state.siteId || null,
      resourceId: isLeaf ? (state.selectedResourceId || undefined) : undefined,
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
        .filter(([, entry]) => entry.value !== null)
        .map(([criterionId, entry]) => ({
          criterionId,
          value: entry.value!,
          operator: entry.operator,
        })),
    };

    setIsSaving(true);
    try {
      const saved = await onSave(formData);
      // Create mode, group: create the queued new children and reparent the
      // queued existing requests under the new group. Failures are surfaced per
      // item via toast — the group and whatever succeeded so far are kept; the
      // user can re-add the rest from the group's dialog.
      if (!request && isGroup && saved && typeof saved === 'object') {
        let touched = false;
        for (const [index, childName] of pendingChildren.entries()) {
          try {
            await createChildRequest(saved.id, childName, index);
            touched = true;
          } catch (error) {
            logger.error("Failed to create queued child request:", error);
            toast.error(`Failed to create child "${childName}"`, {
              description: error instanceof Error ? error.message : undefined,
            });
          }
        }
        for (const [index, existingId] of pendingExistingIds.entries()) {
          try {
            await moveRequest(existingId, {
              newParentRequestId: saved.id,
              sortOrder: pendingChildren.length + index,
            });
            touched = true;
          } catch (error) {
            logger.error("Failed to reparent queued request:", error);
            const name = allRequests?.find((r) => r.id === existingId)?.name ?? "request";
            toast.error(`Failed to add "${name}"`, {
              description: error instanceof Error ? error.message : undefined,
            });
          }
        }
        if (touched) invalidateRequestData(queryClient);
      }
      onOpenChange(false);
    } catch (error) {
      logger.error("Failed to save request:", error);
      setValidationError(error instanceof Error ? error.message : "Failed to save request");
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <>
    <ScaffoldDialog
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
              ? `Adding a child request under "${parentRequest.name}".`
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
            <div className="mx-6 shrink-0 overflow-x-auto">
              <TabsList className="w-max min-w-full">
                <TabsTrigger value="details" className="shrink-0">Details</TabsTrigger>
                <TabsTrigger value="timing" className="relative shrink-0">
                  Timing
                  <TabIndicatorDot dotClass={hasDurationWarning ? "bg-amber-500" : null} label="timing warning" />
                </TabsTrigger>
                <TabsTrigger value="requirements" className="relative shrink-0">
                  Requirements
                  <TabIndicatorDot dotClass={requirementConflictDot} label="requirement conflict" />
                </TabsTrigger>
                {isLeaf && (
                  <TabsTrigger value="resources" className="relative shrink-0">
                    Resources
                    <TabIndicatorDot dotClass={resourceConflictDot} label="resource conflict" />
                  </TabsTrigger>
                )}
                {showChildrenTab && (
                  <TabsTrigger value="children" className="shrink-0">Children</TabsTrigger>
                )}
              </TabsList>
            </div>

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
                          placeholder="e.g., Product Launch Event"
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

              {/* RESOURCES — leaf only. forceMount + conditional hidden keeps pending rows and
                  blocker state alive across tab switches (Radix would otherwise unmount it). */}
              {isLeaf && (
                <TabsContent
                  value="resources"
                  forceMount
                  className={activeTab === 'resources' ? 'mt-0 space-y-6' : 'mt-0 hidden'}
                >
                  {/* Space */}
                  <div>
                    <h4 className="text-sm font-medium flex items-center gap-2">
                      <MapPin className="h-4 w-4" />
                      Space
                      <ConflictIndicator conflicts={spaceConflicts} />
                    </h4>
                    <div className="space-y-2 pt-4">
                      <Select
                        value={state.selectedResourceId || SPACE_NONE_PLACEHOLDER}
                        onValueChange={(value) => setField('selectedResourceId', value === SPACE_NONE_PLACEHOLDER ? "" : value)}
                        disabled={readOnly}
                      >
                        <SelectTrigger id="resourceId">
                          <SelectValue placeholder="No space assigned (unscheduled)" />
                        </SelectTrigger>
                        <SelectContent>
                          <SelectItem value={SPACE_NONE_PLACEHOLDER}>
                            <span className="text-muted-foreground">No space (unscheduled)</span>
                          </SelectItem>
                          {availableSpaces.map((space) => (
                            <SelectItem key={space.id} value={space.id}>
                              {space.name}
                            </SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                    </div>
                  </div>

                  <Separator />

                  {/* People */}
                  <RequestPeopleSection
                    requestId={request?.id}
                    requestStartTs={
                      hasEditableSchedule && state.startDate && state.startTime
                        ? (() => { try { return combineDateTimeToISO(state.startDate, state.startTime); } catch { return undefined; } })()
                        : undefined
                    }
                    requestEndTs={
                      hasEditableSchedule && state.endDate && state.endTime
                        ? (() => { try { return combineDateTimeToISO(state.endDate, state.endTime); } catch { return undefined; } })()
                        : undefined
                    }
                    onBlockersChange={setHasPeopleBlockers}
                    conflictsByResourceId={conflictsByResourceId}
                    readOnly={readOnly}
                  />
                </TabsContent>
              )}

              {/* CHILDREN — groups only, needs the tree. List (both modes) + inline
                  quick-add / remove-from-group (edit mode only). */}
              {showChildrenTab && (
                <TabsContent
                  value="children"
                  className="mt-0 space-y-3"
                  // Children-tab controls (quick-add name, add-existing search /
                  // checkboxes) are transient scratch or immediate committed
                  // actions — not edits to the request's own fields. Stop their
                  // input/change events from bubbling to the form-level
                  // setIsDirty handler, so managing children doesn't raise a
                  // false "Discard changes?" prompt on close.
                  onInput={(e) => e.stopPropagation()}
                  onChange={(e) => e.stopPropagation()}
                >
                  {/* Quick-add first so it stays visible however long the list gets. */}
                  {!readOnly && (
                    <div className="flex gap-2">
                      <Input
                        value={newChildName}
                        onChange={(e) => setNewChildName(e.target.value)}
                        placeholder="New child name"
                        className="flex-1"
                        data-testid="new-child-name"
                        onKeyDown={(e) => {
                          if (e.key === 'Enter') {
                            e.preventDefault();
                            void handleAddChild();
                          }
                        }}
                      />
                      <Button
                        type="button"
                        size="sm"
                        disabled={!newChildName.trim() || isAddingChild}
                        onClick={handleAddChild}
                        data-testid="add-child-btn"
                      >
                        <Plus className="h-4 w-4 mr-1" />
                        Add
                      </Button>
                    </div>
                  )}

                  {/* Add existing — pull parentless requests into this group.
                      Inline (no nested modal). Edit mode moves immediately;
                      create mode queues until the group is saved. */}
                  {!readOnly && (
                    <div>
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        onClick={() => setAddExistingOpen((v) => !v)}
                        data-testid="add-existing-toggle"
                        aria-expanded={addExistingOpen}
                      >
                        <Link className="h-4 w-4 mr-1" />
                        Add existing
                        <ChevronRight
                          className={`h-4 w-4 ml-1 transition-transform ${addExistingOpen ? 'rotate-90' : ''}`}
                        />
                      </Button>

                      {addExistingOpen && (
                        <div className="mt-2 space-y-2 rounded-md border p-2">
                          <div className="relative">
                            <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
                            <Input
                              placeholder="Search requests…"
                              aria-label="Search requests to add"
                              value={addExistingSearch}
                              onChange={(e) => setAddExistingSearch(e.target.value)}
                              className="pl-9"
                            />
                          </div>

                          <ScrollArea type="auto" viewportRef={addExistingViewportRef} className="h-[200px] rounded-md border">
                            {addExistingCandidates.length === 0 ? (
                              <div className="flex h-[200px] items-center justify-center p-4 text-sm text-muted-foreground">
                                {addExistingSearch ? "No matching requests" : "No unassigned requests available."}
                              </div>
                            ) : (
                              <div
                                className="relative w-full p-1"
                                style={{ height: `${addExistingVirtualizer.getTotalSize()}px` }}
                              >
                                {addExistingVirtualizer.getVirtualItems().map((vi) => {
                                  const r = addExistingCandidates[vi.index];
                                  const Icon = getPlanningModeIcon(r.planningMode);
                                  const checked = addExistingSelected.has(r.id);
                                  return (
                                    // The wrapping label forwards clicks anywhere on the row to
                                    // the checkbox (Radix renders a <button>, which is a
                                    // labelable element), so the whole row is the click target.
                                    // NOTE: do NOT convert this to a div with onClick — that
                                    // triggers an infinite render loop via the Radix ScrollArea
                                    // ref (reproduced in Chromium; see requests-dialog-visual
                                    // row-click spec).
                                    <label
                                      key={r.id}
                                      className={`absolute left-0 top-0 flex w-full items-center gap-3 rounded-md px-3 py-2 cursor-pointer hover:bg-muted/50 transition-colors ${checked ? 'bg-muted' : ''}`}
                                      style={{ height: `${vi.size}px`, transform: `translateY(${vi.start}px)` }}
                                    >
                                      <Checkbox
                                        checked={checked}
                                        aria-label={r.name}
                                        onCheckedChange={() => toggleAddExistingSelected(r.id)}
                                      />
                                      <Icon className="h-4 w-4 text-muted-foreground flex-shrink-0" />
                                      <span className="flex-1 truncate text-sm font-medium">{r.name}</span>
                                      <Badge variant="outline" className="text-[10px] flex-shrink-0">
                                        {getPlanningModeLabel(r.planningMode)}
                                      </Badge>
                                    </label>
                                  );
                                })}
                              </div>
                            )}
                          </ScrollArea>

                          <div className="flex justify-end">
                            <Button
                              type="button"
                              size="sm"
                              disabled={addExistingSelected.size === 0 || isAddingExisting}
                              onClick={handleAddExisting}
                              data-testid="add-existing-confirm"
                            >
                              Add {addExistingSelected.size > 0 ? addExistingSelected.size : ''}
                            </Button>
                          </div>
                        </div>
                      )}
                    </div>
                  )}

                  {/* Create mode: locally queued children, created with the group. */}
                  {!request ? (
                    pendingChildren.length === 0 && pendingExistingRequests.length === 0 ? (
                      <p className="text-sm text-muted-foreground">
                        Child tasks added here are created together with this group.
                      </p>
                    ) : (
                      <div className="space-y-1">
                        {pendingChildren.map((childName, index) => {
                          const PendingIcon = getPlanningModeIcon('leaf');
                          return (
                            <div
                              key={`new-${childName}-${index}`}
                              className="flex items-center gap-2 rounded px-2 py-1.5 text-sm hover:bg-muted transition-colors"
                            >
                              <PendingIcon className="h-3.5 w-3.5 text-muted-foreground flex-shrink-0" />
                              <span className="flex-1 min-w-0 truncate">{childName}</span>
                              <Button
                                type="button"
                                variant="ghost"
                                size="sm"
                                aria-label={`Remove ${childName}`}
                                onClick={() =>
                                  setPendingChildren((prev) => prev.filter((_, i) => i !== index))
                                }
                              >
                                <Trash2 className="h-4 w-4" />
                              </Button>
                            </div>
                          );
                        })}
                        {pendingExistingRequests.map((r) => {
                          const PendingIcon = getRequestIcon(r.icon) ?? getPlanningModeIcon(r.planningMode);
                          return (
                            <div
                              key={`existing-${r.id}`}
                              className="flex items-center gap-2 rounded px-2 py-1.5 text-sm hover:bg-muted transition-colors"
                            >
                              <PendingIcon className="h-3.5 w-3.5 text-muted-foreground flex-shrink-0" />
                              <span className="flex-1 min-w-0 truncate">{r.name}</span>
                              <Badge variant="outline" className="text-[10px] flex-shrink-0">
                                {getPlanningModeLabel(r.planningMode)}
                              </Badge>
                              <Button
                                type="button"
                                variant="ghost"
                                size="sm"
                                aria-label={`Remove ${r.name}`}
                                onClick={() =>
                                  setPendingExistingIds((prev) => prev.filter((id) => id !== r.id))
                                }
                              >
                                <Trash2 className="h-4 w-4" />
                              </Button>
                            </div>
                          );
                        })}
                      </div>
                    )
                  ) : directChildren.length === 0 ? (
                    <p className="text-sm text-muted-foreground">No children yet.</p>
                  ) : (
                    <div className="space-y-1">
                      {directChildren.map((child) => {
                        const ChildIcon = getRequestIcon(child.icon) ?? getPlanningModeIcon(child.planningMode);
                        return (
                          <div
                            key={child.id}
                            className="flex items-center gap-2 rounded px-2 py-1.5 text-sm hover:bg-muted transition-colors"
                          >
                            <button
                              type="button"
                              className="flex flex-1 min-w-0 items-center gap-2 text-left"
                              onClick={() => onNavigate?.(child.id)}
                            >
                              <ChildIcon className="h-3.5 w-3.5 text-muted-foreground flex-shrink-0" />
                              <span className="truncate">{child.name}</span>
                              <Badge variant="outline" className="text-[10px] flex-shrink-0">
                                {getPlanningModeLabel(child.planningMode)}
                              </Badge>
                              <RequestStatusBadge status={child.status} />
                            </button>
                            {!readOnly && (
                              <Button
                                type="button"
                                variant="ghost"
                                size="sm"
                                aria-label={`Remove ${child.name} from group`}
                                onClick={() => handleRemoveChild(child)}
                              >
                                <Trash2 className="h-4 w-4" />
                              </Button>
                            )}
                          </div>
                        );
                      })}
                    </div>
                  )}
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
    </ScaffoldDialog>
    {ConfirmDiscardDialog}
    </>
  );
}
