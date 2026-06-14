import { Badge } from "@foundation/src/components/ui/badge";
import { Button } from "@foundation/src/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@foundation/src/components/ui/dropdown-menu";
import { OrkyoDataTable, type ColumnDef } from "@foundation/src/components/ui/OrkyoDataTable";
import { getPlanningModeIcon, getPlanningModeLabel, getRequestIcon } from "@foundation/src/constants";
import { canHaveChildren } from "@foundation/src/domain/request-tree";
import {
  formatDateDisplay,
  formatDuration,
  formatStatusLabel,
  getStatusColor,
} from "@foundation/src/lib/utils/utils";
import type { Request } from "@foundation/src/types/requests";
import { Edit, Link, MoreHorizontal, Plus, Trash2 } from "lucide-react";
import React, { useMemo } from "react";

interface RequestListViewProps {
  requests: Request[];
  selectedId: string | null;
  onSelect: (id: string) => void;
  onEdit: (request: Request) => void;
  onDelete: (request: Request) => void;
  onAddChild: (request: Request) => void;
  onAddExisting: (request: Request) => void;
}

export const RequestListView = React.memo(function RequestListView({
  requests,
  selectedId,
  onSelect,
  onEdit,
  onDelete,
  onAddChild,
  onAddExisting,
}: RequestListViewProps) {
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

  const columns: ColumnDef<Request>[] = useMemo(() => [
    {
      accessorKey: "name",
      header: "Name",
      cell: ({ row }) => {
        const request = row.original;
        const Icon = getRequestIcon(request.icon) ?? getPlanningModeIcon(request.planningMode);
        return (
          <div
            className={`flex items-center gap-2 cursor-pointer ${selectedId === request.id ? "font-semibold" : ""}`}
            onClick={() => onSelect(request.id)}
            onDoubleClick={() => onEdit(request)}
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
        const parentName = parentNameMap.get(row.original.id);
        return parentName
          ? <span className="text-xs text-muted-foreground truncate block">{parentName}</span>
          : <span className="text-xs text-muted-foreground">—</span>;
      },
    },
    {
      id: "schedule",
      header: "Schedule",
      size: 200,
      cell: ({ row }) => {
        const { startTs, endTs } = row.original;
        return startTs && endTs
          ? <span className="text-xs">{formatDateDisplay(startTs)} — {formatDateDisplay(endTs)}</span>
          : <span className="text-xs text-muted-foreground">Unscheduled</span>;
      },
    },
    {
      id: "duration",
      header: "Duration",
      size: 110,
      cell: ({ row }) => (
        <span className="text-sm">
          {formatDuration(row.original.minimalDurationValue, row.original.minimalDurationUnit)}
        </span>
      ),
    },
    {
      id: "status",
      header: "Status",
      size: 100,
      cell: ({ row }) => (
        <Badge className={getStatusColor(row.original.status)}>
          {formatStatusLabel(row.original.status)}
        </Badge>
      ),
    },
    {
      id: "actions",
      header: () => null,
      size: 60,
      cell: ({ row }) => {
        const request = row.original;
        const isParent = canHaveChildren(request.planningMode);
        return (
          <div className="flex justify-end">
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="ghost" size="sm" className="h-7 w-7 p-0">
                  <MoreHorizontal className="h-4 w-4" />
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
        );
      },
    },
  ], [parentNameMap, selectedId, onSelect, onEdit, onDelete, onAddChild, onAddExisting]);

  return (
    <OrkyoDataTable
      columns={columns}
      data={requests}
      emptyMessage="No requests found."
      pageSize={50}
    />
  );
});
