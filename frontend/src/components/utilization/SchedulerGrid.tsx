import type React from "react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { getResourceGroups } from "@foundation/src/lib/api/resource-groups-api";
import { qk } from "@foundation/src/lib/api/query-keys";
// Domain pipeline
import { buildCommittedSchedule, applyDraft } from "@foundation/src/domain/scheduling/schedule-preview";
import { buildIndex, replaceIndexEntry, getOverlapping } from "@foundation/src/domain/scheduling/schedule-index";
import { evaluateEntry } from "@foundation/src/domain/scheduling/schedule-validator";
import { getSpaceResourceId } from "@foundation/src/domain/scheduling/request-assignments";
import { useConflictRegistry } from "@foundation/src/hooks/useConflictRegistry";
import { useSchedulerStore } from "@foundation/src/store/scheduler-store";
import { useAppStore } from "@foundation/src/store/app-store";
import type { PreviewEntry, ValidationResult } from "@foundation/src/domain/scheduling/schedule-model";
import type { Request } from "@foundation/src/types/requests";
import type { Space } from "@foundation/src/types/space";

const EMPTY_REQUESTS: Request[] = [];
const EMPTY_ENTRIES: readonly PreviewEntry[] = [];
const EMPTY_VALIDATION: ValidationResult = new Map();
import type { ResourceGroupInfo } from "@foundation/src/lib/api/resource-groups-api";
import type { TimeScale } from "./ScaleSelect";
import { groupRowsByResourceGroup } from "./scheduler-types";
import { SpaceRow } from "./SpaceRow";
import { NowLine } from "./NowLine";
import { TimelineGridShell, type ShellGroup } from "./TimelineGridShell";
import type { OffTimeRange } from "@foundation/src/domain/scheduling/types";
import {
  generateTimeColumns,
  parseTimeToHour,
  type WorkingHoursConfig,
} from "./time-grid-utils";
import { enrichColumnsWithOffTime } from "./time-grid-offtime";

interface SchedulerGridProps {
  spaces: Space[];
  requests: Request[];
  scale: TimeScale;
  anchorTs: Date;
  timeCursorTs: Date;
  /** Live wall-clock "now" (epoch ms) shared with the operational status recompute — drives the Now line. */
  nowMs: number;
  onRequestClick: (requestId: string) => void;
  onRequestDoubleClick?: (requestId: string) => void;
  onRequestResize?: (requestId: string, startTs: string, endTs: string) => void;
  onTimeCursorClick: (ts: Date) => void;
  onAnchorChange?: (ts: Date) => void;
  offTimeRanges?: readonly OffTimeRange[];
  weekendsEnabled?: boolean;
  workingHoursEnabled?: boolean;
  workingDayStart?: string;
  workingDayEnd?: string;
}

export function SchedulerGrid({
  spaces,
  requests,
  scale,
  anchorTs,
  timeCursorTs,
  nowMs,
  onRequestClick,
  onRequestDoubleClick,
  onRequestResize,
  onTimeCursorClick,
  onAnchorChange,
  offTimeRanges = [],
  weekendsEnabled = false,
  workingHoursEnabled = false,
  workingDayStart = "08:00",
  workingDayEnd = "17:00",
}: SchedulerGridProps) {
  const workingHours: WorkingHoursConfig | null = workingHoursEnabled
    ? { enabled: true, start: parseTimeToHour(workingDayStart), end: parseTimeToHour(workingDayEnd) }
    : null;
  const columns = useMemo(
    () =>
      enrichColumnsWithOffTime(
        generateTimeColumns(scale, anchorTs, weekendsEnabled, workingHours),
        offTimeRanges,
      ),
    // workingHours is derived from the two string props + the flag.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [scale, anchorTs, weekendsEnabled, workingHoursEnabled, workingDayStart, workingDayEnd, offTimeRanges],
  );
  const spaceOrder = useAppStore((s) => s.spaceOrder);

  // ---------------------------------------------------------------------------
  // Scheduling validation pipeline (steps 3–4 of interaction flow)
  //
  // Structured for referential stability during a drag: the committed schedule/
  // index are built once per `requests` change, and the draft overlay replaces
  // only the dragged entry's object and its space's index slice per pointer
  // frame — so SpaceRow/ScheduledRequestOverlay React.memo hold for every
  // untouched space.
  // ---------------------------------------------------------------------------
  const draft = useSchedulerStore((s) => s.draft);

  const committedSchedule = useMemo(() => buildCommittedSchedule(requests), [requests]);
  const committedIndex = useMemo(() => buildIndex(committedSchedule), [committedSchedule]);

  const previewSchedule = useMemo(
    () => applyDraft(committedSchedule, draft),
    [committedSchedule, draft]
  );

  const scheduleIndex = useMemo(() => {
    if (previewSchedule === committedSchedule || !draft) return committedIndex;
    const prev = committedSchedule.get(draft.requestId);
    const next = previewSchedule.get(draft.requestId);
    if (!prev || !next) return committedIndex;
    return replaceIndexEntry(committedIndex, prev, next);
  }, [committedSchedule, committedIndex, previewSchedule, draft]);

  // The backend is the single source of truth for committed bookings' conflicts
  // (overlap/overbook, capability, off-time, site). The grid reads them from the
  // tenant-wide registry — the same source the Conflicts page and Requests-page
  // badges use — so the three views agree by construction.
  const { conflictsByRequest: committedConflicts } = useConflictRegistry();

  // Pre-group requests by space to avoid O(n×m) filtering inside each SpaceRow.
  const requestsBySpaceId = useMemo(() => {
    const map = new Map<string, Request[]>();
    for (const r of requests) {
      const spaceId = getSpaceResourceId(r);
      if (!spaceId) continue;
      const list = map.get(spaceId) ?? [];
      list.push(r);
      map.set(spaceId, list);
    }
    return map;
  }, [requests]);

  // Registry conflicts sliced per space, so each SpaceRow only receives (and
  // only re-renders for) the validation of its own requests.
  const committedValidationBySpace = useMemo(() => {
    const result = new Map<string, ValidationResult>();
    for (const [spaceId, spaceRequests] of requestsBySpaceId) {
      let slice: ValidationResult | null = null;
      for (const r of spaceRequests) {
        const conflicts = committedConflicts.get(r.id);
        if (conflicts && conflicts.length > 0) (slice ??= new Map()).set(r.id, conflicts);
      }
      result.set(spaceId, slice ?? EMPTY_VALIDATION);
    }
    return result;
  }, [requestsBySpaceId, committedConflicts]);

  // Draft overlay: while a bar is being resized, re-evaluate overlap/duration
  // client-side for just the dragged entry and the peers it now overlaps, so the
  // feedback is instant before the mutation commits and the registry refetches.
  // Backend-only conflict kinds for those ids are restored on the next refetch.
  // Only the draft's space gets a new validation slice — all affected entries
  // (draft + overlap peers) live in that space.
  const validationBySpace = useMemo(() => {
    if (!draft) return committedValidationBySpace;
    const draftEntry = previewSchedule.get(draft.requestId);
    if (!draftEntry) return committedValidationBySpace;

    const affected = new Set<string>([draft.requestId]);
    for (const peer of getOverlapping(scheduleIndex, draftEntry)) {
      affected.add(peer.requestId);
    }

    const spaceId = draftEntry.resourceId;
    const slice = new Map(committedValidationBySpace.get(spaceId) ?? EMPTY_VALIDATION);
    for (const id of affected) {
      const entry = previewSchedule.get(id);
      if (!entry) continue;
      const conflicts = evaluateEntry(entry, scheduleIndex);
      if (conflicts.length > 0) slice.set(id, conflicts);
      else slice.delete(id);
    }

    const merged = new Map(committedValidationBySpace);
    merged.set(spaceId, slice);
    return merged;
  }, [committedValidationBySpace, draft, previewSchedule, scheduleIndex]);
  // ---------------------------------------------------------------------------

  // Fetch groups — shared cache with everything else reading space groups.
  const { data: groups = [], isLoading: groupsLoading } = useQuery<ResourceGroupInfo[]>({
    queryKey: qk.resourceGroups.byType('space'),
    queryFn: () => getResourceGroups('space'),
  });

  // Memoize sorting + grouping — expensive with 50+ spaces (#5). Spaces are
  // sorted (manual order, then code) before bucketing so each group keeps that
  // order. Empty groups are dropped (includeEmpty: false).
  const shellGroups = useMemo<ShellGroup<Space>[]>(() => {
    const sortedSpaces = [...spaces].sort((a, b) => {
      if (spaceOrder.length > 0) {
        const indexA = spaceOrder.indexOf(a.id);
        const indexB = spaceOrder.indexOf(b.id);
        if (indexA !== -1 && indexB !== -1) return indexA - indexB;
        if (indexA !== -1) return -1;
        if (indexB !== -1) return 1;
      }
      const codeA = a.code || '';
      const codeB = b.code || '';
      return codeA.localeCompare(codeB);
    });

    return groupRowsByResourceGroup(
      sortedSpaces,
      groups,
      (space) => (space.groupId ? [space.groupId] : []),
      { includeEmpty: false },
    );
  }, [spaces, spaceOrder, groups]);

  // ---------------------------------------------------------------------------
  // Draggable time cursor + edge-scroll (Spaces-only). Rendered as bodyOverlay.
  // ---------------------------------------------------------------------------
  const timeColumnsRef = useRef<HTMLDivElement>(null);
  const [isDraggingCursor, setIsDraggingCursor] = useState(false);
  const [edgeScrollDirection, setEdgeScrollDirection] = useState<'left' | 'right' | null>(null);
  const edgeScrollRef = useRef<number | null>(null);
  const lastMouseXRef = useRef(0);
  // Stable refs for the edge-scroll rAF loop — avoids stale-closure stall when
  // anchorTs/timeCursorTs change mid-scroll (the loop captures them at creation).
  const anchorTsRef = useRef(anchorTs);
  const timeCursorTsRef = useRef(timeCursorTs);
  useEffect(() => { anchorTsRef.current = anchorTs; }, [anchorTs]);
  useEffect(() => { timeCursorTsRef.current = timeCursorTs; }, [timeCursorTs]);

  // Calculate time cursor position as percentage
  const calculateCursorPosition = (): number => {
    const totalDuration = columns[columns.length - 1].end.getTime() - columns[0].start.getTime();
    const cursorOffset = timeCursorTs.getTime() - columns[0].start.getTime();
    return Math.max(0, Math.min(100, (cursorOffset / totalDuration) * 100));
  };

  // Edge scroll threshold (pixels from edge to start scrolling)
  const EDGE_THRESHOLD = 60;
  // Maximum scroll speed factor
  const MAX_SPEED_FACTOR = 2;
  // Base scroll speed: fraction of one column per tick
  const BASE_SCROLL_SPEED = 0.3;

  // Handle cursor drag
  const handleCursorMouseDown = (e: React.MouseEvent) => {
    e.preventDefault();
    setIsDraggingCursor(true);
    lastMouseXRef.current = e.clientX;
  };

  // Stop edge scrolling
  const stopEdgeScroll = useCallback(() => {
    if (edgeScrollRef.current) {
      cancelAnimationFrame(edgeScrollRef.current);
      edgeScrollRef.current = null;
    }
    setEdgeScrollDirection(null);
  }, []);

  // Edge scroll animation loop
  const edgeScrollLoop = useCallback(() => {
    if (!isDraggingCursor || !timeColumnsRef.current || !onAnchorChange) {
      stopEdgeScroll();
      return;
    }

    const rect = timeColumnsRef.current.getBoundingClientRect();
    const x = lastMouseXRef.current - rect.left;
    const width = rect.width;

    // Check if we're in the edge zones
    const leftEdgeDistance = x;
    const rightEdgeDistance = width - x;

    // Calculate one column's duration in ms for consistent scrolling
    const columnDurationMs = columns.length >= 2
      ? columns[1].start.getTime() - columns[0].start.getTime()
      : 0;

    if (columnDurationMs === 0) {
      stopEdgeScroll();
      return;
    }

    if (leftEdgeDistance < EDGE_THRESHOLD && leftEdgeDistance >= 0) {
      // Scrolling left (back in time)
      setEdgeScrollDirection('left');
      const speedFactor = 1 + (1 - leftEdgeDistance / EDGE_THRESHOLD) * (MAX_SPEED_FACTOR - 1);
      const shiftMs = columnDurationMs * BASE_SCROLL_SPEED * speedFactor;
      const newAnchor = new Date(anchorTsRef.current.getTime() - shiftMs);
      onAnchorChange(newAnchor);

      // Also move the cursor to stay in sync
      const newCursorTime = new Date(timeCursorTsRef.current.getTime() - shiftMs);
      onTimeCursorClick(newCursorTime);
    } else if (rightEdgeDistance < EDGE_THRESHOLD && rightEdgeDistance >= 0) {
      // Scrolling right (forward in time)
      setEdgeScrollDirection('right');
      const speedFactor = 1 + (1 - rightEdgeDistance / EDGE_THRESHOLD) * (MAX_SPEED_FACTOR - 1);
      const shiftMs = columnDurationMs * BASE_SCROLL_SPEED * speedFactor;
      const newAnchor = new Date(anchorTsRef.current.getTime() + shiftMs);
      onAnchorChange(newAnchor);

      // Also move the cursor to stay in sync
      const newCursorTime = new Date(timeCursorTsRef.current.getTime() + shiftMs);
      onTimeCursorClick(newCursorTime);
    }

    // Continue the loop at ~20fps for smooth scrolling
    edgeScrollRef.current = requestAnimationFrame(() => {
      setTimeout(() => edgeScrollLoop(), 50);
    });
  }, [isDraggingCursor, onAnchorChange, columns, onTimeCursorClick, stopEdgeScroll]);

  const handleMouseMove = useCallback((e: MouseEvent) => {
    if (!isDraggingCursor || !timeColumnsRef.current) return;

    lastMouseXRef.current = e.clientX;
    const rect = timeColumnsRef.current.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const width = rect.width;

    // Check if in edge zone to start edge scrolling
    const leftEdgeDistance = x;
    const rightEdgeDistance = width - x;
    const inEdgeZone = (leftEdgeDistance < EDGE_THRESHOLD && leftEdgeDistance >= 0) ||
                       (rightEdgeDistance < EDGE_THRESHOLD && rightEdgeDistance >= 0);

    if (inEdgeZone && onAnchorChange) {
      // Start edge scroll if not already running
      if (!edgeScrollRef.current) {
        edgeScrollLoop();
      }
    } else {
      // Stop edge scrolling and update cursor position normally
      stopEdgeScroll();

      const percentage = Math.max(0, Math.min(100, (x / width) * 100));
      const totalDuration = columns[columns.length - 1].end.getTime() - columns[0].start.getTime();
      const timeOffset = (percentage / 100) * totalDuration;
      const newTime = new Date(columns[0].start.getTime() + timeOffset);
      onTimeCursorClick(newTime);
    }
  }, [isDraggingCursor, columns, onTimeCursorClick, onAnchorChange, edgeScrollLoop, stopEdgeScroll]);

  const handleMouseUp = useCallback(() => {
    setIsDraggingCursor(false);
    stopEdgeScroll();
  }, [stopEdgeScroll]);

  // Attach global mouse listeners for drag
  useEffect(() => {
    if (isDraggingCursor) {
      document.addEventListener('mousemove', handleMouseMove);
      document.addEventListener('mouseup', handleMouseUp);
      return () => {
        document.removeEventListener('mousemove', handleMouseMove);
        document.removeEventListener('mouseup', handleMouseUp);
        stopEdgeScroll();
      };
    }
  }, [isDraggingCursor, handleMouseMove, handleMouseUp, stopEdgeScroll]);

  const cursorPosition = calculateCursorPosition();

  const cursorOverlay = (
    <>
      {/* Fixed real-now marker (distinct from the draggable blue scrubber); shows which instant the
          clock-derived "In Progress" status refers to. Hidden when "now" is outside the visible range. */}
      <NowLine
        nowMs={nowMs}
        viewStartMs={columns[0].start.getTime()}
        viewEndMs={columns[columns.length - 1].end.getTime()}
      />

      {/* Edge scroll indicators */}
      {isDraggingCursor && (
        <>
          <div
            className={`absolute top-0 bottom-0 left-52 w-[60px] pointer-events-none transition-opacity duration-150 motion-reduce:transition-none ${
              edgeScrollDirection === 'left' ? 'opacity-100' : 'opacity-0'
            }`}
            style={{ background: 'linear-gradient(to right, rgba(59, 130, 246, 0.3), transparent)' }}
          />
          <div
            className={`absolute top-0 bottom-0 right-0 w-[60px] pointer-events-none transition-opacity duration-150 motion-reduce:transition-none ${
              edgeScrollDirection === 'right' ? 'opacity-100' : 'opacity-0'
            }`}
            style={{ background: 'linear-gradient(to left, rgba(59, 130, 246, 0.3), transparent)' }}
          />
        </>
      )}

      {/* Draggable time cursor line — positioned after the space column */}
      <div
        ref={timeColumnsRef}
        className="absolute top-0 bottom-0 left-52 right-0 pointer-events-none"
      >
        <div
          className={`absolute top-0 bottom-0 w-0.5 z-20 transition-colors motion-reduce:transition-none ${
            edgeScrollDirection ? 'bg-blue-400 animate-pulse motion-reduce:animate-none' : 'bg-blue-500'
          }`}
          style={{ left: `${cursorPosition}%` }}
        >
          <div
            className={`absolute top-0 left-1/2 -translate-x-1/2 w-4 h-full cursor-ew-resize pointer-events-auto ${
              isDraggingCursor ? 'w-6' : ''
            }`}
            onMouseDown={handleCursorMouseDown}
          />
          <div
            className={`absolute -top-1 left-1/2 -translate-x-1/2 w-3 h-3 rounded-full border-2 border-blue-500 pointer-events-none ${
              isDraggingCursor ? 'bg-blue-500 scale-125' : 'bg-background'
            } transition-all motion-reduce:transition-none`}
          />
        </div>
      </div>
    </>
  );

  const renderRow = useCallback(
    (space: Space) => (
      <SpaceRow
        space={space}
        columns={columns}
        spaceRequests={requestsBySpaceId.get(space.id) ?? EMPTY_REQUESTS}
        spaceEntries={scheduleIndex.bySpace.get(space.id) ?? EMPTY_ENTRIES}
        validation={validationBySpace.get(space.id) ?? EMPTY_VALIDATION}
        onRequestClick={onRequestClick}
        onRequestDoubleClick={onRequestDoubleClick}
        onRequestResize={onRequestResize}
        offTimeRanges={offTimeRanges}
      />
    ),
    [columns, requestsBySpaceId, scheduleIndex, validationBySpace, onRequestClick, onRequestDoubleClick, onRequestResize, offTimeRanges],
  );

  return (
    <TimelineGridShell<Space>
      labelHeader="Space"
      columns={columns}
      scale={scale}
      groups={shellGroups}
      collapseIdPrefix="spaces"
      getRowId={(s) => s.id}
      emptyMessage="No spaces available"
      isLoading={groupsLoading}
      onColumnHeaderClick={(col) => onTimeCursorClick(col.start)}
      sortable
      bodyOverlay={cursorOverlay}
      renderRow={renderRow}
    />
  );
}
