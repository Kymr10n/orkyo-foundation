import { Badge } from "@foundation/src/components/ui/badge";
import { Input } from "@foundation/src/components/ui/input";
import {
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
} from "@foundation/src/components/ui/select";
import type { Request, RequestStatus } from "@foundation/src/types/requests";
import { buildRequestTree, flattenTree, canBeScheduled, canHaveChildren } from "@foundation/src/domain/request-tree";
import type { FlatTreeEntry } from "@foundation/src/domain/request-tree";
import { useDraggable, useDroppable } from "@dnd-kit/core";
import { CSS } from "@dnd-kit/utilities";
import { ChevronRight, ChevronDown, GripVertical, Plus, Search } from "lucide-react";
import { getPlanningModeIcon } from "@foundation/src/constants";
import React, { useState, useMemo, useCallback } from "react";
import { formatMinutesHuman } from "@foundation/src/lib/utils/utils";

interface RequestsPanelProps {
  requests: Request[];
  isLoading?: boolean;
  onCreateChild?: (parentId: string) => void;
}

const INDENT_PX = 20;

interface RequestCardProps {
  request: Request;
  depth: number;
  hasChildren: boolean;
  isExpanded: boolean;
  onToggle: (requestId: string) => void;
  onCreateChild?: (requestId: string) => void;
  requestId: string;
}

const RequestCard = React.memo(function RequestCard({
  request,
  depth,
  hasChildren,
  isExpanded,
  onToggle,
  onCreateChild,
  requestId,
}: RequestCardProps) {
  const isDraggable = canBeScheduled(request.planningMode);
  const isDropTarget = canHaveChildren(request.planningMode);

  const { attributes, listeners, setNodeRef: setDragRef, transform, isDragging } =
    useDraggable({
      id: request.id,
      data: request,
      disabled: !isDraggable,
    });

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
    transform: CSS.Translate.toString(transform),
    opacity: isDragging ? 0.5 : 1,
    paddingLeft: `${depth * INDENT_PX + 12}px`,
  };

  const isScheduled = !!request.spaceId && !!request.startTs;
  const ModeIcon = getPlanningModeIcon(request.planningMode);

  return (
    <div
      ref={combinedRef}
      style={style}
      className={`py-2 pr-3 border rounded-lg bg-card hover:bg-accent/50 transition-colors ${
        isDraggable ? "cursor-grab active:cursor-grabbing" : ""
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
              <Badge
              variant={
                request.status === "done"
                  ? "default"
                  : request.status === "in_progress"
                    ? "secondary"
                    : "outline"
              }
              className="text-xs flex-shrink-0"
            >
              {request.status}
            </Badge>
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
            ) : request.planningMode === "container" ? (
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

export function RequestsPanel({ requests, isLoading, onCreateChild }: RequestsPanelProps) {
  const [searchQuery, setSearchQuery] = useState("");
  const [statusFilter, setStatusFilter] = useState<RequestStatus | "all">(
    "all"
  );
  const [scheduledFilter, setScheduledFilter] = useState<
    "all" | "scheduled" | "unscheduled"
  >("unscheduled"); // Default to showing only unscheduled
  const [collapsedIds, setCollapsedIds] = useState(new Set());

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

  // Filter and prune collapsed subtrees
  const visibleEntries = useMemo(() => {
    const result: FlatTreeEntry[] = [];
    // Track collapsed ancestors to hide children
    const collapsedAncestorDepths: number[] = [];

    for (const entry of flatEntries) {
      // Skip children of collapsed nodes
      while (
        collapsedAncestorDepths.length > 0 &&
        entry.depth <= collapsedAncestorDepths[collapsedAncestorDepths.length - 1]
      ) {
        collapsedAncestorDepths.pop();
      }
      if (collapsedAncestorDepths.length > 0) continue;

      // Apply filters
      const { request } = entry;

      // Search filter
      if (
        searchQuery &&
        !request.name.toLowerCase().includes(searchQuery.toLowerCase())
      ) {
        continue;
      }

      // Status filter
      if (statusFilter !== "all" && request.status !== statusFilter) {
        continue;
      }

      // Scheduled filter
      const isScheduled = !!request.spaceId && !!request.startTs;
      if (scheduledFilter === "scheduled" && !isScheduled && request.planningMode !== "container") {
        continue;
      }
      if (scheduledFilter === "unscheduled" && isScheduled) {
        continue;
      }

      result.push(entry);

      // Track if this node is collapsed to skip its children
      if (entry.hasChildren && collapsedIds.has(request.id)) {
        collapsedAncestorDepths.push(entry.depth);
      }
    }

    return result;
  }, [flatEntries, searchQuery, statusFilter, scheduledFilter, collapsedIds]);

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
              <SelectItem value="planned">Planned</SelectItem>
              <SelectItem value="in_progress">In Progress</SelectItem>
              <SelectItem value="done">Done</SelectItem>
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

      {/* Request List */}
      <div className="flex-1 overflow-y-auto p-4 space-y-1.5">
        {isLoading ? (
          <div className="text-center text-muted-foreground text-sm py-8">
            Loading requests...
          </div>
        ) : visibleEntries.length === 0 ? (
          <div className="text-center text-muted-foreground text-sm py-8">
            No requests found
          </div>
        ) : (
          visibleEntries.map((entry) => (
            <RequestCard
              key={entry.request.id}
              request={entry.request}
              requestId={entry.request.id}
              depth={entry.depth}
              hasChildren={entry.hasChildren}
              isExpanded={!collapsedIds.has(entry.request.id)}
              onToggle={toggleCollapse}
              onCreateChild={onCreateChild ? handleCreateChild : undefined}
            />
          ))
        )}
      </div>
    </div>
  );
}
