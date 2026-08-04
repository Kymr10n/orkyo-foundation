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
import { DURATION_TO_MINUTES } from "@foundation/src/domain/constants";
import { formatStatusLabel } from "@foundation/src/lib/utils/utils";
import type { PlanningMode, Request } from "@foundation/src/types/requests";
import { useTableUrlState } from '@foundation/src/hooks/useTableUrlState';
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
      meta: { filter: { type: "text" } },
      // Matches the description too, which the page's search box did and no column shows.
      // An explicit filterFn wins over the one meta.filter would otherwise attach.
      filterFn: (row, _columnId, query: string) => {
        const q = query.toLowerCase();
        const r = row.original;
        return (
          r.name.toLowerCase().includes(q) || (r.description?.toLowerCase().includes(q) ?? false)
        );
      },
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
      accessorFn: (r) => r.planningMode,
      header: "Kind",
      size: 100,
      meta: { filter: { type: "enum", getLabel: (v) => getPlanningModeLabel(v as PlanningMode) } },
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
      enableSorting: false,
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
      // Sort/filter on the resolved start timestamp (not the display string, which
      // would order alphabetically) — same derived-first fallback the cell renders.
      accessorFn: (r) => {
        const derived = derivedMap.get(r.id) ?? null;
        if (derived?.startTs && derived?.endTs) return derived.startTs;
        return r.startTs && r.endTs ? r.startTs : "";
      },
      header: "Schedule",
      size: 200,
      meta: { filter: { type: "date" } },
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
      // Sort/filter on the resolved duration in minutes (not the "2 hours" display
      // string) — derived sum for parents, own minimal duration otherwise.
      accessorFn: (r) => {
        const derived = derivedMap.get(r.id) ?? null;
        return derived
          ? derived.totalDurationValue * DURATION_TO_MINUTES[derived.totalDurationUnit]
          : r.minimalDurationValue * DURATION_TO_MINUTES[r.minimalDurationUnit];
      },
      header: "Duration",
      size: 110,
      meta: { filter: { type: "number" } },
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
      accessorFn: (r) => r.status,
      header: "Status",
      size: 100,
      meta: { filter: { type: "enum", getLabel: formatStatusLabel } },
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

  // Header sort/filter state lives in the URL: bookmarkable, shareable, Back-safe.
  const tableUrlState = useTableUrlState('requests', columns);

  return (
    <OrkyoDataTable
        {...tableUrlState}
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
