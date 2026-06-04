import { CollapsibleFloorplan } from "@foundation/src/components/utilization/CollapsibleFloorplan";
import { RequestsPanel } from "@foundation/src/components/utilization/RequestsPanel";
import { ScaleSelect } from "@foundation/src/components/utilization/ScaleSelect";
import { SchedulerGrid } from "@foundation/src/components/utilization/SchedulerGrid";
import { TimeNavigator } from "@foundation/src/components/utilization/TimeNavigator";
import { TabsContent } from "@foundation/src/components/ui/tabs";
import { PageLayout, PageHeader, PageTabs, type PageTab } from "@foundation/src/components/layout";
import { RequestFormDialog, type RequestFormData } from "@foundation/src/components/requests/RequestFormDialog";
import { useRequestEditor } from "@foundation/src/components/requests/useRequestEditor";
import { getSpaceResourceId } from "@foundation/src/domain/scheduling/request-assignments";
import { useRequests, useScheduleRequest, useSpaces } from "@foundation/src/hooks/useUtilization";
import { useExportHandler } from "@foundation/src/hooks/useImportExport";
import { useConflicts } from "@foundation/src/hooks/useConflicts";
import { usePreferences, useUpdatePreferences } from "@foundation/src/hooks/usePreferences";
import { useSchedulingSettings, useAvailabilityEvents } from "@foundation/src/hooks/useScheduling";
import { useAuth } from "@foundation/src/contexts/AuthContext";
import { useAutoScheduleAvailable, usePreviewAutoSchedule, useApplyAutoSchedule } from "@foundation/src/hooks/useAutoSchedule";
import { AutoScheduleButton } from "@foundation/src/components/utilization/AutoScheduleButton";
import { AutoSchedulePreviewDialog } from "@foundation/src/components/utilization/AutoSchedulePreviewDialog";
import { PeopleUtilizationGrid } from "@foundation/src/components/utilization/PeopleUtilizationGrid";
import type { AutoSchedulePreviewResponse } from "@foundation/src/lib/api/auto-schedule-api";
import { exportUtilization } from "@foundation/src/lib/utils/export-handlers";
import { createRequest, moveRequest } from "@foundation/src/lib/api/request-api";
import { wouldCreateCycle, getNextSortOrder } from "@foundation/src/domain/request-tree";
import { logger } from "@foundation/src/lib/core/logger";
import { buildCreatePayload } from "@foundation/src/lib/utils/utils";
import { expandRecurrence } from "@foundation/src/domain/scheduling/recurrence";
import { generateWeekendRanges } from "@foundation/src/domain/scheduling/weekend-ranges";
import { getSpaceCapabilities } from "@foundation/src/lib/api/space-capability-api";
import { validateSpaceRequirements } from "@foundation/src/domain/scheduling/capability-matcher";
import { useAppStore } from "@foundation/src/store/app-store";
import { useSchedulerStore } from "@foundation/src/store/scheduler-store";
import { useShallow } from "zustand/react/shallow";
import type { OffTimeRange } from "@foundation/src/domain/scheduling/types";
import type { Conflict, Request } from "@foundation/src/types/requests";
import { DndContext, type DragEndEvent, PointerSensor, pointerWithin, useSensor, useSensors } from "@dnd-kit/core";
import { toast } from "sonner";
import { useQueryClient } from "@tanstack/react-query";
import { addMonths, format } from "date-fns";
import { useEffect, useState, useCallback, useMemo } from "react";
import { useSearchParams } from "react-router-dom";
import { navigateTime } from "@foundation/src/lib/utils/time-navigation";

export function UtilizationPage() {
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

  // Require 8px of movement before activating a drag so that plain clicks
  // on scheduled requests don't trigger handleDragEnd and re-schedule them.
  const pointerSensor = useSensor(PointerSensor, {
    activationConstraint: { distance: 8 },
  });
  const sensors = useSensors(pointerSensor);

  // Tab state — persisted in URL so the view is bookmarkable
  const [searchParams, setSearchParams] = useSearchParams();
  const activeTab = (searchParams.get('tab') ?? 'space') as 'space' | 'people';
  const handleTabChange = (tab: string) => {
    const next = new URLSearchParams(searchParams);
    next.set('tab', tab);
    setSearchParams(next, { replace: true });
  };

  // Floorplan height state
  const [floorplanHeight, setFloorplanHeight] = useState(280);

  // Request dialog state
  const { open: openRequestEditor, dialogs: requestEditorDialogs } = useRequestEditor();
  const [isCreateChildDialogOpen, setIsCreateChildDialogOpen] = useState(false);
  const [createChildParent, setCreateChildParent] = useState<Request | null>(null);
  const queryClient = useQueryClient();

  // Check if user can edit (admin or editor)
  const { membership } = useAuth();
  const userCanEdit = membership?.role === "admin" || membership?.role === "editor";

  // Auto-schedule
  const autoScheduleAvailable = useAutoScheduleAvailable();
  const previewMutation = usePreviewAutoSchedule();
  const applyMutation = useApplyAutoSchedule();
  const [autoSchedulePreview, setAutoSchedulePreview] = useState<AutoSchedulePreviewResponse | null>(null);
  const [isPreviewDialogOpen, setIsPreviewDialogOpen] = useState(false);
  const [autoScheduleError, setAutoScheduleError] = useState<string | null>(null);

  // Fetch data from API
  const { data: spaces = [], isLoading: spacesLoading } = useSpaces(selectedSiteId);
  const { data: requests = [], isLoading: requestsLoading } = useRequests();
  const scheduleMutation = useScheduleRequest();
  const { data: preferences } = usePreferences();
  const updatePreferencesMutation = useUpdatePreferences();

  // Scheduling off-times for grid overlay
  const { data: schedulingSettings } = useSchedulingSettings(selectedSiteId ?? undefined);
  const { data: availabilityEventDefs = [] } = useAvailabilityEvents(selectedSiteId ?? undefined);
  const offTimeRanges: readonly OffTimeRange[] = useMemo(() => {
    const tz = schedulingSettings?.timeZone ?? "UTC";
    const windowStart = addMonths(anchorTs, -1).getTime();
    const windowEnd = addMonths(anchorTs, 13).getTime();
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
  }, [availabilityEventDefs, schedulingSettings, anchorTs]);

  // Initialize space order from preferences
  const spaceOrder = useAppStore((state) => state.spaceOrder);
  const setSpaceOrder = useAppStore((state) => state.setSpaceOrder);
  useEffect(() => {
    if (preferences?.spaceOrder && spaceOrder.length === 0) {
      setSpaceOrder(preferences.spaceOrder);
    }
  }, [preferences, spaceOrder.length, setSpaceOrder]);

  // Conflict detection (scheduling + capability) — extracted to hook
  const { conflictingRequestIds } = useConflicts();

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
  const horizonStart = format(anchorTs, "yyyy-MM-dd");
  const horizonEnd = format(addMonths(anchorTs, AUTO_SCHEDULE_HORIZON_MONTHS), "yyyy-MM-dd");

  const handleAutoScheduleClick = useCallback(async () => {
    if (!selectedSiteId) return;
    try {
      const result = await previewMutation.mutateAsync({
        siteId: selectedSiteId,
        horizonStart,
        horizonEnd,
      });
      setAutoSchedulePreview(result);
      setIsPreviewDialogOpen(true);
    } catch {
      // Error handled by mutation state
    }
  }, [selectedSiteId, horizonStart, horizonEnd, previewMutation]);

  const handleAutoScheduleApply = useCallback(async () => {
    if (!selectedSiteId) return;
    setAutoScheduleError(null);
    try {
      await applyMutation.mutateAsync({
        siteId: selectedSiteId,
        horizonStart,
        horizonEnd,
        previewFingerprint: autoSchedulePreview?.fingerprint,
      });
      setIsPreviewDialogOpen(false);
      const scheduledCount = autoSchedulePreview?.assignments.length ?? 0;
      setAutoSchedulePreview(null);
      queryClient.invalidateQueries({ queryKey: ["requests"] });
      toast.success(
        scheduledCount > 0
          ? `Scheduled ${scheduledCount} request${scheduledCount === 1 ? "" : "s"}`
          : "Auto-schedule applied",
      );
    } catch (err) {
      const message = err instanceof Error ? err.message : "Failed to apply schedule";
      if (message.startsWith("API Error (409)")) {
        setAutoScheduleError(
          "The scheduling data has changed since this preview was generated. Please close and re-run the auto-schedule."
        );
      } else {
        setAutoScheduleError(message);
      }
    }
  }, [selectedSiteId, horizonStart, horizonEnd, applyMutation, autoSchedulePreview, queryClient]);

  const handlePrevious = () => setAnchorTs(navigateTime(anchorTs, scale, -1));
  const handleNext = () => setAnchorTs(navigateTime(anchorTs, scale, 1));

  const handleToday = () => {
    setAnchorTs(new Date());
    setTimeCursorTs(new Date());
  };

  // Handle double-click on request in grid
  const handleRequestDoubleClick = useCallback((requestId: string) => {
    const request = requests.find(r => r.id === requestId);
    if (request) openRequestEditor(request);
  }, [requests, openRequestEditor]);

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
    queryClient.invalidateQueries({ queryKey: ["requests"] });
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
    // Conflicts recompute naturally via useConflicts() when the request loses its space assignment
  }, [scheduleMutation, setSelectedRequestId]);

  const handleTreeReparent = useCallback(async (requestId: string, parentId: string) => {
    if (wouldCreateCycle(requestId, parentId, requests)) return;
    try {
      await moveRequest(requestId, {
        newParentRequestId: parentId,
        sortOrder: getNextSortOrder(parentId, requests),
      });
      queryClient.invalidateQueries({ queryKey: ["requests"] });
    } catch (error) {
      logger.error("Failed to reparent request:", error);
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

    // Validate space capabilities BEFORE mutating so an incompatible drop is
    // rejected with feedback instead of silently creating a conflict the user
    // only discovers later on the Conflicts page.
    const allConflicts: Conflict[] = [];
    try {
      const capabilities = await getSpaceCapabilities(selectedSiteId!, resourceId);
      allConflicts.push(...validateSpaceRequirements(draggedData, capabilities));
    } catch (error) {
      logger.error("Failed to validate space requirements:", error);
    }

    if (allConflicts.length > 0) {
      const targetSpace = spaces.find((s) => s.id === resourceId);
      const spaceName = targetSpace?.name ?? "this space";
      const firstReason = allConflicts[0]?.message ?? "missing required capability";
      const extra = allConflicts.length > 1 ? ` (+${allConflicts.length - 1} more)` : "";
      toast.error(`Cannot schedule to ${spaceName}`, {
        description: `${firstReason}${extra}`,
      });
      return;
    }

    await scheduleMutation.mutateAsync({
      requestId: draggedData.id,
      data: { resourceId, startTs: startTs.toISOString(), endTs: endTs.toISOString() },
    });

    logger.debug(`[Drag & Drop] Request "${draggedData.name}" scheduled to space "${resourceId}"`);

    setSelectedRequestId(draggedData.id);
  }, [scheduleMutation, selectedSiteId, spaces, setSelectedRequestId]);

  const handleDragEnd = useCallback(async (event: DragEndEvent) => {
    const { active, over } = event;
    if (!over) return;

    const draggedData = active.data.current as Request & { isScheduled?: boolean; type?: string };
    const dropData = over.data.current as { resourceId?: string; startTs?: Date; type?: string; parentRequestId?: string };

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
    if (dropData?.resourceId && dropData?.startTs && selectedSiteId) {
      await handleScheduleToGrid(draggedData, dropData.resourceId, dropData.startTs);
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

  const tabs: PageTab[] = [
    { value: 'space', label: 'Spaces' },
    { value: 'people', label: 'People' },
  ];

  return (
    <PageLayout>
      <PageHeader
        title="Utilization"
        description="Schedule space allocations and review people utilization"
        actions={
          <>
            {autoScheduleAvailable && userCanEdit && activeTab === 'space' && (
              <AutoScheduleButton
                onClick={handleAutoScheduleClick}
                loading={previewMutation.isPending}
                disabled={!selectedSiteId}
              />
            )}
            <ScaleSelect value={scale} onChange={setScale} />
            <TimeNavigator
              scale={scale}
              anchorTs={anchorTs}
              onAnchorChange={setAnchorTs}
              onPrevious={handlePrevious}
              onNext={handleNext}
              onToday={handleToday}
            />
          </>
        }
      />
      <PageTabs tabs={tabs} value={activeTab} onChange={handleTabChange}>
        {/* Radix hides the inactive tab via data-[state=inactive]:hidden
            (display:none), so the active one takes h-full of the wrapper. */}
        <TabsContent value="space" className="h-full overflow-hidden m-0 data-[state=inactive]:hidden">
          <DndContext sensors={sensors} onDragEnd={handleDragEnd} collisionDetection={pointerWithin}>
            <div className="h-full flex flex-col overflow-hidden gap-3 p-3">
              {/* Collapsible Floorplan */}
              <CollapsibleFloorplan
                isCollapsed={isFloorplanCollapsed}
                onToggle={() => setIsFloorplanCollapsed(!isFloorplanCollapsed)}
                timeCursorTs={timeCursorTs}
                requests={requests}
                conflicts={conflictingRequestIds}
                height={floorplanHeight}
                onHeightChange={setFloorplanHeight}
              />

              {/* Scheduler + Requests Panel */}
              <div className="flex-1 flex overflow-hidden rounded-xl border bg-background">
                <RequestsPanel
                  requests={requests}
                  isLoading={requestsLoading}
                  onCreateChild={userCanEdit ? handleCreateChild : undefined}
                />

                {spacesLoading || requestsLoading ? (
                  <div className="flex-1 flex items-center justify-center">
                    <div className="text-muted-foreground">Loading...</div>
                  </div>
                ) : (
                  <SchedulerGrid
                    spaces={spaces}
                    requests={requests}
                    scale={scale}
                    anchorTs={anchorTs}
                    timeCursorTs={timeCursorTs}
                    onRequestClick={setSelectedRequestId}
                    onRequestDoubleClick={handleRequestDoubleClick}
                    onRequestResize={handleResizeRequest}
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
          </DndContext>
        </TabsContent>

        {/* People tab */}
        <TabsContent value="people" className="h-full overflow-hidden m-0 data-[state=inactive]:hidden">
          <PeopleUtilizationGrid anchorTs={anchorTs} scale={scale} offTimeRanges={offTimeRanges} />
        </TabsContent>
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
