import { Badge } from "@foundation/src/components/ui/badge";
import { OrkyoDataTable, type ColumnDef } from "@foundation/src/components/ui/OrkyoDataTable";
import { RequestRowActions } from "@foundation/src/components/requests/RequestRowActions";
import { RequestStatusBadge } from "@foundation/src/components/ui/RequestStatusBadge";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@foundation/src/components/ui/tooltip";
import { getPlanningModeIcon, getPlanningModeLabel, getRequestIcon } from "@foundation/src/constants";
import { useCanEdit } from "@foundation/src/hooks/usePermissions";
import {
  buildDerivedMap,
  resolveDuration,
  resolveSchedule,
} from "@foundation/src/domain/request-tree";
import type { Request } from "@foundation/src/types/requests";
import React, { useCallback, useMemo } from "react";

interface RequestListViewProps {
  requests: Request[];
  selectedId: string | null;
  onSelect: (id: string) => void;
  onEdit: (request: Request) => void;
  onDelete: (request: Request) => void;
  /** Jump to a parent request (switches to the tree, expands ancestors). */
  onNavigateToParent: (parentId: string) => void;
}

export const RequestListView = React.memo(function RequestListView({
  requests,
  selectedId,
  onSelect,
  onEdit,
  onDelete,
  onNavigateToParent,
}: RequestListViewProps) {
  const canEdit = useCanEdit();

  const parentNameMap = useMemo(() => {
    const byId = new Map(requests.map((r) => [r.id, r]));
    const map = new Map<string, string | null>();
    for (const r of requests) {
      map.set(
        r.id,
        r.parentRequestId ? (byId.get(r.parentRequestId)?.name ?? null) : null,
      );
    }
    return map;
  }, [requests]);

  // Derived schedule/duration for parent (Group/Container) rows — same memo
  // pattern the tree uses, so a group shows its rolled-up window/effort instead
  // of its own (empty) minimal duration and "Unscheduled".
  const derivedMap = useMemo(() => buildDerivedMap(requests), [requests]);

  const columns: ColumnDef<Request>[] = useMemo(() => [
    {
      accessorKey: "name",
      header: "Name",
      cell: ({ row }) => {
        const request = row.original;
        const Icon = getRequestIcon(request.icon) ?? getPlanningModeIcon(request.planningMode);
        return (
          <div
            className={`flex items-center gap-2 ${selectedId === request.id ? "font-semibold" : ""}`}
          >
            <Icon className="h-4 w-4 text-muted-foreground flex-shrink-0" />
            <span className="font-medium truncate">{request.name}</span>
          </div>
        );
      },
    },
    {
      id: "kind",
      header: "Kind",
      size: 100,
      cell: ({ row }) => (
        <Badge variant="outline" className="text-xs font-normal">
          {getPlanningModeLabel(row.original.planningMode)}
        </Badge>
      ),
    },
    {
      id: "parent",
      header: "Parent",
      size: 180,
      cell: ({ row }) => {
        const { parentRequestId } = row.original;
        const parentName = parentNameMap.get(row.original.id);
        return parentName && parentRequestId ? (
          <button
            className="text-xs text-primary hover:underline truncate block max-w-full text-left"
            onClick={(e) => {
              e.stopPropagation();
              onNavigateToParent(parentRequestId);
            }}
          >
            {parentName}
          </button>
        ) : (
          <span className="text-xs text-muted-foreground">—</span>
        );
      },
    },
    {
      id: "schedule",
      header: "Schedule",
      size: 200,
      cell: ({ row }) => {
        const request = row.original;
        const { text, isDerived } = resolveSchedule(request, derivedMap.get(request.id) ?? null);
        if (!text) {
          return <span className="text-xs text-muted-foreground">Unscheduled</span>;
        }
        if (isDerived) {
          return (
            <Tooltip>
              <TooltipTrigger asChild>
                <span className="text-xs italic">{text}</span>
              </TooltipTrigger>
              <TooltipContent side="top">Derived from children</TooltipContent>
            </Tooltip>
          );
        }
        return <span className="text-xs">{text}</span>;
      },
    },
    {
      id: "duration",
      header: "Duration",
      size: 110,
      cell: ({ row }) => {
        const request = row.original;
        const { text, isDerived } = resolveDuration(request, derivedMap.get(request.id) ?? null);
        if (isDerived) {
          return (
            <Tooltip>
              <TooltipTrigger asChild>
                <span className="text-sm italic">{text}</span>
              </TooltipTrigger>
              <TooltipContent side="top">Sum of children</TooltipContent>
            </Tooltip>
          );
        }
        return <span className="text-sm">{text}</span>;
      },
    },
    {
      id: "status",
      header: "Status",
      size: 100,
      cell: ({ row }) => <RequestStatusBadge status={row.original.status} />,
    },
    {
      id: "actions",
      header: () => null,
      size: 60,
      cell: ({ row }) => (
        <RequestRowActions
          request={row.original}
          canEdit={canEdit}
          onEdit={onEdit}
          onDelete={onDelete}
        />
      ),
    },
  ], [parentNameMap, derivedMap, selectedId, onEdit, onNavigateToParent, canEdit, onDelete]);

  // Phone presentation: name + actions on top; kind/status/duration badges and
  // the schedule window below.
  const renderCard = useCallback((request: Request) => {
    const Icon = getRequestIcon(request.icon) ?? getPlanningModeIcon(request.planningMode);
    const derived = derivedMap.get(request.id) ?? null;
    const { text: durationText, isDerived: durationIsDerived } = resolveDuration(request, derived);
    const { text: scheduleTextRaw, isDerived: scheduleIsDerived } = resolveSchedule(request, derived);
    const scheduleText = scheduleTextRaw ?? "Unscheduled";
    return (
      <div className="space-y-2">
        <div className="flex items-start justify-between gap-2">
          <div
            className={`flex items-center gap-2 min-w-0 cursor-pointer ${selectedId === request.id ? "font-semibold" : ""}`}
            onClick={() => {
              onSelect(request.id);
              onEdit(request);
            }}
          >
            <Icon className="h-4 w-4 text-muted-foreground flex-shrink-0" />
            <span className="font-medium truncate">{request.name}</span>
          </div>
          <RequestRowActions
            request={request}
            canEdit={canEdit}
            onEdit={onEdit}
            onDelete={onDelete}
          />
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant="outline" className="text-xs font-normal">
            {getPlanningModeLabel(request.planningMode)}
          </Badge>
          <RequestStatusBadge status={request.status} />
          <span className={`text-xs text-muted-foreground ${durationIsDerived ? "italic" : ""}`}>
            {durationText}
          </span>
        </div>
        <div className={`text-xs text-muted-foreground ${scheduleIsDerived ? "italic" : ""}`}>
          {scheduleText}
        </div>
      </div>
    );
  }, [selectedId, derivedMap, onSelect, onEdit, canEdit, onDelete]);

  return (
    <OrkyoDataTable
      columns={columns}
      data={requests}
      emptyMessage="No requests found."
      pageSize={50}
      renderCard={renderCard}
      onRowClick={(request) => {
        onSelect(request.id);
        onEdit(request);
      }}
    />
  );
});
