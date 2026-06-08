import { useRef, type ReactNode } from "react";
import { format } from "date-fns";
import { SortableContext, verticalListSortingStrategy } from "@dnd-kit/sortable";
import { useAppStore } from "@foundation/src/store/app-store";
import { useShallow } from "zustand/react/shallow";
import type { TimeScale } from "./ScaleSelect";
import type { TimeColumn } from "./scheduler-types";
import { GroupHeader } from "./GroupHeader";

/**
 * Shared presentational shell for both utilization grids (Spaces + People).
 *
 * Owns all the chrome: the header (label column + horizontally-scrollable time
 * columns with weekend / off-day tints), header/body horizontal scroll-sync,
 * the vertical body scroller, group headers + collapse wiring, and the optional
 * row-reorder SortableContext. Rows themselves are produced by `renderRow`
 * (each grid returns its specialised row built on the shared `TimelineRow`).
 *
 * It is deliberately ignorant of requests vs. utilization buckets — the two
 * grids differ in data + bar interactions, which stay in their own components.
 */

export interface ShellGroup<R> {
  id: string;
  name: string;
  color?: string;
  rows: readonly R[];
}

interface TimelineGridShellProps<R> {
  /** Header text for the fixed left column ("Space" | "Person"). */
  labelHeader: string;
  columns: readonly TimeColumn[];
  scale: TimeScale;
  groups: readonly ShellGroup<R>[];
  /** Collapse-id namespace for the shared AppStore slice ("spaces" | "people"). */
  collapseIdPrefix: string;
  getRowId: (row: R) => string;
  renderRow: (row: R) => ReactNode;
  emptyMessage: string;
  /** Clicking a column header (Spaces: move the time cursor). */
  onColumnHeaderClick?: (col: TimeColumn) => void;
  /** Wrap rows in a dnd SortableContext for row reordering (Spaces). */
  sortable?: boolean;
  /** Absolutely-positioned decoration over the body (Spaces: time cursor). */
  bodyOverlay?: ReactNode;
  /** Strip rendered above the header (People: legend + search). */
  toolbar?: ReactNode;
  /** Outer container className override. Defaults to the Spaces grid styling. */
  className?: string;
  testId?: string;
}

export function TimelineGridShell<R>({
  labelHeader,
  columns,
  scale,
  groups,
  collapseIdPrefix,
  getRowId,
  renderRow,
  emptyMessage,
  onColumnHeaderClick,
  sortable = false,
  bodyOverlay,
  toolbar,
  className = "flex-1 flex flex-col overflow-hidden bg-background",
  testId,
}: TimelineGridShellProps<R>) {
  const { collapsedGroupIds, toggleGroupCollapse } = useAppStore(
    useShallow((s) => ({
      collapsedGroupIds: s.collapsedGroupIds,
      toggleGroupCollapse: s.toggleGroupCollapse,
    })),
  );

  const headerScrollRef = useRef<HTMLDivElement>(null);
  const bodyScrollRef = useRef<HTMLDivElement>(null);

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

  const totalRows = groups.reduce((s, g) => s + g.rows.length, 0);
  const sortableIds = sortable
    ? groups.flatMap((g) => g.rows.map(getRowId))
    : [];

  const renderGroups = () =>
    groups.map((group) => {
      const collapseId = `${collapseIdPrefix}:${group.id}`;
      const isCollapsed = collapsedGroupIds.includes(collapseId);
      return (
        <div key={group.id}>
          <GroupHeader
            groupName={group.name}
            groupColor={group.color}
            count={group.rows.length}
            isCollapsed={isCollapsed}
            onToggle={() => toggleGroupCollapse(collapseId)}
          />
          {!isCollapsed &&
            group.rows.map((row) => (
              <div key={getRowId(row)}>{renderRow(row)}</div>
            ))}
        </div>
      );
    });

  return (
    <div className={className} data-testid={testId}>
      {toolbar}

      {/* Header row — label column + horizontally-scrollable time columns. */}
      <div className="flex border-b bg-muted/50 overflow-hidden">
        <div className="w-52 flex-shrink-0 px-3 py-2 border-r text-xs font-medium text-muted-foreground">
          {labelHeader}
        </div>
        <div
          ref={headerScrollRef}
          className="flex-1 overflow-x-auto overflow-y-hidden scrollbar-hide"
          onScroll={handleHeaderScroll}
        >
          <div className="flex">
            {columns.map((col) => {
              const tint =
                col.isWeekend || col.isGlobalOffTime
                  ? "bg-destructive/10 text-destructive"
                  : col.isOutsideWorkingHours
                  ? "bg-muted/80"
                  : "";
              return (
                <div
                  key={col.start.getTime()}
                  className={`flex-1 min-w-[60px] px-3 py-2 border-r text-center text-xs font-medium text-muted-foreground ${
                    onColumnHeaderClick ? "cursor-pointer hover:bg-accent/50" : ""
                  } ${tint}`}
                  title={format(
                    col.start,
                    scale === "day" || scale === "hour"
                      ? "EEEE, MMMM d, yyyy HH:mm"
                      : "EEEE, MMMM d, yyyy",
                  )}
                  onClick={onColumnHeaderClick ? () => onColumnHeaderClick(col) : undefined}
                >
                  {col.label}
                </div>
              );
            })}
          </div>
        </div>
      </div>

      {/* Body — vertical + horizontal scroller (synced to header). */}
      <div
        ref={bodyScrollRef}
        className="flex-1 overflow-y-auto overflow-x-auto"
        onScroll={handleBodyScroll}
      >
        {totalRows === 0 ? (
          <div className="flex items-center justify-center h-32 text-muted-foreground text-sm">
            {emptyMessage}
          </div>
        ) : (
          <div className="relative">
            {sortable ? (
              <SortableContext items={sortableIds} strategy={verticalListSortingStrategy}>
                {renderGroups()}
              </SortableContext>
            ) : (
              renderGroups()
            )}
            {bodyOverlay}
          </div>
        )}
      </div>
    </div>
  );
}
