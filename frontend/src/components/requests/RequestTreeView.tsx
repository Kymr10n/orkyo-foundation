import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { ScrollArea } from "@/components/ui/scroll-area";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import { getPlanningModeIcon, getPlanningModeLabel } from "@/constants";
import {
  canHaveChildren,
  computeDerivedValuesFromChildren,
  type DerivedValues,
  type FlatTreeEntry,
} from "@/domain/request-tree";
import {
  formatDateDisplay,
  formatDuration,
  formatStatusLabel,
  getStatusDotColor,
} from "@/lib/utils/utils";
import { useRequestTreeStore } from "@/store/request-tree-store";
import type { Request } from "@/types/requests";
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
import {
  ChevronsDown,
  ChevronsUp,
  ChevronRight,
  Edit,
  FolderInput,
  GripVertical,
  Link,
  MoreHorizontal,
  Plus,
  Trash2,
} from "lucide-react";
import React, { useCallback, useMemo, useRef, useState } from "react";
import { useVirtualizer } from "@tanstack/react-virtual";

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
  onAddChild,
  onAddSibling,
  onAddExisting,
  onMoveTo,
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
  onAddChild: (request: Request) => void;
  onAddSibling: (request: Request) => void;
  onAddExisting: (request: Request) => void;
  onMoveTo: (request: Request) => void;
  onSelect: (id: string) => void;
  isSelected: boolean;
  isDragOverlay?: boolean;
  isDropTarget?: boolean;
}) {
  const { request, depth, hasChildren } = entry;
  const Icon = getPlanningModeIcon(request.planningMode);
  const isExpanded = useRequestTreeStore((s) => s.expandedIds.has(request.id));
  const isParent = canHaveChildren(request.planningMode);

  const { attributes, listeners, setNodeRef: setDragRef, isDragging } = useDraggable({
    id: request.id,
    data: { request },
    disabled: isDragOverlay,
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

  // Schedule summary
  let schedule: string | null = null;
  if (isParent && derived?.startTs && derived?.endTs) {
    schedule = `${formatDateDisplay(derived.startTs)} — ${formatDateDisplay(derived.endTs)}`;
  } else if (request.startTs && request.endTs) {
    schedule = `${formatDateDisplay(request.startTs)} — ${formatDateDisplay(request.endTs)}`;
  }

  // Duration summary
  const duration = isParent && derived
    ? formatDuration(derived.totalDurationValue, derived.totalDurationUnit)
    : formatDuration(request.minimalDurationValue, request.minimalDurationUnit);

  return (
    <div
      ref={mergedRef}
      role="treeitem"
      aria-expanded={hasChildren ? isExpanded : undefined}
      aria-level={depth + 1}
      aria-selected={isSelected}
      className={`
        group flex items-center gap-1.5 h-10 px-3 cursor-pointer
        hover:bg-muted/50 transition-colors
        ${isSelected ? "bg-muted" : ""}
        ${isDragging ? "opacity-40" : ""}
        ${isDropTarget && isOver ? "ring-2 ring-primary ring-inset bg-primary/10" : ""}
      `}
      style={{ paddingLeft: `${12 + depth * 20}px` }}
      onClick={() => onSelect(request.id)}
      onDoubleClick={() => onEdit(request)}
    >
      {/* Drag handle */}
      <span
        className="flex-shrink-0 cursor-grab opacity-0 group-hover:opacity-50 hover:!opacity-100 transition-opacity"
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

      {/* Icon */}
      <Icon className="h-4 w-4 text-muted-foreground flex-shrink-0" />

      {/* Name */}
      <span className="font-medium text-sm truncate min-w-0 flex-1">
        {request.name}
      </span>

      {/* Inline metadata (visible on hover or always for key info) */}
      <div className="flex items-center gap-2 flex-shrink-0 ml-auto">
        {/* Conflict indicator */}
        {conflictCount > 0 && (
          <Tooltip>
            <TooltipTrigger asChild>
              <button
                type="button"
                className="rounded-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                onClick={(e) => {
                  e.stopPropagation();
                  onOpenConflicts(request.id);
                }}
                aria-label={`Open ${conflictCount} conflict${conflictCount !== 1 ? "s" : ""} for ${request.name}`}
              >
                <Badge variant="destructive" className="text-[10px] px-1.5 py-0 h-5 cursor-pointer">
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
          <Badge variant="secondary" className="text-[10px] px-1.5 py-0 h-5">
            {childCount}
          </Badge>
        )}

        {/* Schedule hint */}
        {schedule && (
          <Tooltip>
            <TooltipTrigger asChild>
              <span className={`text-[11px] text-muted-foreground hidden sm:inline ${isParent ? "italic" : ""}`}>
                {schedule}
              </span>
            </TooltipTrigger>
            <TooltipContent side="top">
              {isParent ? "Derived from children" : "Scheduled"}
            </TooltipContent>
          </Tooltip>
        )}

        {/* Duration */}
        <span className={`text-[11px] text-muted-foreground hidden lg:inline ${isParent && derived ? "italic" : ""}`}>
          {duration}
        </span>

        {/* Mode badge */}
        <Badge variant="outline" className="text-[10px] px-1.5 py-0 h-5 font-normal">
          {getPlanningModeLabel(request.planningMode)}
        </Badge>

        {/* Status dot */}
        <Tooltip>
          <TooltipTrigger asChild>
            <span
              className={`h-2 w-2 rounded-full flex-shrink-0 ${getStatusDotColor(request.status)}`}
            />
          </TooltipTrigger>
          <TooltipContent side="top">{formatStatusLabel(request.status)}</TooltipContent>
        </Tooltip>

        {/* Inline add child dropdown for parents */}
        {isParent && (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button
                variant="ghost"
                size="sm"
                className="h-6 w-6 p-0 opacity-0 group-hover:opacity-100 transition-opacity"
                onClick={(e) => e.stopPropagation()}
                title="Add child"
              >
                <Plus className="h-3.5 w-3.5" />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuItem onClick={() => onAddChild(request)}>
                <Plus className="h-4 w-4 mr-2" />
                New child
              </DropdownMenuItem>
              <DropdownMenuItem onClick={() => onAddExisting(request)}>
                <Link className="h-4 w-4 mr-2" />
                Add existing requests…
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        )}

        {/* Actions dropdown */}
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button
              variant="ghost"
              size="sm"
              className="h-6 w-6 p-0 opacity-0 group-hover:opacity-100 transition-opacity"
              onClick={(e) => e.stopPropagation()}
            >
              <MoreHorizontal className="h-3.5 w-3.5" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuItem onClick={() => onEdit(request)}>
              <Edit className="h-4 w-4 mr-2" />
              Edit
            </DropdownMenuItem>
            {isParent && (
              <>
                <DropdownMenuItem onClick={() => onAddChild(request)}>
                  <Plus className="h-4 w-4 mr-2" />
                  Add new child
                </DropdownMenuItem>
                <DropdownMenuItem onClick={() => onAddExisting(request)}>
                  <Link className="h-4 w-4 mr-2" />
                  Add existing requests…
                </DropdownMenuItem>
              </>
            )}
            <DropdownMenuItem onClick={() => onAddSibling(request)}>
              <Plus className="h-4 w-4 mr-2" />
              Add sibling
            </DropdownMenuItem>
            <DropdownMenuItem onClick={() => onMoveTo(request)}>
              <FolderInput className="h-4 w-4 mr-2" />
              Move to…
            </DropdownMenuItem>
            <DropdownMenuSeparator />
            <DropdownMenuItem
              className="text-destructive focus:text-destructive"
              onClick={() => onDelete(request)}
            >
              <Trash2 className="h-4 w-4 mr-2" />
              Delete
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
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
  onAddChild: (request: Request) => void;
  onAddSibling: (request: Request) => void;
  onAddExisting: (request: Request) => void;
  onMoveTo: (request: Request) => void;
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
  onAddChild,
  onAddSibling,
  onAddExisting,
  onMoveTo,
  onDrop,
}: RequestTreeViewProps) {
  const parentRef = useRef<HTMLDivElement>(null);
  const [activeId, setActiveId] = useState<string | null>(null);
  const expandedIds = useRequestTreeStore((s) => s.expandedIds);
  const expandAll = useRequestTreeStore((s) => s.expandAll);
  const collapseAll = useRequestTreeStore((s) => s.collapseAll);

  const expandableIds = useMemo(
    () => allRequests.filter((r) => canHaveChildren(r.planningMode)).map((r) => r.id),
    [allRequests],
  );

  const allExpandableExpanded = useMemo(
    () => expandableIds.length > 0 && expandableIds.every((id) => expandedIds.has(id)),
    [expandableIds, expandedIds],
  );

  const handleExpandAll = useCallback(() => {
    expandAll(expandableIds);
  }, [expandAll, expandableIds]);

  const handleCollapseAll = useCallback(() => {
    collapseAll();
  }, [collapseAll]);

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

  const derivedMap = useMemo(() => {
    const childrenByParent = new Map<string, Request[]>();
    for (const r of allRequests) {
      if (r.parentRequestId) {
        const siblings = childrenByParent.get(r.parentRequestId);
        if (siblings) siblings.push(r);
        else childrenByParent.set(r.parentRequestId, [r]);
      }
    }
    const map = new Map<string, DerivedValues | null>();
    for (const r of allRequests) {
      if (canHaveChildren(r.planningMode)) {
        const children = childrenByParent.get(r.id);
        map.set(r.id, children?.length ? computeDerivedValuesFromChildren(children) : null);
      }
    }
    return map;
  }, [allRequests]);

  const virtualizer = useVirtualizer({
    count: entries.length,
    getScrollElement: () => parentRef.current,
    estimateSize: () => 40,
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
    ],
  );

  // Sync focused index when selectedId changes externally
  const selectedIndex = useMemo(
    () => entries.findIndex((e) => e.request.id === selectedId),
    [entries, selectedId],
  );
  if (selectedIndex >= 0) focusedIndexRef.current = selectedIndex;

  return (
    <TooltipProvider>
    <DndContext
      sensors={sensors}
      onDragStart={handleDragStart}
      onDragEnd={handleDragEnd}
      onDragCancel={handleDragCancel}
    >
    <div className="h-full flex flex-col">
      {entries.length > 0 && (
        <div className="px-3 py-2 border-b flex items-center justify-end gap-2">
          <span className="text-xs text-muted-foreground mr-auto">
            {expandedIds.size} expanded
          </span>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            className="h-7"
            onClick={handleExpandAll}
            disabled={expandableIds.length === 0 || allExpandableExpanded}
            title="Expand all (*)"
          >
            <ChevronsDown className="h-3.5 w-3.5 mr-1.5" />
            Expand all
          </Button>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            className="h-7"
            onClick={handleCollapseAll}
            disabled={expandedIds.size === 0}
            title="Collapse all (use - to collapse current node)"
          >
            <ChevronsUp className="h-3.5 w-3.5 mr-1.5" />
            Collapse all
          </Button>
        </div>
      )}
      <ScrollArea className="h-full" ref={parentRef}>
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
                id={`tree-item-${entry.request.id}`}
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
                  onAddChild={onAddChild}
                  onAddSibling={onAddSibling}
                  onAddExisting={onAddExisting}
                  onMoveTo={onMoveTo}
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
            onAddChild={onAddChild}
            onAddSibling={onAddSibling}
            onAddExisting={onAddExisting}
            onMoveTo={onMoveTo}
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
