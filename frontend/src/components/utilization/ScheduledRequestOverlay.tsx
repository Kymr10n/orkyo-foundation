import React, { useCallback, useMemo, useRef } from "react";
import { useDraggable } from "@dnd-kit/core";
import { CSS } from "@dnd-kit/utilities";
import { AlertCircle, Layers } from "lucide-react";
import { useSchedulerStore, MIN_DURATION_FLOOR_MS, RESIZE_MOVE_THRESHOLD_PX } from "@foundation/src/store/scheduler-store";
import { useResizeGesture } from "@foundation/src/hooks/useResizeGesture";
import { getSpaceResourceId } from "@foundation/src/domain/scheduling/request-assignments";
import type { ResizeGeometry } from "@foundation/src/hooks/useResizeGesture";
import {
  selectRequestDisplayData,
  isOutsideView,
} from "@foundation/src/domain/scheduling/schedule-selectors";
import type { PreviewEntry, ValidationResult } from "@foundation/src/domain/scheduling/schedule-model";
import type { ScheduleIndex } from "@foundation/src/domain/scheduling/schedule-index";
import type { Request } from "@foundation/src/types/requests";
import type { TimeColumn } from "./scheduler-types";
import { STATUS_CELL_CLASS, STATUS_BORDER_CLASS, STATUS_FILL_CLASS } from "./schedule-colors";
import { formatMinutesHuman } from "@foundation/src/lib/utils/utils";

export const ScheduledRequestOverlay = React.memo(function ScheduledRequestOverlay({
  request,
  entry,
  columns,
  scheduleIndex,
  validation,
  onRequestClick,
  onRequestDoubleClick,
  onRequestResize,
}: {
  request: Request;
  entry: PreviewEntry;
  columns: TimeColumn[];
  scheduleIndex: ScheduleIndex;
  validation: ValidationResult;
  onRequestClick: (requestId: string) => void;
  onRequestDoubleClick?: (requestId: string) => void;
  onRequestResize?: (requestId: string, startTs: string, endTs: string) => void;
}) {
  // Scheduler store — interaction actions only (no validation, no display state)
  const startResize = useSchedulerStore((s) => s.startResize);
  const updateResize = useSchedulerStore((s) => s.updateResize);
  const commitResize = useSchedulerStore((s) => s.commitResize);
  const cancelResize = useSchedulerStore((s) => s.cancelResize);

  // All hooks must run unconditionally
  const { attributes, listeners, setNodeRef, transform, isDragging } =
    useDraggable({
      id: `scheduled-${request.id}`,
      data: { ...request, isScheduled: true },
    });

  const overlayRef = useRef<HTMLDivElement | null>(null);

  // Document-level resize gesture hook — replaces inline pointer handlers
  // and the setPointerCapture approach on 2px handles.
  const { beginGesture, lastCommitMsRef } = useResizeGesture(
    {
      onStart(edge) {
        if (!request.startTs || !request.endTs) return;
        const spaceResourceId = getSpaceResourceId(request);
        if (!spaceResourceId) return;
        startResize({
          requestId: request.id,
          resourceId: spaceResourceId,
          edge,
          committedStartMs: new Date(request.startTs).getTime(),
          committedEndMs: new Date(request.endTs).getTime(),
        });
      },
      onUpdate(startMs, endMs) {
        updateResize(startMs, endMs);
      },
      onCommit(_result) {
        const bounds = commitResize();
        if (bounds) {
          onRequestResize?.(
            request.id,
            new Date(bounds.startMs).toISOString(),
            new Date(bounds.endMs).toISOString(),
          );
        }
      },
      onCancel() {
        cancelResize();
      },
    },
    { thresholdPx: RESIZE_MOVE_THRESHOLD_PX, minDurationMs: MIN_DURATION_FLOOR_MS },
  );

  const combinedRef = useCallback((el: HTMLDivElement | null) => {
    setNodeRef(el);
    overlayRef.current = el;
  }, [setNodeRef]);

  const handleResizePointerDown = useCallback((e: React.PointerEvent<HTMLDivElement>, edge: 'left' | 'right') => {
    if (!request.startTs || !request.endTs || !overlayRef.current?.parentElement) return;

    const container = overlayRef.current.parentElement.getBoundingClientRect();
    const totalDurationMs = columns[columns.length - 1].end.getTime() - columns[0].start.getTime();

    const geometry: ResizeGeometry = {
      origStartMs: new Date(request.startTs).getTime(),
      origEndMs: new Date(request.endTs).getTime(),
      containerWidthPx: container.width,
      totalDurationMs,
    };

    beginGesture(e, edge, geometry);
  }, [request.startTs, request.endTs, columns, beginGesture]);

  // --- All hooks must be called before any conditional return ---

  const viewStartMs = columns[0].start.getTime();
  const viewEndMs = columns[columns.length - 1].end.getTime();

  // All rendering derived from the domain pipeline
  const displayData = useMemo(
    () => selectRequestDisplayData(entry, scheduleIndex, validation, viewStartMs, viewEndMs),
    [entry, scheduleIndex, validation, viewStartMs, viewEndMs],
  );

  // Draft stays alive during "committing" phase — no snap-back.
  // buildPreviewSchedule still sees the draft bounds until finalizeDraft clears it.
  if (isOutsideView(entry, viewStartMs, viewEndMs)) {
    return null;
  }

  const isResizing = entry.isDraft; // true while store has an active or committing draft for us

  const style = {
    left: `${displayData.leftPercent}%`,
    width: `${displayData.widthPercent}%`,
    top: `${displayData.topPx}px`,
    height: `${displayData.heightPx}px`,
    transform: isResizing ? undefined : CSS.Translate.toString(transform),
    opacity: isDragging ? 0.5 : 1,
    zIndex: displayData.zIndex,
  };

  // Match the People grid: status-tinted track + colored border (outline) + translucent fill,
  // all from the shared schedule-colors tokens. A scheduled request reads as a fully "occupied"
  // block (→ assigned palette); conflicts use the overbooked palette.
  const status = displayData.hasConflict ? 'overbooked' : 'assigned';

  const requestConflicts = validation.get(request.id) ?? [];
  const grossLabel = request.actualDurationValue != null && request.actualDurationValue > 0
    ? ` | Gross: ${formatMinutesHuman(request.actualDurationValue)}`
    : '';
  const tooltipText = displayData.hasConflict
    ? `${request.name} (${requestConflicts.length} conflict${requestConflicts.length > 1 ? 's' : ''})${grossLabel}`
    : `${request.name} — Net: ${request.minimalDurationValue} ${request.minimalDurationUnit}${grossLabel}`;

  return (
    <div
      ref={combinedRef}
      style={style}
      className={`absolute rounded border text-xs text-foreground p-1 overflow-hidden group transition hover:brightness-95 ${STATUS_CELL_CLASS[status]} ${STATUS_BORDER_CLASS[status]} ${
        isResizing ? 'cursor-ew-resize select-none' : 'cursor-grab active:cursor-grabbing'
      }`}
      onClick={() => { if (!isResizing && Date.now() - lastCommitMsRef.current > 300) { onRequestClick(request.id); onRequestDoubleClick?.(request.id); } }}
      title={tooltipText}
      {...attributes}
      {...listeners}
    >
      {/* Translucent fill over the tinted track — gives the bar the same weight as a fully
          allocated People segment. */}
      {STATUS_FILL_CLASS[status] && (
        <div className={`absolute inset-0 ${STATUS_FILL_CLASS[status]}`} aria-hidden="true" />
      )}
      {/* Left resize handle — only needs onPointerDown; move/up go to document */}
      <div
        className="absolute left-0 top-0 bottom-0 w-2 cursor-ew-resize opacity-0 group-hover:opacity-100 hover:bg-foreground/10 transition-opacity rounded-l z-20"
        style={{ touchAction: 'none' }}
        onPointerDown={(e) => handleResizePointerDown(e, 'left')}
        onClick={(e) => e.stopPropagation()}
      />
      {/* Right resize handle — only needs onPointerDown; move/up go to document */}
      <div
        className="absolute right-0 top-0 bottom-0 w-2 cursor-ew-resize opacity-0 group-hover:opacity-100 hover:bg-foreground/10 transition-opacity rounded-r z-20"
        style={{ touchAction: 'none' }}
        onPointerDown={(e) => handleResizePointerDown(e, 'right')}
        onClick={(e) => e.stopPropagation()}
      />
      <div className="relative z-10 flex items-center gap-1">
        {displayData.hasConflict && (
          <AlertCircle className="w-3 h-3 flex-shrink-0" />
        )}
        {request.planningMode === 'summary' && (
          <Layers className="w-3 h-3 flex-shrink-0 opacity-70" />
        )}
        {request.parentRequestId && (
          <span className="w-1.5 h-1.5 rounded-full bg-current opacity-50 flex-shrink-0" />
        )}
        <div className="truncate font-medium">{request.name}</div>
      </div>
    </div>
  );
});
