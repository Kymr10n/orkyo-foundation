import type React from "react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { getSpaceGroups } from "@foundation/src/lib/api/space-groups-api";
// Domain pipeline
import { buildPreviewSchedule } from "@foundation/src/domain/scheduling/schedule-preview";
import { buildIndex } from "@foundation/src/domain/scheduling/schedule-index";
import { evaluateSchedule } from "@foundation/src/domain/scheduling/schedule-validator";
import { useSchedulerStore } from "@foundation/src/store/scheduler-store";
import { useAppStore } from "@foundation/src/store/app-store";
import { useShallow } from "zustand/react/shallow";
import type { Request } from "@foundation/src/types/requests";
import type { Space } from "@foundation/src/types/space";
import type { SpaceGroup } from "@foundation/src/types/spaceGroup";
import { SortableContext, verticalListSortingStrategy } from "@dnd-kit/sortable";
import { addDays, addHours, addMinutes, addMonths, addWeeks, format, isWeekend, startOfDay, startOfHour, startOfMonth, startOfWeek } from "date-fns";
import type { TimeScale } from "./ScaleSelect";
import type { TimeColumn, SpacesByGroup } from "./scheduler-types";
import { GroupHeader } from "./GroupHeader";
import { SpaceRow } from "./SpaceRow";
import type { OffTimeRange } from "@foundation/src/domain/scheduling/types";

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
  weekendsEnabled?: boolean;
  workingHoursEnabled?: boolean;
  workingDayStart?: string;
  workingDayEnd?: string;
}

interface WorkingHoursConfig {
  enabled: boolean;
  start: number; // hour, e.g. 8
  end: number;   // hour, e.g. 17
}

function parseTimeToHour(time: string): number {
  const [h] = time.split(":").map(Number);
  return h;
}

function isHourOutsideWorkingHours(hour: number, wh: WorkingHoursConfig | null): boolean {
  if (!wh?.enabled) return false;
  return hour < wh.start || hour >= wh.end;
}

function generateColumns(scale: TimeScale, anchorTs: Date, weekendsEnabled = false, workingHours: WorkingHoursConfig | null = null): TimeColumn[] {
  const columns: TimeColumn[] = [];

  switch (scale) {
    case "year": {
      // Show 12 months starting from the anchor's month
      const monthStart = startOfMonth(anchorTs);
      for (let i = 0; i < 12; i++) {
        const start = addMonths(monthStart, i);
        const end = addMonths(start, 1);
        columns.push({ start, end, label: format(start, "MMM") });
      }
      break;
    }
    case "month": {
      // Show 5 weeks starting from the anchor's week
      const weekStart = startOfWeek(anchorTs, { weekStartsOn: 1 });
      for (let i = 0; i < 5; i++) {
        const start = addWeeks(weekStart, i);
        const end = addWeeks(start, 1);
        columns.push({ start, end, label: format(start, "MMM dd") });
      }
      break;
    }
    case "week": {
      // Always show 7 days; mark weekends when weekendsEnabled
      const dayStart = startOfDay(anchorTs);
      for (let i = 0; i < 7; i++) {
        const start = addDays(dayStart, i);
        const end = addDays(start, 1);
        columns.push({
          start,
          end,
          label: format(start, "EEE dd"),
          isWeekend: weekendsEnabled && isWeekend(start),
        });
      }
      break;
    }
    case "day": {
      // Show 24 hours starting from the anchor's hour
      const hourStart = startOfHour(anchorTs);
      for (let i = 0; i < 24; i++) {
        const start = addHours(hourStart, i);
        const end = addHours(start, 1);
        columns.push({
          start,
          end,
          label: format(start, "HH:00"),
          isOutsideWorkingHours: isHourOutsideWorkingHours(start.getHours(), workingHours),
        });
      }
      break;
    }
    case "hour": {
      // Show 4x 15-minute slots starting from the anchor's nearest 15-min mark
      const hourStart = startOfHour(anchorTs);
      const minuteSlot = Math.floor(anchorTs.getMinutes() / 15) * 15;
      const slotStart = new Date(hourStart);
      slotStart.setMinutes(minuteSlot);
      for (let i = 0; i < 4; i++) {
        const start = addMinutes(slotStart, i * 15);
        const end = addMinutes(start, 15);
        columns.push({
          start,
          end,
          label: format(start, "HH:mm"),
          isOutsideWorkingHours: isHourOutsideWorkingHours(start.getHours(), workingHours),
        });
      }
      break;
    }
  }

  return columns;
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
  weekendsEnabled = false,
  workingHoursEnabled = false,
  workingDayStart = "08:00",
  workingDayEnd = "17:00",
}: SchedulerGridProps) {
  const workingHours: WorkingHoursConfig | null = workingHoursEnabled
    ? { enabled: true, start: parseTimeToHour(workingDayStart), end: parseTimeToHour(workingDayEnd) }
    : null;
  const columns = generateColumns(scale, anchorTs, weekendsEnabled, workingHours);
  const { spaceOrder, collapsedGroupIds, toggleGroupCollapse } = useAppStore(
    useShallow((state) => ({
      spaceOrder: state.spaceOrder,
      collapsedGroupIds: state.collapsedGroupIds,
      toggleGroupCollapse: state.toggleGroupCollapse,
    }))
  );
  const [groups, setGroups] = useState<SpaceGroup[]>([]);

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

  const validation = useMemo(
    () => evaluateSchedule(previewSchedule),
    [previewSchedule]
  );
  // ---------------------------------------------------------------------------

  // Fetch groups
  useEffect(() => {
    getSpaceGroups().then(setGroups);
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

    const groupMap = new Map<string, SpaceGroup>();
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
      return groupA.displayOrder - groupB.displayOrder;
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

  // Flat sorted list derived from grouped result (for SortableContext)
  const sortedSpaces = useMemo(
    () => groupedSpaces.flatMap((g) => g.spaces),
    [groupedSpaces]
  );

  const headerScrollRef = useRef<HTMLDivElement>(null);
  const bodyScrollRef = useRef<HTMLDivElement>(null);
  const _gridRef = useRef<HTMLDivElement>(null);
  const timeColumnsRef = useRef<HTMLDivElement>(null);
  const [isDraggingCursor, setIsDraggingCursor] = useState(false);
  const [edgeScrollDirection, setEdgeScrollDirection] = useState<'left' | 'right' | null>(null);
  const edgeScrollRef = useRef<number | null>(null);
  const lastMouseXRef = useRef(0);

  const handleHeaderScroll = () => {
    if (headerScrollRef.current && bodyScrollRef.current) {
      bodyScrollRef.current.scrollLeft = headerScrollRef.current.scrollLeft;
    }
  };

  const handleBodyScroll = () => {
    if (headerScrollRef.current && bodyScrollRef.current) {
      headerScrollRef.current.scrollLeft = bodyScrollRef.current.scrollLeft;
    }
  };

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
      const newAnchor = new Date(anchorTs.getTime() - shiftMs);
      onAnchorChange(newAnchor);

      // Also move the cursor to stay in sync
      const newCursorTime = new Date(timeCursorTs.getTime() - shiftMs);
      onTimeCursorClick(newCursorTime);
    } else if (rightEdgeDistance < EDGE_THRESHOLD && rightEdgeDistance >= 0) {
      // Scrolling right (forward in time)
      setEdgeScrollDirection('right');
      const speedFactor = 1 + (1 - rightEdgeDistance / EDGE_THRESHOLD) * (MAX_SPEED_FACTOR - 1);
      const shiftMs = columnDurationMs * BASE_SCROLL_SPEED * speedFactor;
      const newAnchor = new Date(anchorTs.getTime() + shiftMs);
      onAnchorChange(newAnchor);

      // Also move the cursor to stay in sync
      const newCursorTime = new Date(timeCursorTs.getTime() + shiftMs);
      onTimeCursorClick(newCursorTime);
    }

    // Continue the loop at ~20fps for smooth scrolling
    edgeScrollRef.current = requestAnimationFrame(() => {
      setTimeout(() => edgeScrollLoop(), 50);
    });
  }, [isDraggingCursor, onAnchorChange, columns, timeCursorTs, anchorTs, onTimeCursorClick, stopEdgeScroll]);

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

  return (
    <div className="flex-1 flex flex-col overflow-hidden bg-background">
      {/* Header Row - scrolls with content */}
      <div className="flex border-b bg-muted/50 overflow-hidden">
        <div className="w-40 flex-shrink-0 p-2 border-r font-semibold text-sm">
          Space
        </div>
        <div
          ref={headerScrollRef}
          className="flex-1 overflow-x-auto overflow-y-hidden scrollbar-hide"
          onScroll={handleHeaderScroll}
        >
          <div className="flex">
            {columns.map((col, idx) => (
              <div
                key={idx}
                className={`flex-1 min-w-[60px] p-2 border-r text-center text-xs font-medium cursor-pointer hover:bg-accent/50 ${col.isWeekend ? 'bg-destructive/10 text-destructive' : col.isOutsideWorkingHours ? 'bg-muted/80 text-muted-foreground' : ''}`}
                title={format(col.start, scale === "day" || scale === "hour" ? "EEEE, MMMM d, yyyy HH:mm" : "EEEE, MMMM d, yyyy")}
                onClick={() => onTimeCursorClick(col.start)}
              >
                {col.label}
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* Rows */}
      <div
        ref={bodyScrollRef}
        className="flex-1 overflow-y-auto overflow-x-auto"
        onScroll={handleBodyScroll}
      >
        {sortedSpaces.length === 0 ? (
          <div className="flex items-center justify-center h-32 text-muted-foreground text-sm">
            No spaces available
          </div>
        ) : (
          <div className="relative">
            <SortableContext items={sortedSpaces.map(s => s.id)} strategy={verticalListSortingStrategy}>
              {groupedSpaces.map((group) => {
                const isCollapsed = collapsedGroupIds.has(group.groupId || "ungrouped");

                return (
                  <div key={group.groupId || 'ungrouped'}>
                    <GroupHeader
                      group={group}
                      isCollapsed={isCollapsed}
                      onToggle={() => {
                        const groupIdToToggle = group.groupId || "ungrouped";
                        toggleGroupCollapse(groupIdToToggle);
                      }}
                    />
                    {!isCollapsed && group.spaces.map((space) => (
                      <SpaceRow
                        key={space.id}
                        space={space}
                        columns={columns}
                        requests={requests}
                        previewSchedule={previewSchedule}
                        scheduleIndex={scheduleIndex}
                        validation={validation}
                        timeCursorTs={timeCursorTs}
                        onRequestClick={onRequestClick}
                        onRequestDoubleClick={onRequestDoubleClick}
                        onRequestResize={onRequestResize}
                        offTimeRanges={offTimeRanges}
                      />
                    ))}
                  </div>
                );
              })}
            </SortableContext>

            {/* Edge scroll indicators */}
            {isDraggingCursor && (
              <>
                {/* Left edge indicator */}
                <div
                  className={`absolute top-0 bottom-0 left-40 w-[60px] pointer-events-none transition-opacity duration-150 ${
                    edgeScrollDirection === 'left' ? 'opacity-100' : 'opacity-0'
                  }`}
                  style={{
                    background: 'linear-gradient(to right, rgba(59, 130, 246, 0.3), transparent)'
                  }}
                />
                {/* Right edge indicator */}
                <div
                  className={`absolute top-0 bottom-0 right-0 w-[60px] pointer-events-none transition-opacity duration-150 ${
                    edgeScrollDirection === 'right' ? 'opacity-100' : 'opacity-0'
                  }`}
                  style={{
                    background: 'linear-gradient(to left, rgba(59, 130, 246, 0.3), transparent)'
                  }}
                />
              </>
            )}

            {/* Draggable Time Cursor Line - positioned after space column */}
            <div
              ref={timeColumnsRef}
              className="absolute top-0 bottom-0 left-40 right-0 pointer-events-none"
            >
              <div
                className={`absolute top-0 bottom-0 w-0.5 z-20 transition-colors ${
                  edgeScrollDirection ? 'bg-blue-400 animate-pulse' : 'bg-blue-500'
                }`}
                style={{ left: `${cursorPosition}%` }}
              >
                {/* Drag handle */}
                <div
                  className={`absolute top-0 left-1/2 -translate-x-1/2 w-4 h-full cursor-ew-resize pointer-events-auto ${
                    isDraggingCursor ? 'w-6' : ''
                  }`}
                  onMouseDown={handleCursorMouseDown}
                />
                {/* Visual handle indicator at top */}
                <div
                  className={`absolute -top-1 left-1/2 -translate-x-1/2 w-3 h-3 rounded-full border-2 border-blue-500 pointer-events-none ${
                    isDraggingCursor ? 'bg-blue-500 scale-125' : 'bg-background'
                  } transition-all`}
                />
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
