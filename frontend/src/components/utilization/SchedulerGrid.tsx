import type React from "react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { getResourceGroups } from "@foundation/src/lib/api/resource-groups-api";
// Domain pipeline
import { buildPreviewSchedule } from "@foundation/src/domain/scheduling/schedule-preview";
import { buildIndex } from "@foundation/src/domain/scheduling/schedule-index";
import { evaluateSchedule } from "@foundation/src/domain/scheduling/schedule-validator";
import { getSpaceResourceId } from "@foundation/src/domain/scheduling/request-assignments";
import { useSchedulerStore } from "@foundation/src/store/scheduler-store";
import { useAppStore } from "@foundation/src/store/app-store";
import type { Request } from "@foundation/src/types/requests";
import type { Space } from "@foundation/src/types/space";

const EMPTY_REQUESTS: Request[] = [];
import type { ResourceGroupInfo } from "@foundation/src/lib/api/resource-groups-api";
import type { TimeScale } from "./ScaleSelect";
import type { SpacesByGroup } from "./scheduler-types";
import { SpaceRow } from "./SpaceRow";
import { TimelineGridShell, type ShellGroup } from "./TimelineGridShell";
import type { OffTimeRange } from "@foundation/src/domain/scheduling/types";
import {
  generateTimeColumns,
  parseTimeToHour,
  type WorkingHoursConfig,
} from "./time-grid-utils";
import { enrichColumnsWithOffTime } from "./time-grid-offtime";
import type { Conflict } from "@foundation/src/types/requests";

interface SchedulerGridProps {
  spaces: Space[];
  requests: Request[];
  scale: TimeScale;
  anchorTs: Date;
  timeCursorTs: Date;
  onRequestClick: (requestId: string) => void;
  onRequestDoubleClick?: (requestId: string) => void;
  onRequestResize?: (requestId: string, startTs: string, endTs: string) => void;
  onTimeCursorClick: (ts: Date) => void;
  onAnchorChange?: (ts: Date) => void;
  offTimeRanges?: readonly OffTimeRange[];
  capabilityConflicts?: Map<string, Conflict[]>;
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
  onRequestClick,
  onRequestDoubleClick,
  onRequestResize,
  onTimeCursorClick,
  onAnchorChange,
  offTimeRanges = [],
  capabilityConflicts,
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
  const [groups, setGroups] = useState<ResourceGroupInfo[]>([]);
  const [groupsLoading, setGroupsLoading] = useState(true);

  // ---------------------------------------------------------------------------
  // Scheduling validation pipeline (steps 3–4 of interaction flow)
  // Runs on every render; inputs are stable references when nothing changed.
  // ---------------------------------------------------------------------------
  const draft = useSchedulerStore((s) => s.draft);

  const previewSchedule = useMemo(
    () => buildPreviewSchedule(requests, draft),
    [requests, draft]
  );

  const scheduleIndex = useMemo(
    () => buildIndex(previewSchedule),
    [previewSchedule]
  );

  const schedulingValidation = useMemo(
    () => evaluateSchedule(previewSchedule, undefined, scheduleIndex),
    [previewSchedule, scheduleIndex]
  );

  // Merge backend capability conflicts into the scheduling validation map so
  // the overlay shows the red indicator + tooltip for both conflict kinds.
  const validation = useMemo(() => {
    if (!capabilityConflicts?.size) return schedulingValidation;
    const merged = new Map(schedulingValidation);
    for (const [requestId, capConflicts] of capabilityConflicts) {
      merged.set(requestId, [...(merged.get(requestId) ?? []), ...capConflicts]);
    }
    return merged;
  }, [schedulingValidation, capabilityConflicts]);
  // ---------------------------------------------------------------------------

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

  // Fetch groups
  useEffect(() => {
    getResourceGroups('space').then((g) => {
      setGroups(g);
      setGroupsLoading(false);
    });
  }, []);

  // Memoize sorting + grouping — expensive with 50+ spaces (#5)
  const groupedSpaces = useMemo(() => {
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

    const groupMap = new Map<string, ResourceGroupInfo>();
    groups.forEach((g) => groupMap.set(g.id, g));

    const spacesByGroupId = new Map<string | null, Space[]>();
    sortedSpaces.forEach((space) => {
      const groupId = space.groupId || null;
      if (!spacesByGroupId.has(groupId)) {
        spacesByGroupId.set(groupId, []);
      }
      spacesByGroupId.get(groupId)!.push(space);
    });

    const sortedGroupIds = Array.from(spacesByGroupId.keys()).sort((a, b) => {
      if (a === null) return 1;
      if (b === null) return -1;
      const groupA = groupMap.get(a);
      const groupB = groupMap.get(b);
      if (!groupA || !groupB) return 0;
      return (groupA.displayOrder ?? 0) - (groupB.displayOrder ?? 0);
    });

    const result: SpacesByGroup[] = [];
    sortedGroupIds.forEach((groupId) => {
      const spacesInGroup = spacesByGroupId.get(groupId) || [];
      if (spacesInGroup.length === 0) return;

      if (groupId === null) {
        result.push({
          groupId: "ungrouped",
          groupName: "Ungrouped",
          spaces: spacesInGroup,
        });
      } else {
        const group = groupMap.get(groupId);
        if (group) {
          result.push({
            groupId: group.id,
            groupName: group.name,
            groupColor: group.color,
            spaces: spacesInGroup,
          });
        }
      }
    });
    return result;
  }, [spaces, spaceOrder, groups]);

  // Adapt to the shell's generic group shape.
  const shellGroups = useMemo<ShellGroup<Space>[]>(
    () =>
      groupedSpaces.map((g) => ({
        id: g.groupId || "ungrouped",
        name: g.groupName,
        color: g.groupColor,
        rows: g.spaces,
      })),
    [groupedSpaces],
  );

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
      {/* Edge scroll indicators */}
      {isDraggingCursor && (
        <>
          <div
            className={`absolute top-0 bottom-0 left-52 w-[60px] pointer-events-none transition-opacity duration-150 ${
              edgeScrollDirection === 'left' ? 'opacity-100' : 'opacity-0'
            }`}
            style={{ background: 'linear-gradient(to right, rgba(59, 130, 246, 0.3), transparent)' }}
          />
          <div
            className={`absolute top-0 bottom-0 right-0 w-[60px] pointer-events-none transition-opacity duration-150 ${
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
          className={`absolute top-0 bottom-0 w-0.5 z-20 transition-colors ${
            edgeScrollDirection ? 'bg-blue-400 animate-pulse' : 'bg-blue-500'
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
            } transition-all`}
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
        scheduleIndex={scheduleIndex}
        validation={validation}
        timeCursorTs={timeCursorTs}
        onRequestClick={onRequestClick}
        onRequestDoubleClick={onRequestDoubleClick}
        onRequestResize={onRequestResize}
        offTimeRanges={offTimeRanges}
      />
    ),
    [columns, requestsBySpaceId, scheduleIndex, validation, timeCursorTs, onRequestClick, onRequestDoubleClick, onRequestResize, offTimeRanges],
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
