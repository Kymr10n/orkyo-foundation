import React, { useCallback, useMemo, useRef } from "react";
import { useDraggable } from "@dnd-kit/core";
import { useSchedulerStore, MIN_DURATION_FLOOR_MS, RESIZE_MOVE_THRESHOLD_PX } from "@foundation/src/store/scheduler-store";
import { useResizeGesture } from "@foundation/src/hooks/useResizeGesture";
import type { ResizeGeometry } from "@foundation/src/hooks/useResizeGesture";
import {
  selectRequestDisplayData,
  isOutsideView,
} from "@foundation/src/domain/scheduling/schedule-selectors";
import type { PreviewEntry, ValidationResult } from "@foundation/src/domain/scheduling/schedule-model";
import type { ScheduleIndex } from "@foundation/src/domain/scheduling/schedule-index";
import type { Request } from "@foundation/src/types/requests";
import type { TimeColumn } from "./scheduler-types";
import {
  REQUEST_BAR_BASE_CLASS,
  RequestBarLabel,
  RequestBarLayers,
  requestBarToneClass,
} from "./RequestBarVisual";
import { formatMinutesHuman } from "@foundation/src/lib/utils/utils";

export const ScheduledRequestOverlay = React.memo(function ScheduledRequestOverlay({
  request,
  entry,
  columns,
  scheduleIndex,
  validation,
  onRequestClick,
  onRequestDoubleClick,
  onRequestContextMenu,
  onRequestResize,
  editable = true,
}: {
  request: Request;
  entry: PreviewEntry;
  columns: TimeColumn[];
  scheduleIndex: ScheduleIndex;
  validation: ValidationResult;
  onRequestClick?: (requestId: string, position?: { x: number; y: number }) => void;
  onRequestDoubleClick?: (requestId: string) => void;
  /** Right-click on the bar. Mouse-only by nature — clearing the dates in the request
   *  editor is the keyboard path to the same result. */
  onRequestContextMenu?: (requestId: string, position: { x: number; y: number }) => void;
  onRequestResize?: (requestId: string, startTs: string, endTs: string) => void;
  /** Whether the caller can edit (= canEdit). Editors get drag-to-move + resize
   *  on every device (mouse-move / touch long-press; quick tap still opens via
   *  the tap detection below). Viewers get a plain tappable button. */
  editable?: boolean;
}) {
  // Scheduler store — interaction actions only (no validation, no display state)
  const startResize = useSchedulerStore((s) => s.startResize);
  const updateResize = useSchedulerStore((s) => s.updateResize);
  const commitResize = useSchedulerStore((s) => s.commitResize);
  const cancelResize = useSchedulerStore((s) => s.cancelResize);

  // All hooks must run unconditionally
  const { attributes, listeners, setNodeRef, isDragging } =
    useDraggable({
      id: `scheduled-${request.id}`,
      data: { ...request, isScheduled: true },
      disabled: !editable,
    });

  const overlayRef = useRef<HTMLDivElement | null>(null);

  // Touch tap-vs-drag: dnd-kit's TouchSensor suppresses the synthesized click on
  // touch, so a plain onClick never fires for a draggable bar on a phone. We
  // detect the tap ourselves — a touchend under the sensor's drag threshold
  // (<250ms, <8px, no drag started) opens the request; a longer press / real
  // move is left to dnd-kit as a reschedule drag.
  const tapRef = useRef<{ t: number; x: number; y: number } | null>(null);

  // Document-level resize gesture hook — replaces inline pointer handlers
  // and the setPointerCapture approach on 2px handles.
  const { beginGesture, lastCommitMsRef } = useResizeGesture(
    {
      onStart(edge) {
        if (!request.startTs || !request.endTs) return;
        // The entry is already rendered against one row, and its resourceId is that row's
        // resource — no need to re-derive it from the request's assignments. An unplaced but
        // scheduled request carries its own id as a synthetic resourceId (see toScheduledEntry),
        // and those have no row to resize against.
        const spaceResourceId = entry.resourceId;
        if (!spaceResourceId || spaceResourceId === request.id) return;
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
    // No transform while dragging: the DragOverlay carries a lightweight clone under the
    // pointer instead, so this node — and its whole subtree — is neither transformed nor
    // reconciled on every pointer move. Transforming it here made a populated grid sluggish.
    // What stays behind is a faded ghost marking where the request came from.
    opacity: isDragging ? 0.4 : undefined,
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

  // Screen-reader name — the status word ("Overbooked"/"Assigned") that the
  // colour tint conveys visually, so the cue isn't colour-only (WCAG 1.4.1).
  const ariaLabel = displayData.hasConflict
    ? `${request.name}, Overbooked, ${requestConflicts.length} conflict${requestConflicts.length > 1 ? 's' : ''}. Open request.`
    : `${request.name}, Assigned. Open request.`;

  return (
    <div
      ref={combinedRef}
      style={style}
      className={`absolute ${REQUEST_BAR_BASE_CLASS} group transition motion-reduce:transition-none hover:brightness-95 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring ${requestBarToneClass(status)} ${
        !editable
          ? 'cursor-pointer'
          : isResizing ? 'cursor-ew-resize select-none' : 'cursor-grab active:cursor-grabbing touch-none'
      }`}
      // Click and double-click are distinct actions, and firing both from onClick made
      // the double-click handler the only one that ever ran. The caller decides what a
      // click means; on a conflicted bar it opens the conflict details, which is the
      // question a red bar raises. The click point travels with it so the caller can
      // anchor a popover to the bar the user actually hit.
      onClick={(e) => {
        if (isResizing || Date.now() - lastCommitMsRef.current <= 300) return;
        onRequestClick?.(request.id, { x: e.clientX, y: e.clientY });
      }}
      onDoubleClick={() => {
        if (isResizing || Date.now() - lastCommitMsRef.current <= 300) return;
        onRequestDoubleClick?.(request.id);
      }}
      onContextMenu={onRequestContextMenu ? (e) => {
        e.preventDefault();
        onRequestContextMenu(request.id, { x: e.clientX, y: e.clientY });
      } : undefined}
      title={tooltipText}
      aria-label={ariaLabel}
      {...(editable ? attributes : { role: 'button', tabIndex: 0 })}
      {...(editable ? listeners : {})}
      // Tap-vs-drag on touch — MUST come after {...listeners} so these compose
      // over (not get overridden by) dnd-kit's own onTouchStart activator. dnd
      // eats the synthesized click, so we detect the tap ourselves; dnd's
      // activator still runs (called explicitly) to keep long-press drag working.
      onTouchStart={editable ? (e) => {
        (listeners?.onTouchStart as ((ev: React.TouchEvent) => void) | undefined)?.(e);
        const t = e.touches[0];
        tapRef.current = { t: Date.now(), x: t.clientX, y: t.clientY };
      } : undefined}
      onTouchEnd={editable ? (e) => {
        (listeners?.onTouchEnd as ((ev: React.TouchEvent) => void) | undefined)?.(e);
        const s = tapRef.current;
        tapRef.current = null;
        if (!s || isDragging || isResizing) return;
        const c = e.changedTouches[0];
        const moved = Math.hypot(c.clientX - s.x, c.clientY - s.y);
        if (Date.now() - s.t < 250 && moved < 8 && Date.now() - lastCommitMsRef.current > 300) {
          e.preventDefault(); // suppress any follow-up synthesized click (no double-open)
          onRequestClick?.(request.id, { x: c.clientX, y: c.clientY });
        }
      } : undefined}
      onKeyDown={(e) => {
        // Enter/Space opens the request (details) rather than starting a keyboard
        // drag — grid drops resolve their time from pointer coordinates, so a
        // keyboard drag can't land a valid slot. Rescheduling stays pointer-drag
        // + the "Schedule to…" dialog for backlog. Overrides the dnd keydown.
        if (e.target !== e.currentTarget) return;
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault();
          // The editor, not the click action: a conflict popover anchors to a pointer
          // position that a keypress does not have, and the editor's ConflictBanner
          // carries the same conflict detail.
          if (!isResizing) onRequestDoubleClick?.(request.id);
        }
      }}
    >
      {/* Translucent fill + the overbooked hatch — shared with the drag clone. */}
      <RequestBarLayers status={status} />
      {/* Resize handles — desktop/tablet only (phone is view + tap-to-open). */}
      {editable && (
        <>
          {/* Left resize handle — only needs onPointerDown; move/up go to document */}
          <div
            className="absolute left-0 top-0 bottom-0 w-2 cursor-ew-resize opacity-0 group-hover:opacity-100 hover:bg-foreground/10 transition-opacity motion-reduce:transition-none rounded-l z-20"
            style={{ touchAction: 'none' }}
            onPointerDown={(e) => handleResizePointerDown(e, 'left')}
            onClick={(e) => e.stopPropagation()}
          />
          {/* Right resize handle — only needs onPointerDown; move/up go to document */}
          <div
            className="absolute right-0 top-0 bottom-0 w-2 cursor-ew-resize opacity-0 group-hover:opacity-100 hover:bg-foreground/10 transition-opacity motion-reduce:transition-none rounded-r z-20"
            style={{ touchAction: 'none' }}
            onPointerDown={(e) => handleResizePointerDown(e, 'right')}
            onClick={(e) => e.stopPropagation()}
          />
        </>
      )}
      <RequestBarLabel request={request} hasConflict={displayData.hasConflict} />
    </div>
  );
});
