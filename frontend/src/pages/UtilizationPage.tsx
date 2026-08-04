import { CollapsibleFloorplan } from "@foundation/src/components/utilization/CollapsibleFloorplan";
import { RequestsPanel } from "@foundation/src/components/utilization/RequestsPanel";
import { ScaleSelect } from "@foundation/src/components/utilization/ScaleSelect";
import { SchedulerGrid } from "@foundation/src/components/utilization/SchedulerGrid";
import { TimeNavigator } from "@foundation/src/components/utilization/TimeNavigator";
import { TabsContent } from "@foundation/src/components/ui/tabs";
import { PageLayout, PageHeader, PageTabs, type PageTab } from "@foundation/src/components/layout";
import { RequestFormDialog, type RequestFormData } from "@foundation/src/components/requests/RequestFormDialog";
import type { DefaultResource } from "@foundation/src/hooks/useRequestForm";
import { useRequestEditor } from "@foundation/src/components/requests/useRequestEditor";
import { getSpaceResourceId, getTargetResourceTypeKeys } from "@foundation/src/domain/scheduling/request-assignments";
import { withEffectiveStatus } from "@foundation/src/domain/scheduling/effective-status";
import { useNow } from "@foundation/src/hooks/useNow";
import { usePageTitle } from "@foundation/src/hooks/usePageTitle";
import { useScheduledRequests, useBacklogRequests, useScheduleRequest, useSpaces } from "@foundation/src/hooks/useUtilization";
import { getFetchWindow, isAnchorStale } from "@foundation/src/components/utilization/time-grid-utils";
import { useExportHandler } from "@foundation/src/hooks/useImportExport";
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
import { createRequest, moveRequest, updateRequest } from "@foundation/src/lib/api/request-api";
import { wouldCreateCycle, getNextSortOrder } from "@foundation/src/domain/request-tree";
import { logger } from "@foundation/src/lib/core/logger";
import { invalidateRequestData } from "@foundation/src/lib/core/invalidate-request-data";
import { buildCreatePayload, buildUpdatePayload } from "@foundation/src/lib/utils/utils";
import { expandRecurrence } from "@foundation/src/domain/scheduling/recurrence";
import { generateWeekendRanges } from "@foundation/src/domain/scheduling/weekend-ranges";
import { DEFAULT_TARGET_RESOURCE_TYPE_KEYS } from "@foundation/src/constants";
import { RESOURCE_TYPE_KEY } from "@foundation/src/constants/resource-type-key";
import { useResourceTypes } from "@foundation/src/hooks/useResourceTypes";
import { useAppStore } from "@foundation/src/store/app-store";
import { useSchedulerStore } from "@foundation/src/store/scheduler-store";
import { useShallow } from "zustand/react/shallow";
import type { OffTimeRange } from "@foundation/src/domain/scheduling/types";
import type { Request } from "@foundation/src/types/requests";
import type { Space } from "@foundation/src/types/space";
import type { TimeColumn } from "@foundation/src/components/utilization/scheduler-types";
import { DndContext, DragOverlay, type CollisionDetection, type DragEndEvent, type DragStartEvent, KeyboardSensor, MouseSensor, TouchSensor, pointerWithin, useSensor, useSensors } from "@dnd-kit/core";
import { sortableKeyboardCoordinates } from "@dnd-kit/sortable";
import { ScheduleToDialog } from "@foundation/src/components/utilization/ScheduleToDialog";
import { resolveColumnStartMs } from "@foundation/src/components/utilization/time-grid-utils";
import { DropColumnIndicator } from "@foundation/src/components/utilization/DropColumnIndicator";
import { LoadingSpinner } from "@foundation/src/components/ui/LoadingSpinner";
import { toast } from "sonner";
import { useQueryClient } from "@tanstack/react-query";
import { addMonths, format, startOfMonth } from "date-fns";
import { DATE_FORMATS } from "@foundation/src/lib/formatters";
import { useEffect, useState, useCallback, useMemo } from "react";
import { useTabParam } from "@foundation/src/hooks/useTabParam";
import { navigateTime, navigateCalendarPeriod } from "@foundation/src/lib/utils/time-navigation";
import { errorMessage } from "@foundation/src/hooks/mutation-utils";

export function UtilizationPage() {
  usePageTitle("Utilization");
  const {
    scale, setScale,
    anchorTs, setAnchorTs,
    timeCursorTs, setTimeCursorTs,
    isFloorplanCollapsed, setIsFloorplanCollapsed,
    setSelectedRequestId,
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
    setSelectedRequestId: state.setSelectedRequestId,
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

  // Label of the request currently being dragged — drives the <DragOverlay> so
  // dnd-kit moves a lightweight clone instead of transforming/reconciling the
  // original node (and its subtree) on every pointer move. Row-reorder drags
  // (type "space-row") don't get an overlay.
  const [activeDragLabel, setActiveDragLabel] = useState<string | null>(null);
  const handleDragStart = useCallback((event: DragStartEvent) => {
    const data = event.active.data.current as { type?: string; name?: string } | undefined;
    setActiveDragLabel(data && data.type !== "space-row" ? data.name ?? null : null);
  }, []);

  // One grid tab per active resource type that has no purpose-built surface. Spaces keeps its
  // own scheduler tab (drag-to-schedule, floorplan, backlog); every other type — people, tools,
  // anything a tenant defines — gets the read-mostly timeline grid.
  const { data: resourceTypes = [], isSuccess: resourceTypesLoaded } = useResourceTypes(true);
  const gridTypes = useMemo(() => {
    const rest = resourceTypes.filter((t) => t.key !== RESOURCE_TYPE_KEY.SPACE);
    // People first — the established second tab — then whatever order the API returns.
    return [
      ...rest.filter((t) => t.key === RESOURCE_TYPE_KEY.PERSON),
      ...rest.filter((t) => t.key !== RESOURCE_TYPE_KEY.PERSON),
    ];
  }, [resourceTypes]);

  // Tab state — persisted in URL so the view is bookmarkable. A grid tab's value is its type
  // key, so the valid set is only known once the types load; judge the URL after that rather
  // than falling back early and flashing Calendar over a valid deep link.
  const [rawTab, handleTabChange] = useTabParam('calendar');
  const isKnownTab =
    rawTab === 'calendar'
    || rawTab === RESOURCE_TYPE_KEY.SPACE
    || gridTypes.some((t) => t.key === rawTab);
  // Until the types load the valid set is unknown, so an unrecognised value is held rather
  // than replaced — otherwise a deep link to a real type tab flashes Calendar first.
  const activeTab = !resourceTypesLoaded || isKnownTab ? rawTab : 'calendar';

  // Correct the URL too. Rendering Calendar while ?tab= still names a type the tenant has
  // since deactivated leaves reload and the back button disagreeing with the screen.
  useEffect(() => {
    if (resourceTypesLoaded && !isKnownTab) handleTabChange('calendar');
  }, [resourceTypesLoaded, isKnownTab, handleTabChange]);

  const isCalendarTab = activeTab === 'calendar';
  const isSchedulerTab = activeTab === RESOURCE_TYPE_KEY.SPACE;
  /** Any derived per-type grid tab (person, tool, tenant-defined). */
  const isTypeGridTab = !isCalendarTab && !isSchedulerTab;

  // Auto-schedule solves for the type whose tab you are on — the tab already names it, so a
  // separate selector could only ever disagree with what is on screen, and keeping the two in
  // step needed an effect and a lock while a preview was open. Calendar has no type of its
  // own, so the controls do not appear there.
  const autoScheduleTypeKey = isCalendarTab ? DEFAULT_TARGET_RESOURCE_TYPE_KEYS[0] : activeTab;

  // Floorplan height state
  const [floorplanHeight, setFloorplanHeight] = useState(280);

  // Request dialog state
  const { open: openRequestEditor, dialogs: requestEditorDialogs } = useRequestEditor();
  // On phone the drag-based scheduler grid is replaced by a drag-free agenda.
  const { isPhone } = useBreakpoint();
  const [isCreateChildDialogOpen, setIsCreateChildDialogOpen] = useState(false);
  const [createChildParent, setCreateChildParent] = useState<Request | null>(null);
  // Non-drag "Schedule to…" dialog target (keyboard-accessible scheduling path).
  const [scheduleToRequest, setScheduleToRequest] = useState<Request | null>(null);
  const queryClient = useQueryClient();

  // Auto-schedule
  const autoScheduleAvailable = useAutoScheduleAvailable();
  const previewMutation = usePreviewAutoSchedule();
  const applyMutation = useApplyAutoSchedule();
  const [autoSchedulePreview, setAutoSchedulePreview] = useState<AutoSchedulePreviewResponse | null>(null);
  const [isPreviewDialogOpen, setIsPreviewDialogOpen] = useState(false);
  const [autoScheduleError, setAutoScheduleError] = useState<string | null>(null);


  // Fetch data from API — scoped to the selected site + a buffered window for the grid bars, plus
  // the tenant-wide unscheduled backlog (drag source). The panel/lookups use the union; the grid
  // bars come from the scoped scheduled set.
  const { data: spaces = [], isLoading: spacesLoading } = useSpaces(selectedSiteId);
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
    enabled: !isTypeGridTab,
  });
  const conflictingRequestIds = useMemo(() => new Set(conflicts.keys()), [conflicts]);

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
      // Calculate visible date range based on current view
      const startDate = new Date(anchorTs);
      const endDate = new Date(anchorTs);

      switch (scale) {
        case "year":
          endDate.setFullYear(endDate.getFullYear() + 1);
          break;
        case "month":
          endDate.setMonth(endDate.getMonth() + 1);
          break;
        case "week":
          endDate.setDate(endDate.getDate() + 7);
          break;
        case "day":
          endDate.setDate(endDate.getDate() + 1);
          break;
        case "hour":
          endDate.setHours(endDate.getHours() + 1);
          break;
      }

      await exportUtilization(requests, spaces, startDate, endDate);
    }
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
        resourceTypeKey: autoScheduleTypeKey,
      });
      setAutoSchedulePreview(result);
      setIsPreviewDialogOpen(true);
    } catch {
      // Error handled by mutation state
    }
  }, [selectedSiteId, horizonStart, horizonEnd, autoScheduleTypeKey, previewMutation]);

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
        resourceTypeKey: autoScheduleTypeKey,
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
  }, [selectedSiteId, horizonStart, horizonEnd, autoScheduleTypeKey, applyMutation, autoSchedulePreview, queryClient]);

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

  // Handle "Add child" from RequestsPanel
  const handleCreateChild = useCallback((parentId: string) => {
    const parent = requests.find(r => r.id === parentId);
    if (!parent) return;
    setCreateChildParent(parent);
    setIsCreateChildDialogOpen(true);
  }, [requests]);

  // Handle save for create-child dialog
  const handleSaveChildRequest = useCallback(async (data: RequestFormData) => {
    await createRequest(buildCreatePayload(data));
    invalidateRequestData(queryClient);
    setIsCreateChildDialogOpen(false);
    setCreateChildParent(null);
  }, [queryClient]);

  // --- Drag-end sub-handlers (named for readability, not extracted) ---

  const handleSpaceReorder = useCallback((activeId: string | number, overId: string | number) => {
    const currentOrder = useAppStore.getState().spaceOrder;
    const orderedIds = currentOrder.length > 0
      ? currentOrder
      : spaces.map(s => s.id);

    const oldIndex = orderedIds.indexOf(String(activeId));
    const newIndex = orderedIds.indexOf(String(overId));

    if (oldIndex !== -1 && newIndex !== -1) {
      const reordered = [...orderedIds];
      const [moved] = reordered.splice(oldIndex, 1);
      reordered.splice(newIndex, 0, moved);
      useAppStore.getState().setSpaceOrder(reordered);
      updatePreferencesMutation.mutate({ ...preferences, spaceOrder: reordered });
    }
  }, [spaces, preferences, updatePreferencesMutation]);

  const handleUnschedule = useCallback((request: Request & { isScheduled?: boolean }) => {
    if (!request.isScheduled) return;
    scheduleMutation.mutate({
      requestId: request.id,
      data: { resourceId: null, startTs: null, endTs: null },
    });
    setSelectedRequestId(null);
    // Conflicts refresh via the registry (invalidated on the schedule mutation) when the request loses its space assignment
  }, [scheduleMutation, setSelectedRequestId]);

  const handleTreeReparent = useCallback(async (requestId: string, parentId: string) => {
    if (wouldCreateCycle(requestId, parentId, requests)) return;
    try {
      await moveRequest(requestId, {
        newParentRequestId: parentId,
        sortOrder: getNextSortOrder(parentId, requests),
      });
      invalidateRequestData(queryClient);
    } catch (error) {
      logger.error("Failed to reparent request:", error);
      toast.error("Couldn't move the request. Please try again.");
    }
  }, [requests, queryClient]);

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
    });

    logger.debug(`[Drag & Drop] Request "${draggedData.name}" scheduled to space "${resourceId}"`);

    setSelectedRequestId(draggedData.id);
  }, [scheduleMutation, setSelectedRequestId]);

  // Non-drag scheduling: reuse the exact drag-drop handler (duration → endTs,
  // schedule mutation, conflict feedback) so the dialog and the drag path submit
  // identically.
  const handleScheduleToSubmit = useCallback(async (resourceId: string, startTs: Date) => {
    if (!scheduleToRequest) return;
    await handleScheduleToGrid(scheduleToRequest, resourceId, startTs);
  }, [scheduleToRequest, handleScheduleToGrid]);

  const handleDragEnd = useCallback(async (event: DragEndEvent) => {
    setActiveDragLabel(null);
    const { active, over } = event;
    if (!over) return;

    const draggedData = active.data.current as Request & { isScheduled?: boolean; type?: string };
    const dropData = over.data.current as { resourceId?: string; type?: string; parentRequestId?: string; columnStartsMs?: number[] };

    if (draggedData?.type === "space-row") {
      if (active.id !== over.id) handleSpaceReorder(active.id, over.id);
      return;
    }
    if (!draggedData) return;

    if (dropData?.type === "unschedule") {
      handleUnschedule(draggedData);
      return;
    }
    if (dropData?.type === "tree-reparent" && dropData.parentRequestId) {
      await handleTreeReparent(draggedData.id, dropData.parentRequestId);
      return;
    }
    if (dropData?.type === "space-track" && dropData.resourceId && dropData.columnStartsMs && selectedSiteId) {
      // The whole row is one droppable; resolve the exact column from where the
      // pointer ended (activator position + accumulated drag delta) relative to
      // the track's measured rect.
      const activator = event.activatorEvent as PointerEvent;
      const pointerX = activator.clientX + event.delta.x;
      const startMs = resolveColumnStartMs(
        pointerX,
        over.rect.left,
        over.rect.width,
        dropData.columnStartsMs,
      );
      await handleScheduleToGrid(draggedData, dropData.resourceId, new Date(startMs));
    }
  }, [selectedSiteId, handleSpaceReorder, handleUnschedule, handleTreeReparent, handleScheduleToGrid]);

  const handleResizeRequest = useCallback((requestId: string, startTs: string, endTs: string) => {
    const request = requests.find((r) => r.id === requestId);
    if (!request) return;
    const spaceResourceId = getSpaceResourceId(request);
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
  }, [requests, scheduleMutation]);

  // --- Calendar tab handlers ---

  // Move/resize: re-send the request's CURRENT space resourceId with the new
  // times, so drag/resize never alters resource assignments (same mechanism as
  // handleResizeRequest above). Validation/conflicts refresh via the mutation.
  const handleCalendarReschedule = useCallback((requestId: string, start: Date, end: Date) => {
    const request = requests.find((r) => r.id === requestId);
    if (!request) return;
    const spaceResourceId = getSpaceResourceId(request);
    if (!spaceResourceId) return;
    scheduleMutation.mutate({
      requestId,
      data: { resourceId: spaceResourceId, startTs: start.toISOString(), endTs: end.toISOString() },
    });
  }, [requests, scheduleMutation]);

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
  const handleGridCellClick = useCallback((space: Space, col: TimeColumn) => {
    setSlotSelection({
      start: col.start,
      end: col.end,
      resource: { id: space.id, name: space.code || space.name, typeKey: RESOURCE_TYPE_KEY.SPACE },
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
      // start — the exact path drag-drop and ScheduleToDialog use.
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
    { value: RESOURCE_TYPE_KEY.SPACE, label: 'Spaces' },
    ...gridTypes.map((t) => ({ value: t.key, label: t.displayNamePlural })),
  ];

  // Scale + time navigation shared across every tab (the calendar is
  // page-controlled — its built-in toolbar is disabled). On desktop/tablet these
  // sit in the header's actions slot; on phones they'd overlap the title, so they
  // get their own row under the heading (above the tabs; see the render below).
  const schedulingControls = (
    <>
      {autoScheduleAvailable && canEdit && !isCalendarTab && (
        <>
          <AutoScheduleButton
            onClick={handleAutoScheduleClick}
            loading={previewMutation.isPending}
            disabled={!selectedSiteId}
          />
        </>
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
        actions={isPhone ? undefined : schedulingControls}
      />
      {/* Phone: controls can't fit beside the title, so they get their own row
          under the heading (above the tabs) — mirroring desktop's
          heading→controls→tabs order. */}
      {isPhone && (
        <div className="flex flex-wrap items-center gap-2 mb-4">{schedulingControls}</div>
      )}
      <PageTabs
        tabs={tabs}
        value={activeTab}
        onChange={handleTabChange}
      >
        {/* Radix hides the inactive tab via data-[state=inactive]:hidden
            (display:none), so the active one takes h-full of the wrapper. */}

        {/* Calendar tab — Outlook-style time view of scheduled requests */}
        <TabsContent value="calendar" className="h-full overflow-hidden m-0 data-[state=inactive]:hidden">
          <div className="h-full rounded-xl border bg-background p-2 md:p-3">
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

        <TabsContent value="space" className="h-full overflow-hidden m-0 data-[state=inactive]:hidden">
          {/* One DndContext across breakpoints — the grid's TimelineGridShell uses
              dnd-kit SortableContext/useSortable and must have a context ancestor
              even on phone. Phones drop the heavy floorplan canvas + backlog panel
              and get a scroll-only, tap-to-open, drag-free grid. */}
          <DndContext
            sensors={sensors}
            onDragStart={handleDragStart}
            onDragEnd={handleDragEnd}
            onDragCancel={() => setActiveDragLabel(null)}
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

              {/* Scheduler + Requests Panel (backlog panel is desktop/tablet only) */}
              <div className="flex-1 flex overflow-hidden rounded-xl border bg-background">
                {!isPhone && (
                  <RequestsPanel
                    requests={requests}
                    isLoading={requestsLoading}
                    onCreateChild={canEdit ? handleCreateChild : undefined}
                    onRequestClick={openRequestEditor}
                    onScheduleTo={canEdit ? setScheduleToRequest : undefined}
                  />
                )}

                {spacesLoading || requestsLoading ? (
                  <div className="flex-1">
                    <LoadingSpinner fullScreen={false} message="Loading requests…" />
                  </div>
                ) : (
                  <SchedulerGrid
                    spaces={spaces}
                    requests={scheduled}
                    scale={scale}
                    anchorTs={anchorTs}
                    timeCursorTs={timeCursorTs}
                    nowMs={nowMs}
                    // Phone has no hover/double-click, so a single tap opens the
                    // editor; desktop keeps single-click-selects / double-opens.
                    // Drag-to-reschedule works on both (mouse-move / touch long-
                    // press via the sensors above). Precise duration edits happen
                    // in the dialog's Timing tab — better on touch than 2px handles.
                    onRequestClick={isPhone ? handleRequestDoubleClick : setSelectedRequestId}
                    onRequestDoubleClick={handleRequestDoubleClick}
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

            {/* Live drop-location hint (isolated; does not re-render the grid). */}
            <DropColumnIndicator />

            {/* Lightweight drag clone — avoids transforming the original node. */}
            <DragOverlay dropAnimation={null}>
              {activeDragLabel ? (
                <div className="rounded bg-primary/90 px-2 py-1 text-xs font-medium text-primary-foreground shadow-lg">
                  {activeDragLabel}
                </div>
              ) : null}
            </DragOverlay>
          </DndContext>
        </TabsContent>

        {/* One grid per type without a purpose-built surface. Radix unmounts inactive
            panels, so only the visible tab's grid fetches. */}
        {gridTypes.map((type) => (
          <TabsContent
            key={type.key}
            value={type.key}
            className="h-full overflow-hidden m-0 data-[state=inactive]:hidden"
          >
            <ResourceUtilizationGrid
              resourceType={type}
              anchorTs={anchorTs}
              scale={scale}
              offTimeRanges={offTimeRanges}
              weekendsEnabled={schedulingSettings ? !schedulingSettings.weekendsEnabled : undefined}
              siteId={selectedSiteId}
            />
          </TabsContent>
        ))}

      </PageTabs>

      {/* Dialogs — rendered outside tabs; they portal to document.body */}
      {requestEditorDialogs}

      <RequestFormDialog
        key={`child-${createChildParent?.id ?? 'none'}`}
        open={isCreateChildDialogOpen}
        onOpenChange={(open) => {
          setIsCreateChildDialogOpen(open);
          if (!open) setCreateChildParent(null);
        }}
        parentRequest={createChildParent}
        onSave={handleSaveChildRequest}
      />

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

      {/* Non-drag "Schedule to…" — keyboard-accessible scheduling for backlog cards */}
      <ScheduleToDialog
        open={!!scheduleToRequest}
        onOpenChange={(open) => { if (!open) setScheduleToRequest(null); }}
        request={scheduleToRequest}
        spaces={spaces}
        onSchedule={handleScheduleToSubmit}
        isSubmitting={scheduleMutation.isPending}
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
