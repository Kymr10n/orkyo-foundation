import { useRef, useMemo, type ReactNode } from "react";
import { formatLocalized, HOUR_CYCLE } from "@foundation/src/lib/formatters";
import { SortableContext, verticalListSortingStrategy } from "@dnd-kit/sortable";
import { useVirtualizer } from "@tanstack/react-virtual";
import { useAppStore } from "@foundation/src/store/app-store";
import { LoadingSpinner } from "@foundation/src/components/ui/LoadingSpinner";
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
  /** Show a loading indicator in the body instead of emptyMessage / rows. */
  isLoading?: boolean;
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
  isLoading = false,
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

  // Flat, collapse-aware item list for the virtualizer (group headers + rows).
  const flatItems = useMemo(() => {
    const items: ({ kind: 'header'; group: ShellGroup<R> } | { kind: 'row'; row: R })[] = [];
    for (const group of groups) {
      items.push({ kind: 'header', group });
      const collapseId = `${collapseIdPrefix}:${group.id}`;
      if (!collapsedGroupIds.includes(collapseId)) {
        for (const row of group.rows) {
          items.push({ kind: 'row', row });
        }
      }
    }
    return items;
  }, [groups, collapseIdPrefix, collapsedGroupIds]);

  // Per-column tint + tooltip title, precomputed once per column/scale change.
  // The title's date-fns format() was previously called for every column on
  // every render of the shell.
  const columnHeaders = useMemo(() => {
    const opts: Intl.DateTimeFormatOptions =
      scale === "day" || scale === "hour"
        ? { weekday: "long", month: "long", day: "numeric", year: "numeric", hour: "2-digit", minute: "2-digit", hourCycle: HOUR_CYCLE }
        : { weekday: "long", month: "long", day: "numeric", year: "numeric" };
    return columns.map((col) => ({
      tint:
        col.isWeekend || col.isGlobalOffTime
          ? "bg-destructive/10 text-destructive"
          : col.isOutsideWorkingHours
          ? "bg-muted/80"
          : "",
      title: formatLocalized(col.start, opts),
    }));
  }, [columns, scale]);

  const virtualizer = useVirtualizer({
    count: flatItems.length,
    getScrollElement: () => bodyScrollRef.current,
    estimateSize: (i) => (flatItems[i]?.kind === 'header' ? 36 : 60),
    getItemKey: (i) => {
      const item = flatItems[i];
      if (!item) return String(i);
      return item.kind === 'header' ? `h:${item.group.id}` : getRowId(item.row);
    },
    overscan: 5,
  });

  const vItems = virtualizer.getVirtualItems().map((vItem) => {
    const item = flatItems[vItem.index];
    if (!item) return null;
    return (
      <div
        key={vItem.key}
        data-index={vItem.index}
        ref={virtualizer.measureElement}
        style={{
          position: 'absolute',
          top: 0,
          width: '100%',
          transform: `translateY(${vItem.start}px)`,
        }}
      >
        {item.kind === 'header' ? (
          <GroupHeader
            groupName={item.group.name}
            groupColor={item.group.color}
            count={item.group.rows.length}
            isCollapsed={collapsedGroupIds.includes(`${collapseIdPrefix}:${item.group.id}`)}
            onToggle={() => toggleGroupCollapse(`${collapseIdPrefix}:${item.group.id}`)}
          />
        ) : (
          renderRow(item.row)
        )}
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
            {columns.map((col, i) => (
              <div
                key={col.start.getTime()}
                className={`flex-1 min-w-[60px] px-3 py-2 border-r text-center text-xs font-medium text-muted-foreground ${
                  onColumnHeaderClick ? "cursor-pointer hover:bg-accent/50" : ""
                } ${columnHeaders[i].tint}`}
                title={columnHeaders[i].title}
                onClick={onColumnHeaderClick ? () => onColumnHeaderClick(col) : undefined}
              >
                {col.label}
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* Body — vertical + horizontal scroller (synced to header). */}
      <div
        ref={bodyScrollRef}
        className="flex-1 overflow-y-auto overflow-x-auto"
        onScroll={handleBodyScroll}
      >
        {isLoading ? (
          <div className="h-32">
            <LoadingSpinner fullScreen={false} message="Loading…" />
          </div>
        ) : totalRows === 0 ? (
          <div className="flex items-center justify-center h-32 text-muted-foreground text-sm">
            {emptyMessage}
          </div>
        ) : (
          <div style={{ height: `${virtualizer.getTotalSize()}px`, position: 'relative' }}>
            {sortable ? (
              <SortableContext items={sortableIds} strategy={verticalListSortingStrategy}>
                {vItems}
              </SortableContext>
            ) : vItems}
            {bodyOverlay}
          </div>
        )}
      </div>
    </div>
  );
}
