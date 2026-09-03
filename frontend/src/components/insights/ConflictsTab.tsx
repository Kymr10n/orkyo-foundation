import { useConflictRegistry, useConflictedRequests } from "@foundation/src/hooks/useConflictRegistry";
import { useRequestEditor } from "@foundation/src/components/requests/useRequestEditor";
import { AlertCircle } from "lucide-react";
import { useExportHandler } from "@foundation/src/hooks/useImportExport";
import { exportConflicts } from "@foundation/src/lib/utils/export-handlers";
import { ConflictItem, type ConflictWithRequest } from "@foundation/src/components/insights/ConflictItem";
import React, { useMemo, useRef } from "react";
import { useVirtualizer } from "@tanstack/react-virtual";
import { logger } from "@foundation/src/lib/core/logger";
import { LoadingSpinner } from "@foundation/src/components/ui/LoadingSpinner";
import { ScrollArea } from "@foundation/src/components/ui/scroll-area";
import { Button } from "@foundation/src/components/ui/button";
import { ConflictTrendChart } from "@foundation/src/components/insights/InsightsTrendCharts";
import { useInsightsConflicts } from "@foundation/src/hooks/useInsights";
import { useInsightsTabContext } from "@foundation/src/components/insights/insightsTabContext";
import { useAiAssistantAvailable } from "@foundation/src/hooks/useAiAssistantAvailable";
import { useAiStatus } from "@foundation/src/hooks/useAiAssistant";
import { useUiActionsStore } from "@foundation/src/store/ui-actions-store";

export function ConflictsTab() {
  const { from, to, bucket, siteId } = useInsightsTabContext();
  const conflictsTrend = useInsightsConflicts(siteId, from, to, bucket);

  const { open: openRequestEditor, dialogs: requestEditorDialogs } = useRequestEditor();

  // The assistant opens with the conflict already in view, so the person does not have to
  // restate what they are looking at. Guidance stays in the panel — the request editor
  // remains the one place a request's details are edited (UI-GUIDELINES §15).
  const assistantEntitled = useAiAssistantAvailable();
  const { data: aiStatus } = useAiStatus(assistantEntitled);
  const assistantAvailable = assistantEntitled && aiStatus?.available === true;
  const openAssistant = useUiActionsStore((s) => s.openAssistant);
  const askAssistant = React.useCallback(
    (item: ConflictWithRequest) =>
      openAssistant({ type: "conflict", requestId: item.request.id, kind: item.kind }),
    [openAssistant]
  );
  // Tenant-wide authoritative registry + just the conflicted requests (not the whole tenant).
  const {
    conflictsByRequest: conflicts,
    isPending: conflictsPending,
    isError: conflictsError,
    refetch: refetchConflicts,
  } = useConflictRegistry();
  const {
    data: requests = [],
    isPending: requestsPending,
    isError: requestsError,
    refetch: refetchRequests,
  } = useConflictedRequests();

  // The tab joins both queries, so it is only "ready" once both have settled. Until then we must
  // not show the empty ("no conflicts") state — that would be a misleading false-negative.
  const isLoading = conflictsPending || requestsPending;
  const isError = conflictsError || requestsError;

  const searchParams = useMemo(
    () =>
      new URLSearchParams(
        typeof window !== "undefined" ? window.location.search : "",
      ),
    [],
  );
  const targetRequestId = searchParams.get("requestId");
  const targetConflictId = searchParams.get("conflictId");

  // Create a map of requestId to request for easy lookup
  const requestMap = useMemo(
    () => new Map(requests.map((r) => [r.id, r])),
    [requests]
  );

  // Get all conflicts with their associated requests
  const conflictItems = useMemo(
    () =>
      Array.from(conflicts.entries()).flatMap(([requestId, requestConflicts]) => {
        const request = requestMap.get(requestId);
        if (!request) return [];

        return requestConflicts.map((conflict) => ({
          ...conflict,
          request,
        }));
      }),
    [conflicts, requestMap]
  );

  const visibleConflictItems = useMemo(() => {
    if (!targetRequestId) return conflictItems;
    return conflictItems.filter((item) => item.request.id === targetRequestId);
  }, [conflictItems, targetRequestId]);

  // Handle export
  useExportHandler('conflicts', async (format) => {
    await exportConflicts(visibleConflictItems, format);
    logger.info(`Exported ${visibleConflictItems.length} conflicts as ${format.toUpperCase()}`);
  }, { label: 'Conflicts', description: 'Export the current list of conflicts (import not available).', formats: ['csv'] });

  // Virtualize the (potentially large) conflict list — render only the visible rows. Heights vary
  // (peer link, multi-line messages), so measureElement handles dynamic sizing.
  const scrollRef = useRef<HTMLDivElement>(null);
  // TanStack Virtual's API is not memoizable, so the compiler skips this component. Nothing to fix.
  // eslint-disable-next-line react-hooks/incompatible-library
  const virtualizer = useVirtualizer({
    count: visibleConflictItems.length,
    getScrollElement: () => scrollRef.current,
    estimateSize: () => 104,
    overscan: 8,
    getItemKey: (i) => visibleConflictItems[i].id,
  });

  const description = isLoading
    ? "Checking scheduled requests for conflicts…"
    : isError
      ? "Couldn't load conflicts."
      : visibleConflictItems.length === 0
        ? "No conflicts detected. All scheduled requests meet their requirements."
        : `${visibleConflictItems.length} conflict${visibleConflictItems.length > 1 ? "s" : ""} found in scheduled requests.`;

  return (
    <div className="flex h-full flex-col gap-4 p-1">
      <ConflictTrendChart
        data={conflictsTrend.data}
        bucket={bucket}
        isLoading={conflictsTrend.isLoading}
        error={conflictsTrend.error}
      />

      <p className="text-sm text-muted-foreground">{description}</p>
      {targetRequestId && (
        <p className="-mt-3 text-xs text-muted-foreground">
          Filtered to request {targetRequestId}
        </p>
      )}

      {isLoading ? (
        <div className="py-12">
          <LoadingSpinner fullScreen={false} message="Checking for conflicts…" />
        </div>
      ) : isError ? (
        <div className="flex items-center justify-center py-12 text-muted-foreground">
          <div className="text-center">
            <AlertCircle className="w-12 h-12 mx-auto mb-3 text-destructive opacity-70" />
            <p className="mb-3">Couldn't load conflicts.</p>
            <Button
              variant="outline"
              size="sm"
              onClick={() => { refetchConflicts(); refetchRequests(); }}
            >
              Try again
            </Button>
          </div>
        </div>
      ) : visibleConflictItems.length === 0 ? (
        <div className="flex items-center justify-center py-12 text-muted-foreground">
          <div className="text-center">
            <AlertCircle className="w-12 h-12 mx-auto mb-3 opacity-50" />
            <p>No conflicts to display</p>
          </div>
        </div>
      ) : (
        <ScrollArea type="auto" viewportRef={scrollRef} className="flex-1 min-h-0">
          <div style={{ height: `${virtualizer.getTotalSize()}px`, position: "relative" }}>
            {virtualizer.getVirtualItems().map((vItem) => {
              const item = visibleConflictItems[vItem.index];
              return (
                <div
                  key={vItem.key}
                  data-index={vItem.index}
                  ref={virtualizer.measureElement}
                  style={{ position: "absolute", top: 0, left: 0, width: "100%", transform: `translateY(${vItem.start}px)` }}
                >
                  {/* pb-4 reproduces the previous space-y-4 gap (measured into the row height). */}
                  <div className="pb-4">
                    <ConflictItem
                      item={item}
                      isHighlighted={targetConflictId === item.id}
                      onOpen={(request) => openRequestEditor(request, conflicts.get(request.id) ?? [])}
                      onAskAssistant={askAssistant}
                      assistantAvailable={assistantAvailable}
                      peerRequest={item.peerRequestId ? requestMap.get(item.peerRequestId) : undefined}
                    />
                  </div>
                </div>
              );
            })}
          </div>
        </ScrollArea>
      )}
      {requestEditorDialogs}
    </div>
  );
}
