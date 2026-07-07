import { Input } from "@foundation/src/components/ui/input";
import { EmptyState } from "@foundation/src/components/ui/EmptyState";
import { RequestStatusBadge } from "@foundation/src/components/ui/RequestStatusBadge";
import { LoadingSpinner } from "@foundation/src/components/ui/LoadingSpinner";
import { ScrollArea } from "@foundation/src/components/ui/scroll-area";
import {
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
} from "@foundation/src/components/ui/select";
import type { Request, RequestStatus } from "@foundation/src/types/requests";
import { buildRequestTree, flattenTree, canBeScheduled, canHaveChildren } from "@foundation/src/domain/request-tree";
import { useDraggable, useDroppable } from "@dnd-kit/core";
import { ChevronRight, ChevronDown, GripVertical, Plus, Search } from "lucide-react";
import { getPlanningModeIcon, REQUEST_STATUS_ORDER, PLANNING_MODE } from "@foundation/src/constants";
import { filterPanelEntries } from "./request-panel-filter";
import React, { useState, useMemo, useCallback, useRef } from "react";
import { useVirtualizer } from "@tanstack/react-virtual";
import { formatMinutesHuman, formatStatusLabel } from "@foundation/src/lib/utils/utils";

interface RequestsPanelProps {
  requests: Request[];
  isLoading?: boolean;
  onCreateChild?: (parentId: string) => void;
  onRequestClick?: (request: Request) => void;
}

const INDENT_PX = 20;

interface RequestCardProps {
  request: Request;
  depth: number;
  hasChildren: boolean;
  isExpanded: boolean;
  onToggle: (requestId: string) => void;
  onCreateChild?: (requestId: string) => void;
  onCardClick?: (request: Request) => void;
  requestId: string;
}

const RequestCard = React.memo(function RequestCard({
  request,
  depth,
  hasChildren,
  isExpanded,
  onToggle,
  onCreateChild,
  onCardClick,
  requestId,
}: RequestCardProps) {
  const isDraggable = canBeScheduled(request.planningMode);
  const isDropTarget = canHaveChildren(request.planningMode);

  const { attributes, listeners, setNodeRef: setDragRef, isDragging } =
    useDraggable({
      id: request.id,
      data: request,
      disabled: !isDraggable,
    });

  // Note: deliberately NOT reading useDndContext() here. A droppable is only a
  // collision candidate while a drag is active, so gating on `active === null`
  // bought nothing — but it forced every visible card to re-render on every
  // pointer move (useDndContext subscribes to the whole drag state).
  const { isOver, setNodeRef: setDropTargetRef } = useDroppable({
    id: `tree-drop-${request.id}`,
    data: { type: "tree-reparent", parentRequestId: request.id },
    disabled: !isDropTarget,
  });

  const combinedRef = useCallback((el: HTMLDivElement | null) => {
    setDragRef(el);
    setDropTargetRef(el);
  }, [setDragRef, setDropTargetRef]);

  const style = {
    // The drag visual is the lightweight <DragOverlay> clone in UtilizationPage.
    // Deliberately do NOT translate the source card: it lives in an absolutely-
    // positioned virtualizer slot (see the translateY wrapper below), so applying
    // the drag delta here would slide the original out of its slot and over its
    // neighbours while the clone also moves — the "items jump around the list"
    // effect. Keep it in place and just dim it while dragging.
    opacity: isDragging ? 0.5 : 1,
    paddingLeft: `${depth * INDENT_PX + 12}px`,
  };

  const isScheduled = !!request.isScheduled;
  const ModeIcon = getPlanningModeIcon(request.planningMode);

  return (
    <div
      ref={combinedRef}
      style={style}
      onClick={onCardClick ? () => onCardClick(request) : undefined}
      className={`py-2 pr-3 border rounded-lg bg-card hover:bg-accent/50 transition-colors ${
        isDraggable ? "cursor-grab active:cursor-grabbing" : onCardClick ? "cursor-pointer" : ""
      } ${isOver ? "ring-2 ring-primary/50 bg-primary/5" : ""}`}
      {...attributes}
      {...(isDraggable ? listeners : {})}
    >
      <div className="flex items-start gap-1.5">
        {/* Expand/collapse toggle for parent nodes */}
        {hasChildren ? (
          <button
            type="button"
            onClick={(e) => {
              e.stopPropagation();
              onToggle(requestId);
            }}
            className="mt-0.5 p-0.5 rounded hover:bg-accent flex-shrink-0"
          >
            {isExpanded ? (
              <ChevronDown className="h-3.5 w-3.5 text-muted-foreground" />
            ) : (
              <ChevronRight className="h-3.5 w-3.5 text-muted-foreground" />
            )}
          </button>
        ) : (
          <span className="w-[18px] flex-shrink-0" />
        )}

        {isDraggable && (
          <GripVertical className="h-4 w-4 text-muted-foreground mt-0.5 flex-shrink-0" />
        )}

        <ModeIcon className="h-3.5 w-3.5 text-muted-foreground mt-0.5 flex-shrink-0" />

        <div className="flex-1 min-w-0">
          <div className="flex items-start justify-between gap-2">
            <h4 className="font-medium text-sm truncate">{request.name}</h4>
            <div className="flex items-center gap-1 flex-shrink-0">
              {isDropTarget && onCreateChild && (
                <button
                  type="button"
                  onClick={(e) => {
                    e.stopPropagation();
                    onCreateChild(requestId);
                  }}
                  className="p-0.5 rounded hover:bg-accent text-muted-foreground hover:text-foreground"
                  title="Add child request"
                >
                  <Plus className="h-3.5 w-3.5" />
                </button>
              )}
              <RequestStatusBadge status={request.status} className="text-xs flex-shrink-0" />
            </div>
          </div>
          {request.description && (
            <p className="text-xs text-muted-foreground mt-1 line-clamp-2">
              {request.description}
            </p>
          )}
          <div className="flex items-center gap-2 mt-1 text-xs text-muted-foreground">
            {isScheduled ? (
              <span className="text-green-600">Scheduled</span>
            ) : request.planningMode === PLANNING_MODE.CONTAINER ? (
              <span className="text-muted-foreground/60">Container</span>
            ) : (
              <span>Unscheduled</span>
            )}
            {request.durationMin && (
              <span>• {Math.round(request.durationMin / 60)}h</span>
            )}
            {request.actualDurationValue != null && request.actualDurationValue > 0 && (
              <span>• Gross: {formatMinutesHuman(request.actualDurationValue)}</span>
            )}
          </div>
        </div>
      </div>
    </div>
  );
});

export function RequestsPanel({ requests, isLoading, onCreateChild, onRequestClick }: RequestsPanelProps) {
  const [searchQuery, setSearchQuery] = useState("");
  const [statusFilter, setStatusFilter] = useState<RequestStatus | "all">(
    "all"
  );
  const [scheduledFilter, setScheduledFilter] = useState<
    "all" | "scheduled" | "unscheduled"
  >("unscheduled"); // Default to showing only unscheduled
  const [collapsedIds, setCollapsedIds] = useState<Set<string>>(new Set());

  const toggleCollapse = useCallback((requestId: string) => {
    setCollapsedIds((prev) => {
      const next = new Set(prev);
      if (next.has(requestId)) {
        next.delete(requestId);
      } else {
        next.add(requestId);
      }
      return next;
    });
  }, []);

  // Wrap onCreateChild to auto-expand the parent node
  const handleCreateChild = useCallback((parentId: string) => {
    setCollapsedIds((prev) => {
      if (!prev.has(parentId)) return prev;
      const next = new Set(prev);
      next.delete(parentId);
      return next;
    });
    onCreateChild?.(parentId);
  }, [onCreateChild]);

  const listScrollRef = useRef<HTMLDivElement>(null);

  // Make the panel droppable for unscheduling
  const { isOver, setNodeRef: setDropRef } = useDroppable({
    id: "unschedule-zone",
    data: { type: "unschedule" },
  });

  // Build tree from all requests, then filter the flattened entries
  const flatEntries = useMemo(() => {
    const tree = buildRequestTree(requests);
    return flattenTree(tree);
  }, [requests]);

  // Filter to the entries the panel should show. A search or specific status filter reveals matches
  // regardless of tree collapse (so an in_progress job nested under a collapsed summary still shows).
  const visibleEntries = useMemo(
    () => filterPanelEntries(flatEntries, { searchQuery, statusFilter, scheduledFilter, collapsedIds }),
    [flatEntries, searchQuery, statusFilter, scheduledFilter, collapsedIds],
  );

  const virtualizer = useVirtualizer({
    count: visibleEntries.length,
    getScrollElement: () => listScrollRef.current,
    estimateSize: () => 72,
    getItemKey: (i) => visibleEntries[i]?.request.id ?? String(i),
    overscan: 5,
  });

  return (
    <div
      ref={setDropRef}
      className={`w-[360px] border-r bg-card flex flex-col ${
        isOver ? "bg-accent/20" : ""
      }`}
    >
      {/* Header */}
      <div className="p-4 border-b">
        <h2 className="text-lg font-semibold mb-3">Requests</h2>
        {isOver && (
          <div className="text-xs text-blue-600 dark:text-blue-400 mb-2">
            Drop here to unschedule
          </div>
        )}

        {/* Search */}
        <div className="relative mb-3">
          <Search className="absolute left-2 top-2.5 h-4 w-4 text-muted-foreground" />
          <Input
            placeholder="Search requests..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            className="pl-8"
          />
        </div>

        {/* Filters */}
        <div className="space-y-2">
          <Select
            value={statusFilter}
            onValueChange={(v) => setStatusFilter(v as RequestStatus | "all")}
          >
            <SelectTrigger className="w-full">
              <SelectValue placeholder="Filter by status" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All Statuses</SelectItem>
              {REQUEST_STATUS_ORDER.map((s) => (
                <SelectItem key={s} value={s}>
                  {formatStatusLabel(s)}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>

          <Select
            value={scheduledFilter}
            onValueChange={(v) =>
              setScheduledFilter(v as "all" | "scheduled" | "unscheduled")
            }
          >
            <SelectTrigger className="w-full">
              <SelectValue placeholder="Filter by schedule" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All Requests</SelectItem>
              <SelectItem value="unscheduled">Unscheduled Only</SelectItem>
              <SelectItem value="scheduled">Scheduled Only</SelectItem>
            </SelectContent>
          </Select>
        </div>
      </div>

      {/* Request List — house scrollbar; the virtualizer scrolls the ScrollArea Viewport. */}
      <ScrollArea type="auto" viewportRef={listScrollRef} className="flex-1 min-h-0">
        <div className="px-4 py-2">
        {isLoading ? (
          <LoadingSpinner fullScreen={false} message="Loading requests…" />
        ) : visibleEntries.length === 0 ? (
          <EmptyState message="No requests found" className="text-sm" />
        ) : (
          <div style={{ height: `${virtualizer.getTotalSize()}px`, position: 'relative' }}>
            {virtualizer.getVirtualItems().map((vItem) => {
              const entry = visibleEntries[vItem.index];
              if (!entry) return null;
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
                    paddingBottom: '6px',
                  }}
                >
                  <RequestCard
                    request={entry.request}
                    requestId={entry.request.id}
                    depth={entry.depth}
                    hasChildren={entry.hasChildren}
                    isExpanded={!collapsedIds.has(entry.request.id)}
                    onToggle={toggleCollapse}
                    onCreateChild={onCreateChild ? handleCreateChild : undefined}
                    onCardClick={onRequestClick}
                  />
                </div>
              );
            })}
          </div>
        )}
        </div>
      </ScrollArea>
    </div>
  );
}
