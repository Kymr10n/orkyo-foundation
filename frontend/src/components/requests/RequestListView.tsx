import { Badge } from "@foundation/src/components/ui/badge";
import { Button } from "@foundation/src/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@foundation/src/components/ui/dropdown-menu";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@foundation/src/components/ui/table";
import { getPlanningModeIcon, getPlanningModeLabel } from "@foundation/src/constants";
import { canHaveChildren } from "@foundation/src/domain/request-tree";
import {
  formatDateDisplay,
  formatDuration,
  formatStatusLabel,
  getStatusColor,
} from "@foundation/src/lib/utils/utils";
import type { Request } from "@foundation/src/types/requests";
import { Edit, Link, MoreHorizontal, Plus, Trash2 } from "lucide-react";
import React, { useMemo, useRef } from "react";
import { useVirtualizer } from "@tanstack/react-virtual";

// ---------------------------------------------------------------------------
// List row
// ---------------------------------------------------------------------------

const ListRow = React.memo(function ListRow({
  request,
  parentName,
  onEdit,
  onDelete,
  onAddChild,
  onAddExisting,
  onSelect,
  isSelected,
}: {
  request: Request;
  parentName: string | null;
  onEdit: (request: Request) => void;
  onDelete: (request: Request) => void;
  onAddChild: (request: Request) => void;
  onAddExisting: (request: Request) => void;
  onSelect: (id: string) => void;
  isSelected: boolean;
}) {
  const Icon = getPlanningModeIcon(request.planningMode);
  const isParent = canHaveChildren(request.planningMode);

  return (
    <TableRow
      className={`cursor-pointer hover:bg-muted/50 ${isSelected ? "bg-muted" : ""}`}
      onClick={() => onSelect(request.id)}
      onDoubleClick={() => onEdit(request)}
    >
      {/* Name */}
      <TableCell className="w-[25%]">
        <div className="flex items-center gap-2">
          <Icon className="h-4 w-4 text-muted-foreground flex-shrink-0" />
          <span className="font-medium truncate">{request.name}</span>
        </div>
      </TableCell>

      {/* Kind */}
      <TableCell className="w-[8%]">
        <Badge variant="outline" className="text-xs font-normal">
          {getPlanningModeLabel(request.planningMode)}
        </Badge>
      </TableCell>

      {/* Parent */}
      <TableCell className="text-sm w-[15%]">
        {parentName ? (
          <span className="text-xs text-muted-foreground truncate block">
            {parentName}
          </span>
        ) : (
          <span className="text-xs text-muted-foreground">—</span>
        )}
      </TableCell>

      {/* Schedule */}
      <TableCell className="text-sm w-[17%]">
        {request.startTs && request.endTs ? (
          <span className="text-xs">
            {formatDateDisplay(request.startTs)} — {formatDateDisplay(request.endTs)}
          </span>
        ) : (
          <span className="text-xs text-muted-foreground">Unscheduled</span>
        )}
      </TableCell>

      {/* Duration */}
      <TableCell className="text-sm w-[10%]">
        {formatDuration(request.minimalDurationValue, request.minimalDurationUnit)}
      </TableCell>

      {/* Status */}
      <TableCell className="w-[8%]">
        <Badge className={getStatusColor(request.status)}>
          {formatStatusLabel(request.status)}
        </Badge>
      </TableCell>

      {/* Actions */}
      <TableCell className="text-right w-[7%]">
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button
              variant="ghost"
              size="sm"
              className="h-7 w-7 p-0"
              onClick={(e) => e.stopPropagation()}
            >
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
      </TableCell>
    </TableRow>
  );
});

// ---------------------------------------------------------------------------
// RequestListView
// ---------------------------------------------------------------------------

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
  const parentRef = useRef<HTMLDivElement>(null);

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

  const virtualizer = useVirtualizer({
    count: requests.length,
    getScrollElement: () => parentRef.current,
    estimateSize: () => 49,
    overscan: 10,
  });

  return (
    <div className="border rounded-md">
      <Table className="table-fixed">
        <TableHeader>
          <TableRow>
            <TableHead className="w-[25%]">Name</TableHead>
            <TableHead className="w-[8%]">Kind</TableHead>
            <TableHead className="w-[15%]">Parent</TableHead>
            <TableHead className="w-[17%]">Schedule</TableHead>
            <TableHead className="w-[10%]">Duration</TableHead>
            <TableHead className="w-[8%]">Status</TableHead>
            <TableHead className="text-right w-[7%]">Actions</TableHead>
          </TableRow>
        </TableHeader>
      </Table>

      <div ref={parentRef} className="overflow-auto" style={{ height: "600px" }}>
        <div
          style={{
            height: `${virtualizer.getTotalSize()}px`,
            width: "100%",
            position: "relative",
          }}
        >
          <Table className="table-fixed">
            <TableBody>
              {virtualizer.getVirtualItems().map((virtualRow) => {
                const request = requests[virtualRow.index];
                return (
                  <ListRow
                    key={request.id}
                    request={request}
                    parentName={parentNameMap.get(request.id) ?? null}
                    onEdit={onEdit}
                    onDelete={onDelete}
                    onAddChild={onAddChild}
                    onAddExisting={onAddExisting}
                    onSelect={onSelect}
                    isSelected={selectedId === request.id}
                  />
                );
              })}
            </TableBody>
          </Table>
        </div>
      </div>
    </div>
  );
});
