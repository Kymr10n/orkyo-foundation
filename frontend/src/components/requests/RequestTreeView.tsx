import { Badge } from "@foundation/src/components/ui/badge";
import { ScrollArea } from "@foundation/src/components/ui/scroll-area";
import { RequestRowActions } from "@foundation/src/components/requests/RequestRowActions";
import { RequestStatusBadge } from "@foundation/src/components/ui/RequestStatusBadge";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@foundation/src/components/ui/tooltip";
import { useCanEdit } from "@foundation/src/hooks/usePermissions";
import { useBreakpoint } from "@foundation/src/hooks/useBreakpoint";
import { getPlanningModeIcon, getRequestIcon } from "@foundation/src/constants";
import {
  buildDerivedMap,
  canHaveChildren,
  resolveDuration,
  resolveSchedule,
  type DerivedValues,
  type FlatTreeEntry,
} from "@foundation/src/domain/request-tree";
import { useRequestTreeStore } from "@foundation/src/store/request-tree-store";
import type { Request } from "@foundation/src/types/requests";
import {
  DndContext,
  DragOverlay,
  PointerSensor,
  useSensor,
  useSensors,
  useDraggable,
  useDroppable,
  type DragEndEvent,
  type DragStartEvent,
} from "@dnd-kit/core";
import { AlertTriangle, ChevronRight, GripVertical } from "lucide-react";
import React, { useCallback, useMemo, useRef, useState } from "react";
import { useVirtualizer } from "@tanstack/react-virtual";

// Column widths mirror RequestListView so toggling views feels seamless.
// Name flexes; Schedule/Duration/Status/Actions are fixed. Schedule hides
// below sm, Duration below lg (matches the old inline-metadata treatment).
const COL_SCHEDULE = "w-[200px] shrink-0 hidden sm:block";
const COL_DURATION = "w-[110px] shrink-0 hidden lg:block";
const COL_STATUS = "w-[100px] shrink-0";
const COL_ACTIONS = "w-[60px] shrink-0";

// ---------------------------------------------------------------------------
// Tree row
// ---------------------------------------------------------------------------

const TreeRow = React.memo(function TreeRow({
  entry,
  childCount,
  derived,
  conflictCount,
  onOpenConflicts,
  onToggle,
  onEdit,
  onDelete,
  onSelect,
  isSelected,
  isDragOverlay,
  isDropTarget,
}: {
  entry: FlatTreeEntry;
  childCount: number;
  derived: DerivedValues | null;
  conflictCount: number;
  onOpenConflicts: (requestId: string) => void;
  onToggle: (id: string) => void;
  onEdit: (request: Request) => void;
  onDelete: (request: Request) => void;
  onSelect: (id: string) => void;
  isSelected: boolean;
  isDragOverlay?: boolean;
  isDropTarget?: boolean;
}) {
  const { request, depth, hasChildren } = entry;
  const Icon = getRequestIcon(request.icon) ?? getPlanningModeIcon(request.planningMode);
  const isExpanded = useRequestTreeStore((s) => s.expandedIds.has(request.id));
  const isParent = canHaveChildren(request.planningMode);
  const canEdit = useCanEdit();
  const { isDesktop } = useBreakpoint();

  // Touch surfaces have no hover, so row affordances must be persistently visible
  // there; desktop keeps the reveal-on-hover treatment. Drag-to-reparent stays
  // desktop-only; touch users reparent via the request's Edit dialog → Children tab.
  const hoverReveal = isDesktop ? "opacity-0 group-hover:opacity-100 transition-opacity" : "";

  const { attributes, listeners, setNodeRef: setDragRef, isDragging } = useDraggable({
    id: request.id,
    data: { request },
    disabled: isDragOverlay || !canEdit,
  });

  const { setNodeRef: setDropRef, isOver } = useDroppable({
    id: `drop-${request.id}`,
    data: { request },
    disabled: isDragOverlay,
  });

  // Merge drag + drop refs
  const mergedRef = useCallback(
    (node: HTMLDivElement | null) => {
      setDragRef(node);
      setDropRef(node);
    },
    [setDragRef, setDropRef],
  );

  // Schedule/duration summary — derived (rolled up from children) for
  // parents, else the request's own values. `derived` is only ever non-null
  // for parent rows (derivedMap is keyed by canHaveChildren), so `isParent`
  // is implied by a non-null `derived`.
  const { text: schedule, isDerived: scheduleIsDerived } = resolveSchedule(request, derived);
  const { text: duration, isDerived: durationIsDerived } = resolveDuration(request, derived);

  return (
    <div
      ref={mergedRef}
      id={isDragOverlay ? undefined : `tree-item-${request.id}`}
      role="treeitem"
      aria-expanded={hasChildren ? isExpanded : undefined}
      aria-level={depth + 1}
      aria-selected={isSelected}
      className={`
        group flex items-center gap-2 h-12 px-4 cursor-pointer border-b
        hover:bg-accent/40 transition-colors
        ${isSelected ? "bg-muted" : ""}
        ${isDragging ? "opacity-40" : ""}
        ${isDropTarget && isOver ? "ring-2 ring-primary ring-inset bg-primary/10" : ""}
      `}
      onClick={() => {
        onSelect(request.id);
        onEdit(request);
      }}
    >
      {/* Name */}
      <div
        className="flex items-center gap-1.5 flex-1 min-w-0"
        style={{ paddingLeft: `${depth * 20}px` }}
      >
        {/* Drag handle — desktop only; touch reparents via the Edit dialog → Children tab. */}
        <span
          className={`flex-shrink-0 cursor-grab ${isDesktop ? "opacity-0 group-hover:opacity-50 hover:!opacity-100 transition-opacity" : "hidden"}`}
          {...listeners}
          {...attributes}
        >
          <GripVertical className="h-3.5 w-3.5 text-muted-foreground" />
        </span>

        {/* Expand / collapse */}
        {hasChildren ? (
          <button
            tabIndex={-1}
            onClick={(e) => {
              e.stopPropagation();
              onToggle(request.id);
            }}
            className="p-0.5 rounded hover:bg-muted-foreground/10 flex-shrink-0"
          >
            <ChevronRight
              className={`h-3.5 w-3.5 text-muted-foreground transition-transform duration-150 ${
                isExpanded ? "rotate-90" : ""
              }`}
            />
          </button>
        ) : (
          <span className="w-[18px] flex-shrink-0" />
        )}

        {/* Planning-mode / curated icon */}
        <Icon className="h-4 w-4 text-muted-foreground flex-shrink-0" />

        {/* Name */}
        <span className="font-medium text-sm truncate min-w-0">{request.name}</span>

        {/* Conflict indicator */}
        {conflictCount > 0 && (
          <Tooltip>
            <TooltipTrigger asChild>
              <button
                type="button"
                className="flex-shrink-0 rounded-sm focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-ring"
                onClick={(e) => {
                  e.stopPropagation();
                  onOpenConflicts(request.id);
                }}
                aria-label={`Open ${conflictCount} conflict${conflictCount !== 1 ? "s" : ""} for ${request.name}`}
              >
                <Badge
                  variant="destructive"
                  className="inline-flex items-center gap-0.5 text-[10px] px-1.5 py-0 h-5 cursor-pointer"
                >
                  <AlertTriangle className="h-3 w-3" />
                  {conflictCount}
                </Badge>
              </button>
            </TooltipTrigger>
            <TooltipContent side="top">
              {conflictCount} conflict{conflictCount !== 1 ? "s" : ""} - click to open
            </TooltipContent>
          </Tooltip>
        )}

        {/* Child count */}
        {isParent && childCount > 0 && (
          <Tooltip>
            <TooltipTrigger asChild>
              <Badge
                variant="secondary"
                className="flex-shrink-0 text-[10px] px-1.5 py-0 h-5"
                aria-label={`${childCount} direct child${childCount !== 1 ? "ren" : ""}`}
              >
                {childCount}
              </Badge>
            </TooltipTrigger>
            <TooltipContent side="top">
              {childCount} direct child{childCount !== 1 ? "ren" : ""}
            </TooltipContent>
          </Tooltip>
        )}
      </div>

      {/* Schedule */}
      <div className={`${COL_SCHEDULE} text-xs text-muted-foreground truncate`}>
        {schedule &&
          (scheduleIsDerived ? (
            <Tooltip>
              <TooltipTrigger asChild>
                <span className="italic">{schedule}</span>
              </TooltipTrigger>
              <TooltipContent side="top">Derived from children</TooltipContent>
            </Tooltip>
          ) : (
            <span>{schedule}</span>
          ))}
      </div>

      {/* Duration */}
      <div className={`${COL_DURATION} text-xs text-muted-foreground truncate`}>
        <Tooltip>
          <TooltipTrigger asChild>
            <span className={durationIsDerived ? "italic" : ""}>{duration}</span>
          </TooltipTrigger>
          <TooltipContent side="top">
            {durationIsDerived ? "Sum of children" : "Minimal duration"}
          </TooltipContent>
        </Tooltip>
      </div>

      {/* Status */}
      <div className={COL_STATUS}>
        <RequestStatusBadge status={request.status} />
      </div>

      {/* Actions — Edit/Delete, hover-revealed on desktop / persistent on touch. */}
      <div className={`${COL_ACTIONS} flex justify-end`}>
        <span className={hoverReveal}>
          <RequestRowActions
            request={request}
            canEdit={canEdit}
            onEdit={onEdit}
            onDelete={onDelete}
          />
        </span>
      </div>
    </div>
  );
});

// ---------------------------------------------------------------------------
// RequestTreeView
// ---------------------------------------------------------------------------

interface RequestTreeViewProps {
  entries: FlatTreeEntry[];
  allRequests: Request[];
  selectedId: string | null;
  conflictMap?: Map<string, number>;
  onOpenConflicts?: (requestId: string) => void;
  onToggle: (id: string) => void;
  onSelect: (id: string) => void;
  onEdit: (request: Request) => void;
  onDelete: (request: Request) => void;
  onDrop: (draggedId: string, targetId: string) => void;
}

export const RequestTreeView = React.memo(function RequestTreeView({
  entries,
  allRequests,
  selectedId,
  conflictMap,
  onOpenConflicts,
  onToggle,
  onSelect,
  onEdit,
  onDelete,
  onDrop,
}: RequestTreeViewProps) {
  const parentRef = useRef<HTMLDivElement>(null);
  const [activeId, setActiveId] = useState<string | null>(null);
  const expandedIds = useRequestTreeStore((s) => s.expandedIds);
  const expandAll = useRequestTreeStore((s) => s.expandAll);
  const canEdit = useCanEdit();

  // Expandable (parent) ids — feeds the `*` expand-all shortcut. The page owns
  // the toolbar Expand/Collapse-all buttons and computes this list identically.
  const expandableIds = useMemo(
    () => allRequests.filter((r) => canHaveChildren(r.planningMode)).map((r) => r.id),
    [allRequests],
  );

  const handleExpandAll = useCallback(() => {
    expandAll(expandableIds);
  }, [expandAll, expandableIds]);

  // Require 8px drag distance before activating — avoids conflicts with clicks
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 8 } }),
  );

  // Build set of valid drop target IDs: items that canHaveChildren, excluding
  // the dragged item itself and its descendants.
  const validDropTargets = useMemo(() => {
    if (!activeId) return new Set<string>();
    const draggedDescendants = new Set<string>();
    // Walk the tree to find all descendants of the dragged item
    const collectDescendants = (parentId: string) => {
      for (const r of allRequests) {
        if (r.parentRequestId === parentId && !draggedDescendants.has(r.id)) {
          draggedDescendants.add(r.id);
          collectDescendants(r.id);
        }
      }
    };
    collectDescendants(activeId);

    const valid = new Set<string>();
    for (const r of allRequests) {
      if (
        r.id !== activeId &&
        !draggedDescendants.has(r.id) &&
        canHaveChildren(r.planningMode)
      ) {
        valid.add(r.id);
      }
    }
    return valid;
  }, [activeId, allRequests]);

  const handleDragStart = useCallback((event: DragStartEvent) => {
    setActiveId(event.active.id as string);
  }, []);

  const handleDragEnd = useCallback(
    (event: DragEndEvent) => {
      setActiveId(null);
      const { active, over } = event;
      if (!over) return;
      // Drop target IDs are prefixed with "drop-"
      const targetId = (over.id as string).replace(/^drop-/, "");
      const draggedId = active.id as string;
      if (draggedId === targetId) return;
      if (!validDropTargets.has(targetId)) return;
      onDrop(draggedId, targetId);
    },
    [onDrop, validDropTargets],
  );

  const handleDragCancel = useCallback(() => {
    setActiveId(null);
  }, []);

  const childCountMap = useMemo(() => {
    const map = new Map<string, number>();
    for (const r of allRequests) {
      if (r.parentRequestId) {
        map.set(r.parentRequestId, (map.get(r.parentRequestId) ?? 0) + 1);
      }
    }
    return map;
  }, [allRequests]);

  const derivedMap = useMemo(() => buildDerivedMap(allRequests), [allRequests]);

  const virtualizer = useVirtualizer({
    count: entries.length,
    getScrollElement: () => parentRef.current,
    estimateSize: () => 48,
    overscan: 15,
  });

  // Entry being dragged — for the overlay
  const activeEntry = useMemo(
    () => (activeId ? entries.find((e) => e.request.id === activeId) ?? null : null),
    [activeId, entries],
  );

  // Keyboard navigation
  const focusedIndexRef = useRef(0);

  const handleOpenConflicts = useCallback(
    (requestId: string) => {
      onOpenConflicts?.(requestId);
    },
    [onOpenConflicts],
  );

  const handleTreeKeyDown = useCallback(
    (e: React.KeyboardEvent<HTMLDivElement>) => {
      if (entries.length === 0) return;

      const currentIndex = focusedIndexRef.current;

      switch (e.key) {
        case "ArrowDown": {
          e.preventDefault();
          const next = Math.min(currentIndex + 1, entries.length - 1);
          focusedIndexRef.current = next;
          onSelect(entries[next].request.id);
          virtualizer.scrollToIndex(next, { align: "auto" });
          break;
        }
        case "ArrowUp": {
          e.preventDefault();
          const prev = Math.max(currentIndex - 1, 0);
          focusedIndexRef.current = prev;
          onSelect(entries[prev].request.id);
          virtualizer.scrollToIndex(prev, { align: "auto" });
          break;
        }
        case "ArrowRight": {
          e.preventDefault();
          const entry = entries[currentIndex];
          if (entry && entry.hasChildren && !expandedIds.has(entry.request.id)) {
            onToggle(entry.request.id);
          }
          break;
        }
        case "ArrowLeft": {
          e.preventDefault();
          const entry = entries[currentIndex];
          if (entry && expandedIds.has(entry.request.id)) {
            onToggle(entry.request.id);
          } else if (entry?.request.parentRequestId) {
            // Navigate to parent
            const parentIdx = entries.findIndex(
              (en) => en.request.id === entry.request.parentRequestId,
            );
            if (parentIdx >= 0) {
              focusedIndexRef.current = parentIdx;
              onSelect(entries[parentIdx].request.id);
              virtualizer.scrollToIndex(parentIdx, { align: "auto" });
            }
          }
          break;
        }
        case "*": {
          e.preventDefault();
          handleExpandAll();
          break;
        }
        case "+": {
          e.preventDefault();
          const entry = entries[currentIndex];
          if (entry && entry.hasChildren && !expandedIds.has(entry.request.id)) {
            onToggle(entry.request.id);
          }
          break;
        }
        case "-": {
          e.preventDefault();
          const entry = entries[currentIndex];
          if (entry && entry.hasChildren && expandedIds.has(entry.request.id)) {
            onToggle(entry.request.id);
          }
          break;
        }
        case "Enter": {
          e.preventDefault();
          const entry = entries[currentIndex];
          if (entry) onEdit(entry.request);
          break;
        }
        case "Delete": {
          e.preventDefault();
          if (!canEdit) break;
          const entry = entries[currentIndex];
          if (entry) onDelete(entry.request);
          break;
        }
        case "Home": {
          e.preventDefault();
          focusedIndexRef.current = 0;
          onSelect(entries[0].request.id);
          virtualizer.scrollToIndex(0, { align: "auto" });
          break;
        }
        case "End": {
          e.preventDefault();
          const last = entries.length - 1;
          focusedIndexRef.current = last;
          onSelect(entries[last].request.id);
          virtualizer.scrollToIndex(last, { align: "auto" });
          break;
        }
      }
    },
    [
      entries,
      onSelect,
      onToggle,
      onEdit,
      onDelete,
      virtualizer,
      expandedIds,
      handleExpandAll,
      canEdit,
    ],
  );

  // Sync focused index when selectedId changes externally
  const selectedIndex = useMemo(
    () => entries.findIndex((e) => e.request.id === selectedId),
    [entries, selectedId],
  );
  if (selectedIndex >= 0) focusedIndexRef.current = selectedIndex;

  return (
    <TooltipProvider delayDuration={300}>
    <DndContext
      sensors={sensors}
      onDragStart={handleDragStart}
      onDragEnd={handleDragEnd}
      onDragCancel={handleDragCancel}
    >
    <div className="h-full flex flex-col rounded-2xl border bg-card text-card-foreground shadow-xs overflow-hidden">
      {/* Header row — mirrors the house table header, pinned above the scroll area. */}
      <div className="flex items-center gap-2 h-10 px-4 shrink-0 border-b text-sm font-medium text-muted-foreground">
        <span className="flex-1 min-w-0">Name</span>
        <span className={COL_SCHEDULE}>Schedule</span>
        <span className={COL_DURATION}>Duration</span>
        <span className={COL_STATUS}>Status</span>
        <span className={COL_ACTIONS} aria-hidden />
      </div>
      {/* The virtualizer scrolls the ScrollArea Viewport (wired via `viewportRef`),
          not the Root — so this uses the shared house scrollbar instead of a bare
          overflow div. `type="auto"` keeps the bar visible whenever the tree
          overflows (native-like) rather than only on hover. */}
      <ScrollArea type="auto" viewportRef={parentRef} className="flex-1 min-h-0">
        <div
          role="tree"
          tabIndex={0}
          aria-activedescendant={selectedId ? `tree-item-${selectedId}` : undefined}
          onKeyDown={handleTreeKeyDown}
          style={{
            height: `${virtualizer.getTotalSize()}px`,
            width: "100%",
            position: "relative",
            outline: "none",
          }}
        >
          {virtualizer.getVirtualItems().map((virtualRow) => {
            const entry = entries[virtualRow.index];
            return (
              <div
                key={entry.request.id}
                style={{
                  position: "absolute",
                  top: 0,
                  left: 0,
                  width: "100%",
                  height: `${virtualRow.size}px`,
                  transform: `translateY(${virtualRow.start}px)`,
                }}
              >
                <TreeRow
                  entry={entry}
                  childCount={childCountMap.get(entry.request.id) ?? 0}
                  derived={derivedMap.get(entry.request.id) ?? null}
                  conflictCount={conflictMap?.get(entry.request.id) ?? 0}
                  onOpenConflicts={handleOpenConflicts}
                  onToggle={onToggle}
                  onEdit={onEdit}
                  onDelete={onDelete}
                  onSelect={onSelect}
                  isSelected={selectedId === entry.request.id}
                  isDropTarget={validDropTargets.has(entry.request.id)}
                />
              </div>
            );
          })}
        </div>
      </ScrollArea>
    </div>
    <DragOverlay dropAnimation={null}>
      {activeEntry && (
        <div className="rounded-md border bg-card shadow-lg opacity-90 w-[400px]">
          <TreeRow
            entry={activeEntry}
            childCount={childCountMap.get(activeEntry.request.id) ?? 0}
            derived={derivedMap.get(activeEntry.request.id) ?? null}
            conflictCount={conflictMap?.get(activeEntry.request.id) ?? 0}
            onOpenConflicts={handleOpenConflicts}
            onToggle={onToggle}
            onEdit={onEdit}
            onDelete={onDelete}
            onSelect={onSelect}
            isSelected={false}
            isDragOverlay
          />
        </div>
      )}
    </DragOverlay>
    </DndContext>
    </TooltipProvider>
  );
});
