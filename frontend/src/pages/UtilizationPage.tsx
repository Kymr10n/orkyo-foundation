import { CollapsibleFloorplan } from "@foundation/src/components/utilization/CollapsibleFloorplan";
import { ScaleSelect } from "@foundation/src/components/utilization/ScaleSelect";
import { TypeFilterSelect } from "@foundation/src/components/utilization/TypeFilterSelect";
import { ScheduleFilterBar } from "@foundation/src/components/utilization/ScheduleFilterBar";
import { StationGridLegend } from "@foundation/src/components/utilization/StationGridLegend";
import { AssetGridLegend } from "@foundation/src/components/utilization/AssetGridLegend";
import { ResourceGridFilterBar } from "@foundation/src/components/utilization/ResourceGridFilterBar";
import {
  EMPTY_RESOURCE_GRID_FILTER,
  type ResourceGridFilter,
} from "@foundation/src/components/utilization/resource-grid-filter";
import {
  ISSUE_FILTER_ORDER,
  filterScheduledRequests,
  type ScheduleFilter,
} from "@foundation/src/components/utilization/schedule-filter";
import { REQUEST_STATUS_ORDER } from "@foundation/src/constants/request-status";
import { useTypeFilter } from "@foundation/src/hooks/useTypeFilter";
import { SchedulerGrid } from "@foundation/src/components/utilization/SchedulerGrid";
import { TimeNavigator } from "@foundation/src/components/utilization/TimeNavigator";
import { TabsContent } from "@foundation/src/components/ui/tabs";
import { PageLayout, PageHeader, PageTabs, type PageTab } from "@foundation/src/components/layout";
import { RequestFormDialog, type RequestFormData } from "@foundation/src/components/requests/RequestFormDialog";
import type { DefaultResource } from "@foundation/src/hooks/useRequestForm";
import { useRequestEditor } from "@foundation/src/components/requests/useRequestEditor";
import { getPlacementResourceId, getTargetResourceTypeKeys } from "@foundation/src/domain/scheduling/request-assignments";
import { withEffectiveStatus } from "@foundation/src/domain/scheduling/effective-status";
import { useNow } from "@foundation/src/hooks/useNow";
import { usePageTitle } from "@foundation/src/hooks/usePageTitle";
import { useScheduledRequests, useBacklogRequests, useScheduleRequest, usePlaceableResources } from "@foundation/src/hooks/useUtilization";
import { usePlaceableTypeKeys } from "@foundation/src/hooks/usePlaceableResources";
import { generateTimeColumns, getFetchWindow, isAnchorStale } from "@foundation/src/components/utilization/time-grid-utils";
import { useCalendarFeedHandler, useExportHandler } from "@foundation/src/hooks/useImportExport";
import { useConflictRegistry } from "@foundation/src/hooks/useConflictRegistry";
import { usePreferences, useUpdatePreferences } from "@foundation/src/hooks/usePreferences";
import { useCanEdit } from "@foundation/src/hooks/usePermissions";
import { useSchedulingSettings, useAvailabilityEvents } from "@foundation/src/hooks/useScheduling";
import { useAutoScheduleAvailable, usePreviewAutoSchedule, useApplyAutoSchedule } from "@foundation/src/hooks/useAutoSchedule";
import { AutoScheduleButton } from "@foundation/src/components/utilization/AutoScheduleButton";
import { AutoSchedulePreviewDialog } from "@foundation/src/components/utilization/AutoSchedulePreviewDialog";
import { ResourceUtilizationGrid } from "@foundation/src/components/utilization/ResourceUtilizationGrid";
import { useBreakpoint } from "@foundation/src/hooks/useBreakpoint";
import { RequestCalendar } from "@foundation/src/components/utilization/RequestCalendar";
import { ScheduleSlotDialog } from "@foundation/src/components/utilization/ScheduleSlotDialog";
import { requestsToCalendarEvents, scaleToCalendarView } from "@foundation/src/components/utilization/request-calendar-events";
import type { AutoSchedulePreviewResponse } from "@foundation/src/lib/api/auto-schedule-api";
import { exportUtilization } from "@foundation/src/lib/utils/export-handlers";
import { createRequest, updateRequest } from "@foundation/src/lib/api/request-api";
import { logger } from "@foundation/src/lib/core/logger";
import { invalidateRequestData } from "@foundation/src/lib/core/invalidate-request-data";
import { buildCreatePayload, buildUpdatePayload } from "@foundation/src/lib/utils/utils";
import { expandRecurrence } from "@foundation/src/domain/scheduling/recurrence";
import { generateWeekendRanges } from "@foundation/src/domain/scheduling/weekend-ranges";
import { RESOURCE_TYPE_KEY } from "@foundation/src/constants/resource-type-key";
import { useResourceTypes } from "@foundation/src/hooks/useResourceTypes";
import { useAppStore } from "@foundation/src/store/app-store";
import { useSchedulerStore } from "@foundation/src/store/scheduler-store";
import { useShallow } from "zustand/react/shallow";
import type { OffTimeRange } from "@foundation/src/domain/scheduling/types";
import type { Request } from "@foundation/src/types/requests";
import type { ResourceInfo } from "@foundation/src/lib/api/resources-api";
import type { TimeColumn } from "@foundation/src/components/utilization/scheduler-types";
import { DndContext, DragOverlay, type CollisionDetection, type DragEndEvent, type DragStartEvent, KeyboardSensor, MouseSensor, TouchSensor, pointerWithin, useSensor, useSensors } from "@dnd-kit/core";
import { sortableKeyboardCoordinates } from "@dnd-kit/sortable";
import { resolveDropStartMs } from "@foundation/src/components/utilization/time-grid-utils";
import { DropPositionIndicator } from "@foundation/src/components/utilization/DropPositionIndicator";
import {
  REQUEST_BAR_BASE_CLASS,
  RequestBarLabel,
  RequestBarLayers,
  requestBarToneClass,
} from "@foundation/src/components/utilization/RequestBarVisual";
import { LoadingSpinner } from "@foundation/src/components/ui/LoadingSpinner";
import { toast } from "sonner";
import { useQueryClient } from "@tanstack/react-query";
import { addMonths, format, startOfMonth } from "date-fns";
import { DATE_FORMATS } from "@foundation/src/lib/formatters";
import { useEffect, useState, useCallback, useMemo } from "react";
import { useUiActionsStore } from "@foundation/src/store/ui-actions-store";
import { CalendarOff } from "lucide-react";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@foundation/src/components/ui/dropdown-menu";
import { useTabParam } from "@foundation/src/hooks/useTabParam";
import { navigateTime, navigateCalendarPeriod } from "@foundation/src/lib/utils/time-navigation";
import { errorMessage } from "@foundation/src/hooks/mutation-utils";

/** "Mills", "Mills and Drills", "Mills, Drills and Presses". */
function joinNames(names: string[]): string {
  if (names.length === 0) return 'resources';
  if (names.length === 1) return names[0];
  return `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`;
}

const STATIONS_TAB = 'stations';
const ASSETS_TAB = 'assets';

export function UtilizationPage() {
  usePageTitle("Utilization");
  const {
    scale, setScale,
    anchorTs, setAnchorTs,
    timeCursorTs, setTimeCursorTs,
    isFloorplanCollapsed, setIsFloorplanCollapsed,
    selectedSiteId,
  } = useAppStore(useShallow((state) => ({
    scale: state.scale,
    setScale: state.setScale,
    anchorTs: state.anchorTs,
    setAnchorTs: state.setAnchorTs,
    timeCursorTs: state.timeCursorTs,
    setTimeCursorTs: state.setTimeCursorTs,
    isFloorplanCollapsed: state.isFloorplanCollapsed,
    setIsFloorplanCollapsed: state.setIsFloorplanCollapsed,
    selectedSiteId: state.selectedSiteId,
  })));

  // Viewers get a read-only grid: with no canEdit, attach no drag sensors so
  // scheduling/reorder drags can't be initiated at all (writes also 403 server-side).
  const canEdit = useCanEdit();

  // Separate mouse + touch sensors so tap-to-open and drag-to-reschedule coexist
  // on every device. Mouse: 8px of movement before a drag (plain clicks open the
  // request, never re-schedule it). Touch: a 250ms press-hold before a drag, so a
  // quick tap falls through to onClick (opens the dialog) while a long-press
  // starts the drag. A unified PointerSensor can't give mouse and touch different
  // activation rules, which is why touch taps were being swallowed.
  const mouseSensor = useSensor(MouseSensor, {
    activationConstraint: { distance: 8 },
  });
  const touchSensor = useSensor(TouchSensor, {
    activationConstraint: { delay: 250, tolerance: 8 },
  });
  // KeyboardSensor makes the row-reorder drag (Spaces) keyboard-operable — that
  // path resolves its target purely from element ids, so it is keyboard-correct.
  // Request cards / scheduled bars intercept Enter/Space for their own "open"
  // affordance, so they never start a keyboard drag onto the time grid (whose
  // drop time is resolved from pointer coordinates); those users schedule via
  // the "Schedule to…" dialog instead.
  const keyboardSensor = useSensor(KeyboardSensor, {
    coordinateGetter: sortableKeyboardCoordinates,
  });
  const sensors = useSensors(...(canEdit ? [mouseSensor, touchSensor, keyboardSensor] : []));

  // Scope collision detection to the active drag's intent so a single set of
  // droppables can serve two modes unambiguously: dragging a space-row only
  // considers other space-row sortables (reorder); dragging a request considers
  // everything else (the per-row time tracks, the unschedule zone, tree-reparent
  // targets). Without this, the row-level track droppable and the row sortable
  // overlap and `over` would be non-deterministic.
  const collisionDetection = useCallback<CollisionDetection>((args) => {
    const activeType = args.active.data.current?.type;
    const containers = args.droppableContainers.filter((c) => {
      const t = c.data.current?.type;
      return activeType === "space-row" ? t === "space-row" : t !== "space-row";
    });
    return pointerWithin({ ...args, droppableContainers: containers });
  }, []);

  // The request currently being dragged. It drives the <DragOverlay>, which carries a
  // lightweight clone under the pointer so dnd-kit never transforms or reconciles the real
  // bar (and its subtree) on every pointer move — that is what kept a populated grid smooth.
  // Row-reorder drags (type "space-row") get no overlay.
  const [activeDragRequest, setActiveDragRequest] = useState<Request | null>(null);
  const handleDragStart = useCallback((event: DragStartEvent) => {
    const data = event.active.data.current as (Request & { type?: string }) | undefined;
    setActiveDragRequest(data && data.type !== "space-row" ? data : null);
  }, []);

  // Right-click target on a scheduled bar, in viewport coordinates. The menu anchors to a
  // zero-size span at the pointer, the same way the floorplan's shape menu does.
  const [requestMenu, setRequestMenu] = useState<{ id: string; x: number; y: number } | null>(null);
  const handleRequestContextMenu = useCallback((requestId: string, position: { x: number; y: number }) => {
    setRequestMenu({ id: requestId, ...position });
  }, []);

  // One grid tab per active resource type that is not placeable. Placeable types share the
  // scheduler tab (drag-to-schedule, floorplan, backlog) because they share one floorplan;
  // every other type — people, tools, anything a tenant defines — gets the read-mostly grid.
  //
  // Keyed on has_geometry rather than the space key: the floorplan read path never filtered by
  // type (GET /api/sites/{id}/spaces scopes on `rt.has_geometry AND r.is_active`), so a
  // tenant-defined placeable type already appeared on the floorplan while *also* getting a grid
  // tab of its own — the same resources listed twice, under two different surfaces.
  const { data: resourceTypes = [], isSuccess: resourceTypesLoaded } = useResourceTypes(true);
  const placeableTypes = useMemo(
    () => resourceTypes.filter((t) => t.hasGeometry),
    [resourceTypes],
  );
  const gridTypes = useMemo(() => {
    const rest = resourceTypes.filter((t) => !t.hasGeometry);
    // People first — the established second tab — then whatever order the API returns.
    return [
      ...rest.filter((t) => t.key === RESOURCE_TYPE_KEY.PERSON),
      ...rest.filter((t) => t.key !== RESOURCE_TYPE_KEY.PERSON),
    ];
  }, [resourceTypes]);

  // Tab state — persisted in URL so the view is bookmarkable. Three fixed values, so the set is
  // known without waiting for the types.
  const [rawTab, handleTabChange] = useTabParam('calendar');
  const isKnownTab =
    rawTab === 'calendar' || rawTab === STATIONS_TAB || rawTab === ASSETS_TAB;
  // Until the types load the valid set is unknown, so an unrecognised value is held rather
  // than replaced — otherwise a deep link to a real type tab flashes Calendar first.
  const activeTab = !resourceTypesLoaded || isKnownTab ? rawTab : 'calendar';

  // Correct the URL too. Rendering Calendar while ?tab= still names a type the tenant has
  // since deactivated leaves reload and the back button disagreeing with the screen.
  useEffect(() => {
    if (resourceTypesLoaded && !isKnownTab) handleTabChange('calendar');
  }, [resourceTypesLoaded, isKnownTab, handleTabChange]);

  const isCalendarTab = activeTab === 'calendar';
  const isSchedulerTab = activeTab === STATIONS_TAB;
  const isAssetsTab = activeTab === ASSETS_TAB;

  // Which types each grid tab is showing. Held in the URL so a filtered view is shareable.
  const [stationKeys, setStationKeys] = useTypeFilter('stationTypes', placeableTypes);
  const [assetKeys, setAssetKeys] = useTypeFilter('assetTypes', gridTypes);
  const selectedKeys = isSchedulerTab ? stationKeys : assetKeys;
  const selectedAssetTypes = useMemo(
    () => gridTypes.filter((t) => assetKeys.includes(t.key)),
    [gridTypes, assetKeys],
  );

  // One solver run solves one type, and a grid tab now shows a set of them. So the button is
  // live exactly when the filter names a single type, which generalizes the old rule (enabled
  // when the tenant happened to have one placeable type) instead of guessing at a pool.
  const autoScheduleTypeKey =
    !isCalendarTab && selectedKeys.length === 1 ? selectedKeys[0] : null;

  // Stations first, then assets with people at their head — the PDF sections rows by type, and
  // this is the order the two grid tabs present them in.
  const orderedTypes = useMemo(
    () => [...placeableTypes, ...gridTypes],
    [placeableTypes, gridTypes],
  );

  // The export follows what is on screen: Calendar has no type of its own and exports every
  // type; a grid tab exports exactly the types its filter admits. Anything else hands back a PDF
  // that disagrees with the screen it came from.
  const exportTypes = useMemo(
    () => {
      if (isCalendarTab) return orderedTypes;
      return orderedTypes.filter((t) => selectedKeys.includes(t.key));
    },
    [isCalendarTab, orderedTypes, selectedKeys],
  );
  // Names what the PDF will contain. The scheduler tab can span several placeable types, and
  // naming only the first would misdescribe the file. A filter that admits everything says so rather than
  // enumerating the whole catalogue, and three or more types read as a list, not as a chain of
  // "and"s.
  const exportScopeLabel =
    isCalendarTab || exportTypes.length === orderedTypes.length
      ? 'all resources'
      : joinNames(exportTypes.map((t) => t.displayNamePlural));

  // Floorplan height state
  const [floorplanHeight, setFloorplanHeight] = useState(280);

  // Request dialog state
  const { open: openRequestEditor, dialogs: requestEditorDialogs } = useRequestEditor();
  // On phone the drag-based scheduler grid is replaced by a drag-free agenda.
  const { isPhone } = useBreakpoint();
  // Non-drag "Schedule to…" dialog target (keyboard-accessible scheduling path).
  const queryClient = useQueryClient();

  // Auto-schedule
  const autoScheduleAvailable = useAutoScheduleAvailable();
  const previewMutation = usePreviewAutoSchedule();
  const applyMutation = useApplyAutoSchedule();
  const [autoSchedulePreview, setAutoSchedulePreview] = useState<AutoSchedulePreviewResponse | null>(null);
  const [isPreviewDialogOpen, setIsPreviewDialogOpen] = useState(false);
  /**
   * Set when the run was scoped to specific requests (an accepted assistant proposal),
   * null for the page's own whole-site run. Apply must repeat it, or the backend re-solves
   * over a wider set than the preview showed.
   */
  const [autoScheduleRequestIds, setAutoScheduleRequestIds] = useState<string[] | null>(null);
  const [autoScheduleError, setAutoScheduleError] = useState<string | null>(null);


  // Fetch data from API — scoped to the selected site + a buffered window for the grid bars, plus
  // the tenant-wide unscheduled backlog (drag source). The panel/lookups use the union; the grid
  // bars come from the scoped scheduled set.
  const { data: allSpaces = [], isLoading: spacesLoading } = usePlaceableResources(selectedSiteId);
  // The plan itself keeps every station — a floorplan with half its shapes missing reads as a
  // broken drawing — so the filter narrows the grid rows only.
  const spaces = useMemo(
    () => allSpaces.filter((r) => stationKeys.includes(r.resourceTypeKey)),
    [allSpaces, stationKeys],
  );
  const placeableKeys = usePlaceableTypeKeys();
  const handleToday = useCallback(() => {
    setAnchorTs(new Date());
    setTimeCursorTs(new Date());
  }, [setAnchorTs, setTimeCursorTs]);

  // The store's default anchor is a `new Date()` frozen at module load; a tab left open across midnight
  // (or a long-lived HMR dev tab) drifts from the real today. Refresh a *stale* (past-day) anchor to now
  // — mirroring "Today" — on open and each time the tab regains focus/visibility (the natural signal that
  // the user returned), while leaving a current/future week the user navigated to untouched. Read the live
  // store anchor via getState() so the listeners never close over a stale value.
  useEffect(() => {
    const snapIfStale = () => {
      if (isAnchorStale(useAppStore.getState().anchorTs, new Date())) handleToday();
    };
    snapIfStale();
    const onVisible = () => {
      if (document.visibilityState === "visible") snapIfStale();
    };
    window.addEventListener("focus", snapIfStale);
    document.addEventListener("visibilitychange", onVisible);
    return () => {
      window.removeEventListener("focus", snapIfStale);
      document.removeEventListener("visibilitychange", onVisible);
    };
  }, [handleToday]);

  const fetchWindow = useMemo(() => getFetchWindow(scale, anchorTs), [scale, anchorTs]);
  const { data: rawScheduled = [], isLoading: scheduledLoading } = useScheduledRequests(selectedSiteId, fetchWindow.from, fetchWindow.to);
  const { data: rawBacklog = [] } = useBacklogRequests();
  // Recompute the active lifecycle (new → in_progress → done) live on the client off one ticking clock,
  // so the "In Progress" filter / badges track the grid's "Now" marker instead of the (frozen) fetch-time
  // value. cancelled/deferred pass through. withEffectiveStatus keeps the array ref stable until a flip.
  const nowMs = useNow();
  const scheduled = useMemo(() => withEffectiveStatus(rawScheduled, nowMs), [rawScheduled, nowMs]);
  const backlog = useMemo(() => withEffectiveStatus(rawBacklog, nowMs), [rawBacklog, nowMs]);
  const requests = useMemo(() => [...scheduled, ...backlog], [scheduled, backlog]);
  const requestsLoading = scheduledLoading;
  const scheduleMutation = useScheduleRequest();
  const { data: preferences } = usePreferences();
  const updatePreferencesMutation = useUpdatePreferences();

  // Scheduling off-times for grid overlay
  const { data: schedulingSettings } = useSchedulingSettings(selectedSiteId ?? undefined);
  const { data: availabilityEventDefs = [] } = useAvailabilityEvents(selectedSiteId ?? undefined);
  // Key the expansion window on the anchor's month, not the raw anchor: edge-scroll
  // updates anchorTs ~20×/s, and re-expanding recurrences on every tick made panning
  // churn. Panning within a month reuses the same array; the ±1/+13-month slack
  // around the month start still covers every visible window.
  const monthAnchorMs = startOfMonth(anchorTs).getTime();
  const offTimeRanges: readonly OffTimeRange[] = useMemo(() => {
    const tz = schedulingSettings?.timeZone ?? "UTC";
    const windowStart = addMonths(monthAnchorMs, -1).getTime();
    const windowEnd = addMonths(monthAnchorMs, 13).getTime();
    const expanded = availabilityEventDefs
      .filter((e) => e.enabled && e.defaultEffect === "closed")
      .flatMap((e) => expandRecurrence(
        {
          id: e.id,
          siteId: e.siteId,
          title: e.title,
          type: "custom" as const,
          appliesToAllSpaces: true,
          resourceIds: [],
          startMs: new Date(e.startTs).getTime(),
          endMs: new Date(e.endTs).getTime(),
          isRecurring: e.isRecurring,
          recurrenceRule: e.recurrenceRule ?? null,
          enabled: e.enabled,
        },
        windowStart, windowEnd, tz,
      ));

    // Weekends as off-time ranges (consistent with manual off-times)
    if (schedulingSettings && !schedulingSettings.weekendsEnabled) {
      expanded.push(...generateWeekendRanges(windowStart, windowEnd));
    }

    return expanded;
  }, [availabilityEventDefs, schedulingSettings, monthAnchorMs]);

  // Initialize space order from preferences
  const spaceOrder = useAppStore((state) => state.spaceOrder);
  const setSpaceOrder = useAppStore((state) => state.setSpaceOrder);
  useEffect(() => {
    if (preferences?.spaceOrder && spaceOrder.length === 0) {
      setSpaceOrder(preferences.spaceOrder);
    }
  }, [preferences, spaceOrder.length, setSpaceOrder]);

  // Conflict detection — backend is the single source of truth. Scope the registry to the visible
  // window (Calendar/Space) so it never evaluates the whole tenant all-time. A type grid tab
  // computes its own windowed conflicts, so skip the registry there entirely.
  const { conflictsByRequest: conflicts } = useConflictRegistry({
    from: fetchWindow.from,
    to: fetchWindow.to,
    enabled: !isAssetsTab,
  });
  const conflictingRequestIds = useMemo(() => new Set(conflicts.keys()), [conflicts]);

  // Search and filters for the stations grid. Local, like the calendar's: a query changes on every
  // keystroke, and writing that to the address bar would bury real navigation under typing history.
  const [stationFilter, setStationFilter] = useState<ScheduleFilter>({
    query: '',
    statuses: REQUEST_STATUS_ORDER,
    issues: ISSUE_FILTER_ORDER,
  });
  const visibleScheduled = useMemo(
    () => filterScheduledRequests(scheduled, stationFilter, conflicts),
    [scheduled, stationFilter, conflicts],
  );

  // One search and one filter for the whole Assets tab. Each stacked type grid gets them as a
  // prop: a box per grid would ask which of three identical boxes to type in, and could only ever
  // be labelled after one of the types.
  const [assetFilter, setAssetFilter] = useState<ResourceGridFilter>(EMPTY_RESOURCE_GRID_FILTER);

  // Calendar tab: scheduled requests projected to FullCalendar events, coloured by
  // status + conflict severity. Reuses the same scoped `scheduled` set as the grid.
  const calendarEvents = useMemo(
    () => requestsToCalendarEvents(scheduled, conflicts, canEdit),
    [scheduled, conflicts, canEdit],
  );

  // Empty-slot scheduling flow (calendar slot select + Spaces-grid cell click).
  // `resource` is set only for grid clicks: it labels the chooser, filters the
  // backlog, and routes "Schedule existing" through the grid schedule mutation.
  const [slotSelection, setSlotSelection] = useState<
    { start: Date; end: Date; resource?: { id: string; name: string; typeKey: string } } | null
  >(null);
  const [isSlotChooserOpen, setIsSlotChooserOpen] = useState(false);
  const [calendarForm, setCalendarForm] = useState<
    { mode: "create" | "edit"; request: Request | null; startTs: string; endTs: string; resource?: DefaultResource } | null
  >(null);

  // Handle export from TopBar
  useExportHandler('utilization', async (exportFormat) => {
    if (exportFormat === 'pdf') {
      // The grid's own columns ARE the visible period — snapped to week/month
      // starts etc., unlike a raw anchor+1-period window. (weekends/working
      // hours only annotate columns, so defaults give the same edges.)
      const columns = generateTimeColumns(scale, anchorTs);
      await exportUtilization(requests, columns[0].start, columns[columns.length - 1].end, exportTypes);
    }
  }, {
    label: `Utilization (${exportScopeLabel})`,
    description: `Export a PDF of the ${exportScopeLabel} schedule for the visible period.`,
    formats: ['pdf'],
  });

  // Registered for the page, not the Calendar tab: the feed serves the site's
  // whole schedule regardless of which visualization is on screen.
  useCalendarFeedHandler('utilization', {
    label: 'Utilization schedule',
    description: 'Add this schedule to Outlook, Google Calendar or Apple Calendar. The calendar updates itself — you subscribe once and it stays current.',
  });

  // Auto-schedule handlers
  const AUTO_SCHEDULE_HORIZON_MONTHS = 3;
  const horizonStart = format(anchorTs, DATE_FORMATS.DATE_ISO);
  const horizonEnd = format(addMonths(anchorTs, AUTO_SCHEDULE_HORIZON_MONTHS), DATE_FORMATS.DATE_ISO);

  const handleAutoScheduleClick = useCallback(async () => {
    if (!selectedSiteId) return;
    try {
      const result = await previewMutation.mutateAsync({
        siteId: selectedSiteId,
        horizonStart,
        horizonEnd,
        resourceTypeKey: autoScheduleTypeKey ?? undefined,
      });
      setAutoScheduleRequestIds(null);
      setAutoSchedulePreview(result);
      setIsPreviewDialogOpen(true);
    } catch {
      // Error handled by mutation state
    }
  }, [selectedSiteId, horizonStart, horizonEnd, autoScheduleTypeKey, previewMutation]);

  // An accepted auto-scheduling proposal lands here: preview exactly the requests the
  // person approved and open the ordinary dialog. Keyed on the tick rather than the ids so
  // accepting the same proposal twice still fires, and so a re-render never re-runs it.
  const proposedRequestIds = useUiActionsStore((s) => s.autoScheduleRequestIds);
  const clearAutoSchedule = useUiActionsStore((s) => s.clearAutoSchedule);

  useEffect(() => {
    if (!selectedSiteId || !proposedRequestIds?.length) return;
    // Consumed here rather than remembered in a ref: a ref dies with the page, so coming
    // back to the scheduler later would re-open the preview for requests already dealt
    // with. Clearing the payload makes the request single-use wherever it is read.
    clearAutoSchedule();

    void (async () => {
      try {
        const result = await previewMutation.mutateAsync({
          siteId: selectedSiteId,
          horizonStart,
          horizonEnd,
          requestIds: proposedRequestIds,
          resourceTypeKey: autoScheduleTypeKey ?? undefined,
        });
        setAutoScheduleRequestIds(proposedRequestIds);
        setAutoSchedulePreview(result);
        setIsPreviewDialogOpen(true);
      } catch {
        // Surfaced by the mutation's own error state, same as the toolbar run.
      }
    })();
  }, [proposedRequestIds, clearAutoSchedule, selectedSiteId, horizonStart, horizonEnd, autoScheduleTypeKey, previewMutation]);

  // Deliberately hand-rolled toast/invalidate orchestration (not meta-mutation):
  // the success toast interpolates the preview's dynamic count, and the catch
  // classifies the preview-fingerprint 409 into an in-dialog error.
  const handleAutoScheduleApply = useCallback(async () => {
    if (!selectedSiteId) return;
    setAutoScheduleError(null);
    try {
      // resourceTypeKey must be the one the preview solved for — the fingerprint alone
      // doesn't pin it, so a changed selector would re-solve for a different type.
      await applyMutation.mutateAsync({
        siteId: selectedSiteId,
        horizonStart,
        horizonEnd,
        requestIds: autoScheduleRequestIds ?? undefined,
        resourceTypeKey: autoScheduleTypeKey ?? undefined,
        previewFingerprint: autoSchedulePreview?.fingerprint,
      });
      setIsPreviewDialogOpen(false);
      const scheduledCount = autoSchedulePreview?.assignments.length ?? 0;
      setAutoSchedulePreview(null);
      invalidateRequestData(queryClient);
      toast.success(
        scheduledCount > 0
          ? `Scheduled ${scheduledCount} request${scheduledCount === 1 ? "" : "s"}`
          : "Auto-schedule applied",
      );
    } catch (err) {
      const message = errorMessage(err);
      if (message.startsWith("API Error (409)")) {
        setAutoScheduleError(
          "The scheduling data has changed since this preview was generated. Please close and re-run the auto-schedule."
        );
      } else {
        setAutoScheduleError(message);
      }
    }
  }, [selectedSiteId, horizonStart, horizonEnd, autoScheduleRequestIds, autoScheduleTypeKey, applyMutation, autoSchedulePreview, queryClient]);

  // The Calendar tab pages by whole periods (one click = one week/month); every timeline grid
  // pans by a sub-period. Same controls, tab-aware step.
  const stepAnchor = (direction: 1 | -1) =>
    isCalendarTab
      ? navigateCalendarPeriod(anchorTs, scale, direction)
      : navigateTime(anchorTs, scale, direction);
  const handlePrevious = () => setAnchorTs(stepAnchor(-1));
  const handleNext = () => setAnchorTs(stepAnchor(1));

  // Handle double-click on request in grid
  const handleRequestDoubleClick = useCallback((requestId: string) => {
    const request = requests.find(r => r.id === requestId);
    if (request) openRequestEditor(request, conflicts.get(requestId) ?? []);
  }, [requests, openRequestEditor, conflicts]);

  // --- Drag-end sub-handlers (named for readability, not extracted) ---

  const handleSpaceReorder = useCallback((activeId: string | number, overId: string | number) => {
    const currentOrder = useAppStore.getState().spaceOrder;
    // Seeded from every station, not the filtered rows: a first drag under an active type filter
    // would otherwise persist an order naming only that type, and SchedulerGrid sinks every
    // unlisted station below it for good once the filter clears.
    const orderedIds = currentOrder.length > 0
      ? currentOrder
      : allSpaces.map(s => s.id);

    const oldIndex = orderedIds.indexOf(String(activeId));
    const newIndex = orderedIds.indexOf(String(overId));

    if (oldIndex !== -1 && newIndex !== -1) {
      const reordered = [...orderedIds];
      const [moved] = reordered.splice(oldIndex, 1);
      reordered.splice(newIndex, 0, moved);
      useAppStore.getState().setSpaceOrder(reordered);
      updatePreferencesMutation.mutate({ ...preferences, spaceOrder: reordered });
    }
  }, [allSpaces, preferences, updatePreferencesMutation]);

  const handleUnschedule = useCallback((request: Request & { isScheduled?: boolean }) => {
    if (!request.isScheduled) return;
    scheduleMutation.mutate({
      requestId: request.id,
      data: { resourceId: null, startTs: null, endTs: null },
    });
    // Conflicts refresh via the registry (invalidated on the schedule mutation) when the request loses its space assignment
  }, [scheduleMutation]);

  const handleScheduleToGrid = useCallback(async (
    draggedData: Request & { isScheduled?: boolean },
    resourceId: string,
    startTs: Date,
  ) => {
    // Preserve actual duration for already-scheduled requests;
    // fall back to durationMin for unscheduled ones.
    let durationMs: number;
    if (draggedData.isScheduled && draggedData.startTs && draggedData.endTs) {
      durationMs = new Date(draggedData.endTs).getTime() - new Date(draggedData.startTs).getTime();
    } else {
      durationMs = (draggedData.durationMin || 0) * 60 * 1000;
    }
    const endTs = new Date(startTs.getTime() + durationMs);

    await scheduleMutation.mutateAsync({
      requestId: draggedData.id,
      data: { resourceId, startTs: startTs.toISOString(), endTs: endTs.toISOString() },
      // Client-side only: the optimistic bar must be tagged with the row's real type, or it
      // would vanish on refetch for any placeable type other than space.
      resourceTypeKey: spaces.find((r) => r.id === resourceId)?.resourceTypeKey,
    });

    logger.debug(`[Drag & Drop] Request "${draggedData.name}" scheduled to resource "${resourceId}"`);
  }, [scheduleMutation, spaces]);

  // Non-drag scheduling: reuse the exact drag-drop handler (duration → endTs,
  // schedule mutation, conflict feedback) so the dialog and the drag path submit
  // identically.
  const handleDragEnd = useCallback(async (event: DragEndEvent) => {
    setActiveDragRequest(null);
    const { active, over } = event;
    if (!over) return;

    const draggedData = active.data.current as Request & { isScheduled?: boolean; type?: string };
    const dropData = over.data.current as { resourceId?: string; type?: string; parentRequestId?: string; viewStartMs?: number; viewEndMs?: number };

    if (draggedData?.type === "space-row") {
      if (active.id !== over.id) handleSpaceReorder(active.id, over.id);
      return;
    }
    if (!draggedData) return;

    // Only a scheduled bar is draggable onto a track, so it always carries its own
    // bounds — the drag is a move of an existing placement, never a first placement
    // (backlog reaches the grid through the "Schedule to…" dialog instead).
    if (
      dropData?.type === "space-track" && dropData.resourceId && selectedSiteId &&
      dropData.viewStartMs !== undefined && dropData.viewEndMs !== undefined &&
      draggedData.startTs && draggedData.endTs
    ) {
      // The whole row is one droppable. The bar moves freely: its new start comes
      // from the drag delta against the track's measured width, with no snap to a
      // column edge, so it lands where the user sees it.
      const origStartMs = new Date(draggedData.startTs).getTime();
      const startMs = resolveDropStartMs(
        origStartMs,
        new Date(draggedData.endTs).getTime() - origStartMs,
        event.delta.x,
        over.rect.width,
        dropData.viewStartMs,
        dropData.viewEndMs,
      );
      await handleScheduleToGrid(draggedData, dropData.resourceId, new Date(startMs));
    }
  }, [selectedSiteId, handleSpaceReorder, handleScheduleToGrid]);

  const handleResizeRequest = useCallback((requestId: string, startTs: string, endTs: string) => {
    const request = requests.find((r) => r.id === requestId);
    if (!request) return;
    const spaceResourceId = getPlacementResourceId(request, placeableKeys);
    if (!spaceResourceId) return;
    scheduleMutation.mutate(
      {
        requestId,
        data: {
          resourceId: spaceResourceId,
          startTs,
          endTs,
        },
      },
      {
        // Clear the "committing" draft only after the mutation fully settles
        // (success or error). By this point the query cache is authoritative
        // (onSuccess wrote the server response, or onError rolled back).
        onSettled: () => {
          useSchedulerStore.getState().finalizeDraft(requestId);
        },
      },
    );
  }, [requests, scheduleMutation, placeableKeys]);

  // --- Calendar tab handlers ---

  // Move/resize: re-send the request's CURRENT space resourceId with the new
  // times, so drag/resize never alters resource assignments (same mechanism as
  // handleResizeRequest above). Validation/conflicts refresh via the mutation.
  const handleCalendarReschedule = useCallback((requestId: string, start: Date, end: Date) => {
    const request = requests.find((r) => r.id === requestId);
    if (!request) return;
    const spaceResourceId = getPlacementResourceId(request, placeableKeys);
    if (!spaceResourceId) return;
    scheduleMutation.mutate({
      requestId,
      data: { resourceId: spaceResourceId, startTs: start.toISOString(), endTs: end.toISOString() },
    });
  }, [requests, scheduleMutation, placeableKeys]);

  const handleCalendarEventClick = useCallback((requestId: string) => {
    const request = requests.find((r) => r.id === requestId);
    if (request) openRequestEditor(request);
  }, [requests, openRequestEditor]);

  const handleSlotSelect = useCallback((start: Date, end: Date) => {
    setSlotSelection({ start, end });
    setIsSlotChooserOpen(true);
  }, []);

  // Spaces-grid empty-cell click: same chooser as the calendar, with the
  // clicked space carried along so it can be pre-filled.
  const handleGridCellClick = useCallback((space: ResourceInfo, col: TimeColumn) => {
    setSlotSelection({
      start: col.start,
      end: col.end,
      // The row's own type, not the space key: the chooser filters the backlog by it, so a
      // booth cell offering only space-targeted requests would look empty for no reason.
      resource: { id: space.id, name: space.code || space.name, typeKey: space.resourceTypeKey },
    });
    setIsSlotChooserOpen(true);
  }, []);

  // Grid clicks only offer requests that actually target the clicked resource's
  // type; the calendar (no resource) keeps the full backlog.
  const chooserBacklog = useMemo(() => {
    const typeKey = slotSelection?.resource?.typeKey;
    if (!typeKey) return backlog;
    return backlog.filter((r) => getTargetResourceTypeKeys(r).includes(typeKey));
  }, [backlog, slotSelection]);

  const handleChooserCreateNew = useCallback(() => {
    if (!slotSelection) return;
    setIsSlotChooserOpen(false);
    setCalendarForm({
      mode: "create",
      request: null,
      startTs: slotSelection.start.toISOString(),
      endTs: slotSelection.end.toISOString(),
      resource: slotSelection.resource
        ? { typeKey: slotSelection.resource.typeKey, resourceId: slotSelection.resource.id }
        : undefined,
    });
  }, [slotSelection]);

  const handleChooserScheduleExisting = useCallback((request: Request) => {
    if (!slotSelection) return;
    setIsSlotChooserOpen(false);
    if (slotSelection.resource) {
      // Grid click: schedule straight onto the clicked space at the cell's
      // start — the same path drag-to-reschedule uses.
      void handleScheduleToGrid(request, slotSelection.resource.id, slotSelection.start);
      return;
    }
    setCalendarForm({
      mode: "edit",
      request,
      startTs: slotSelection.start.toISOString(),
      endTs: slotSelection.end.toISOString(),
    });
  }, [slotSelection, handleScheduleToGrid]);

  // Both chooser paths reuse RequestFormDialog (space picker + validation) and
  // persist via the existing create/update request APIs. The form pre-selects
  // the calendar's site (scheduleSiteId) so the scheduled request lands on this
  // site's calendar — but the user stays in control and the form warns if they
  // pick a site that won't show here. So persist exactly what they chose.
  const handleCalendarFormSave = useCallback(async (data: RequestFormData) => {
    if (!calendarForm) return;
    if (calendarForm.mode === "edit" && calendarForm.request) {
      await updateRequest(calendarForm.request.id, buildUpdatePayload(data, calendarForm.request.planningMode, calendarForm.request.siteId));
    } else {
      await createRequest(buildCreatePayload(data));
    }
    invalidateRequestData(queryClient);
    setCalendarForm(null);
  }, [calendarForm, queryClient]);

  // The calendar is driven by the page's scale selector + date navigator (shared
  // with the Spaces/People tabs), so scale is page-owned; the calendar only
  // reports the visible range's start, which we mirror into the anchor so
  // useScheduledRequests fetches the right window when the view snaps to a
  // period boundary.
  const handleCalendarDatesSet = useCallback((activeStart: Date) => {
    setAnchorTs(activeStart);
  }, [setAnchorTs]);

  const tabs: PageTab[] = [
    { value: 'calendar', label: 'Calendar' },
    { value: STATIONS_TAB, label: 'Stations' },
    { value: ASSETS_TAB, label: 'Assets' },
  ];

  // Scale + time navigation shared across every tab (the calendar is
  // page-controlled — its built-in toolbar is disabled). They always live in the
  // header's actions slot; PageHeader flex-wraps, so on phones the compact
  // controls wrap onto their own line under the title instead of a bespoke row.
  const schedulingControls = (
    <>
      {autoScheduleAvailable && canEdit && !isCalendarTab && (
        <AutoScheduleButton
          onClick={handleAutoScheduleClick}
          loading={previewMutation.isPending}
          disabled={!selectedSiteId || !autoScheduleTypeKey}
        />
      )}
      <ScaleSelect value={scale} onChange={setScale} compact={isPhone} />
      <TimeNavigator
        scale={scale}
        anchorTs={anchorTs}
        onAnchorChange={setAnchorTs}
        onPrevious={handlePrevious}
        onNext={handleNext}
        onToday={handleToday}
        compact={isPhone}
      />
    </>
  );

  return (
    <PageLayout>
      <PageHeader
        title="Utilization"
        description="Schedule allocations and review utilization across your resources"
        actions={schedulingControls}
      />
      <PageTabs
        tabs={tabs}
        value={activeTab}
        onChange={handleTabChange}
      >
        {/* Radix hides the inactive tab via data-[state=inactive]:hidden
            (display:none), so the active one takes h-full of the wrapper. */}

        {/* Calendar tab — Outlook-style time view of scheduled requests */}
        <TabsContent value="calendar" className="h-full overflow-hidden m-0 data-[state=inactive]:hidden">
          {/* One outline, drawn here — the same container the stations and assets tabs use. The
              padding this used to carry pushed FullCalendar's own outer border inward, so the tab
              read as a box inside a box. */}
          <div className="flex h-full flex-col overflow-hidden rounded-xl border bg-background">
            <RequestCalendar
              events={calendarEvents}
              offTimeRanges={offTimeRanges}
              workingHours={schedulingSettings ? {
                enabled: schedulingSettings.workingHoursEnabled,
                start: schedulingSettings.workingDayStart,
                end: schedulingSettings.workingDayEnd,
              } : undefined}
              editable={canEdit}
              initialView={scaleToCalendarView(scale, { phone: isPhone })}
              initialDate={anchorTs}
              active={activeTab === 'calendar'}
              onEventClick={handleCalendarEventClick}
              onEventMove={handleCalendarReschedule}
              onEventResize={handleCalendarReschedule}
              onSlotSelect={handleSlotSelect}
              onDatesSet={handleCalendarDatesSet}
            />
          </div>
        </TabsContent>

        <TabsContent value={STATIONS_TAB} className="h-full overflow-hidden m-0 data-[state=inactive]:hidden">
          {/* One DndContext across breakpoints — the grid's TimelineGridShell uses
              dnd-kit SortableContext/useSortable and must have a context ancestor
              even on phone. Phones drop the heavy floorplan canvas + backlog panel
              and get a scroll-only, tap-to-open, drag-free grid. */}
          <DndContext
            sensors={sensors}
            onDragStart={handleDragStart}
            onDragEnd={handleDragEnd}
            onDragCancel={() => setActiveDragRequest(null)}
            collisionDetection={collisionDetection}
          >
            <div className="h-full flex flex-col overflow-hidden gap-3">
              {/* Collapsible Floorplan — desktop/tablet only */}
              {!isPhone && (
                <CollapsibleFloorplan
                  isCollapsed={isFloorplanCollapsed}
                  onToggle={() => setIsFloorplanCollapsed(!isFloorplanCollapsed)}
                  timeCursorTs={timeCursorTs}
                  requests={requests}
                  conflicts={conflictingRequestIds}
                  height={floorplanHeight}
                  onHeightChange={setFloorplanHeight}
                />
              )}

              {/* Scheduler — full width; the backlog reaches the grid by clicking an empty cell. */}
              <div className="flex-1 flex flex-col overflow-hidden rounded-xl border bg-background">
                {/* Key on the left, search and filters opposite it — the same bar the calendar
                    carries, over this grid's own key. */}
                <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 border-b px-3 py-2 text-xs text-muted-foreground shrink-0">
                  {!isPhone && <StationGridLegend />}
                  <ScheduleFilterBar
                    value={stationFilter}
                    onChange={(patch) => setStationFilter((current) => ({ ...current, ...patch }))}
                    matchCount={visibleScheduled.length}
                    totalCount={scheduled.length}
                    typeFilter={
                      <TypeFilterSelect
                        available={placeableTypes}
                        selected={stationKeys}
                        onChange={setStationKeys}
                      />
                    }
                  />
                </div>
                <div className="flex flex-1 overflow-hidden">
                {spacesLoading || requestsLoading ? (
                  <div className="flex-1">
                    <LoadingSpinner fullScreen={false} message="Loading requests…" />
                  </div>
                ) : (
                  <SchedulerGrid
                    spaces={spaces}
                    requests={visibleScheduled}
                    scale={scale}
                    anchorTs={anchorTs}
                    timeCursorTs={timeCursorTs}
                    nowMs={nowMs}
                    // Phone has no hover/double-click, so a single tap opens the
                    // editor; desktop opens on double-click only.
                    // Drag-to-reschedule works on both (mouse-move / touch long-
                    // press via the sensors above). Precise duration edits happen
                    // in the dialog's Timing tab — better on touch than 2px handles.
                    onRequestClick={isPhone ? handleRequestDoubleClick : undefined}
                    onRequestDoubleClick={handleRequestDoubleClick}
                    onRequestContextMenu={canEdit && !isPhone ? handleRequestContextMenu : undefined}
                    onRequestResize={handleResizeRequest}
                    onEmptyCellClick={canEdit ? handleGridCellClick : undefined}
                    editable={canEdit}
                    onTimeCursorClick={setTimeCursorTs}
                    onAnchorChange={setAnchorTs}
                    offTimeRanges={offTimeRanges}
                    weekendsEnabled={schedulingSettings ? !schedulingSettings.weekendsEnabled : undefined}
                    workingHoursEnabled={schedulingSettings?.workingHoursEnabled}
                    workingDayStart={schedulingSettings?.workingDayStart}
                    workingDayEnd={schedulingSettings?.workingDayEnd}
                  />
                )}
                </div>
              </div>
            </div>

            {/* Live drop-location hint (isolated; does not re-render the grid). */}
            <DropPositionIndicator scale={scale} />
            {/* The clone. dnd-kit sizes this wrapper to the bar it picked up, so the copy
                fills it and reads as the same object moving. */}
            <DragOverlay dropAnimation={null}>
              {activeDragRequest ? (() => {
                const status = conflictingRequestIds.has(activeDragRequest.id) ? 'overbooked' : 'assigned';
                return (
                  <div className={`relative h-full w-full ${REQUEST_BAR_BASE_CLASS} ${requestBarToneClass(status)} shadow-lg cursor-grabbing`}>
                    <RequestBarLayers status={status} />
                    <RequestBarLabel request={activeDragRequest} hasConflict={status === 'overbooked'} />
                  </div>
                );
              })() : null}
            </DragOverlay>
          </DndContext>
        </TabsContent>

        {/* Assets — one grid per selected type, stacked. Reuses the grid unchanged rather than
            teaching it about several types, whose columns would have to mean different things. */}
        <TabsContent
          value={ASSETS_TAB}
          className="h-full overflow-hidden m-0 data-[state=inactive]:hidden"
        >
          <div className="flex h-full flex-col overflow-hidden rounded-xl border bg-background">
            {/* Key on the left, search and filters opposite it — the same bar the calendar and the
                stations grid carry, over the states these grids paint. One outline around the lot,
                and no per-type heading: each grid's own row header already names its type. */}
            <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 border-b px-4 py-2 text-xs text-muted-foreground shrink-0">
              {!isPhone && <AssetGridLegend />}
              <ResourceGridFilterBar
                value={assetFilter}
                onChange={setAssetFilter}
                typeFilter={
                  <TypeFilterSelect
                    available={gridTypes}
                    selected={assetKeys}
                    onChange={setAssetKeys}
                  />
                }
              />
            </div>
            {/* One scroll region per grid, never two. The stack splits the height rather than
                scrolling itself: a scrolling stack around grids that already scroll internally
                gives nested scrollbars, where a wheel gesture lands in whichever region the
                pointer happens to be over. The stations tab has the same shape — bar, then a grid
                that owns its own scrolling with its header pinned. */}
            <div className="flex flex-1 flex-col overflow-hidden divide-y">
              {selectedAssetTypes.map((type) => (
                <section key={type.key} className="flex min-h-0 flex-1 flex-col">
                  <ResourceUtilizationGrid
                    resourceType={type}
                    anchorTs={anchorTs}
                    scale={scale}
                    offTimeRanges={offTimeRanges}
                    weekendsEnabled={schedulingSettings ? !schedulingSettings.weekendsEnabled : undefined}
                    siteId={selectedSiteId}
                    filter={assetFilter}
                  />
                </section>
              ))}
            </div>
          </div>
        </TabsContent>

      </PageTabs>

      {/* Dialogs — rendered outside tabs; they portal to document.body */}
      {requestEditorDialogs}

      {/* Unschedule lives here rather than in the grid so the mutation stays on the page that
          owns it. Double-click already opens the editor, so the menu does not repeat it. */}
      {requestMenu && (
        <DropdownMenu open onOpenChange={(open) => !open && setRequestMenu(null)}>
          <DropdownMenuTrigger asChild>
            <span
              aria-hidden
              style={{ position: "fixed", left: requestMenu.x, top: requestMenu.y, width: 0, height: 0 }}
            />
          </DropdownMenuTrigger>
          <DropdownMenuContent align="start">
            <DropdownMenuItem
              onSelect={() => {
                const request = requests.find((r) => r.id === requestMenu.id);
                setRequestMenu(null);
                if (request) handleUnschedule({ ...request, isScheduled: true });
              }}
            >
              <CalendarOff className="mr-2 h-4 w-4" />
              Unschedule
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      )}

      {/* Empty-slot scheduling chooser (calendar slot select + Spaces-grid cell click) */}
      <ScheduleSlotDialog
        open={isSlotChooserOpen}
        onOpenChange={setIsSlotChooserOpen}
        selection={slotSelection}
        resourceName={slotSelection?.resource?.name}
        backlog={chooserBacklog}
        onCreateNew={handleChooserCreateNew}
        onScheduleExisting={handleChooserScheduleExisting}
      />

      {/* Chooser follow-up: create-new / schedule-existing form, prefilled with the slot */}
      <RequestFormDialog
        key={`calendar-${calendarForm?.request?.id ?? 'new'}-${calendarForm?.startTs ?? ''}-${calendarForm?.resource?.resourceId ?? ''}`}
        open={!!calendarForm}
        onOpenChange={(open) => { if (!open) setCalendarForm(null); }}
        request={calendarForm?.request ?? undefined}
        defaultSchedule={calendarForm ? { startTs: calendarForm.startTs, endTs: calendarForm.endTs } : undefined}
        defaultResource={calendarForm?.resource}
        scheduleSiteId={selectedSiteId ?? undefined}
        onSave={handleCalendarFormSave}
      />

      <AutoSchedulePreviewDialog
        open={isPreviewDialogOpen}
        preview={autoSchedulePreview}
        isApplying={applyMutation.isPending}
        applyError={autoScheduleError}
        onApply={handleAutoScheduleApply}
        onClose={() => {
          setIsPreviewDialogOpen(false);
          setAutoSchedulePreview(null);
          setAutoScheduleError(null);
        }}
      />
    </PageLayout>
  );
}
