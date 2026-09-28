import { useState } from "react";
import { Pencil, Plus, Trash2 } from "lucide-react";
import { SettingsPageHeader } from "./SettingsPageHeader";
import { Badge } from "@foundation/src/components/ui/badge";
import { Button } from "@foundation/src/components/ui/button";
import { ConfirmDialog } from "@foundation/src/components/ui/ConfirmDialog";
import { OrkyoDataTable, type ColumnDef } from "@foundation/src/components/ui/OrkyoDataTable";
import { RowActions } from "@foundation/src/components/ui/RowActions";
import { useTableUrlState } from "@foundation/src/hooks/useTableUrlState";
import { useCanEdit } from "@foundation/src/hooks/usePermissions";
import { useDeleteRouting, useRoutings } from "@foundation/src/hooks/useRoutings";
import type { Routing } from "@foundation/src/types/routings";
import { RoutingEditDialog } from "./RoutingEditDialog";
import { LoadingSpinner } from "@foundation/src/components/ui/LoadingSpinner";

/** @internal Exported for unit testing. "Saw → Mill → Deburr", in step order. */
export function describeSteps(routing: Routing): string {
  return [...routing.steps]
    .sort((a, b) => a.stepNo - b.stepNo)
    .map((s) => s.operationName)
    .join(" → ");
}

export function RoutingSettings() {
  const canEdit = useCanEdit();
  const [dialog, setDialog] = useState<{ open: boolean; routing: Routing | null }>({
    open: false,
    routing: null,
  });
  const [deleting, setDeleting] = useState<Routing | null>(null);

  const { data: routings = [], isLoading, error, refetch } = useRoutings();
  const deleteMutation = useDeleteRouting();

  const handleConfirmDelete = async () => {
    if (!deleting) return;
    try {
      await deleteMutation.mutateAsync(deleting.id);
      setDeleting(null);
    } catch {
      // error toast fired centrally via the mutation's meta (MutationCache)
    }
  };

  const renderActions = (routing: Routing) => (
    <RowActions
      triggerLabel={`Actions for ${routing.name}`}
      actions={[
        { label: "Edit", icon: Pencil, onSelect: () => setDialog({ open: true, routing }), disabled: !canEdit },
        { label: "Delete", icon: Trash2, onSelect: () => setDeleting(routing), disabled: !canEdit, destructive: true },
      ]}
    />
  );

  const columns: ColumnDef<Routing>[] = [
    {
      accessorKey: "name",
      header: "Name",
      meta: { filter: { type: "text" } },
      cell: ({ row }) => <span className="font-semibold">{row.original.name}</span>,
    },
    {
      id: "steps",
      accessorFn: (r) => describeSteps(r),
      header: "Steps",
      meta: { filter: { type: "text" } },
      cell: ({ row }) => (
        <div className="flex items-center gap-2 min-w-0">
          <Badge variant="outline" className="text-xs shrink-0">{row.original.steps.length}</Badge>
          <span className="text-sm text-muted-foreground truncate">{describeSteps(row.original)}</span>
        </div>
      ),
    },
    {
      id: "description",
      accessorFn: (r) => r.description ?? "",
      header: "Description",
      meta: { filter: { type: "text" } },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground truncate max-w-md">
          {row.original.description || "—"}
        </span>
      ),
    },
    {
      id: "actions",
      header: () => null,
      size: 96,
      cell: ({ row }) => renderActions(row.original),
    },
  ];

  const tableUrlState = useTableUrlState("routings", columns);

  const renderCard = (routing: Routing) => (
    <div className="flex items-start justify-between gap-2">
      <div className="min-w-0 space-y-1">
        <span className="font-semibold truncate block">{routing.name}</span>
        <p className="text-sm text-muted-foreground">{describeSteps(routing)}</p>
      </div>
      {renderActions(routing)}
    </div>
  );

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <LoadingSpinner inline size="xs" muted message="Loading routings…" />
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <SettingsPageHeader
        title="Routings"
        description="The sequence of operations a part goes through. A work order created from a routing gets one request per step, chained in order, with setup time plus run time × quantity."
      >
        <Button onClick={() => setDialog({ open: true, routing: null })} disabled={!canEdit}>
          <Plus className="h-4 w-4 mr-2" />
          Add Routing
        </Button>
      </SettingsPageHeader>

      <OrkyoDataTable
        {...tableUrlState}
        columns={columns}
        data={routings}
        error={error ? error.message || "Failed to load routings" : null}
        onRetry={() => void refetch()}
        emptyMessage={routings.length === 0 ? "No routings defined yet" : undefined}
        emptyAction={
          routings.length === 0 && (
            <Button onClick={() => setDialog({ open: true, routing: null })} variant="outline" disabled={!canEdit}>
              <Plus className="h-4 w-4 mr-2" />
              Create your first routing
            </Button>
          )
        }
        renderCard={renderCard}
        onRowClick={(routing) => setDialog({ open: true, routing })}
      />

      <RoutingEditDialog
        routing={dialog.routing}
        open={dialog.open}
        onOpenChange={(open) => setDialog((d) => ({ ...d, open }))}
      />

      <ConfirmDialog
        open={!!deleting}
        onOpenChange={(open) => !open && setDeleting(null)}
        title={`Delete "${deleting?.name}"?`}
        description="Work orders already created from it are kept. This action cannot be undone."
        confirmLabel="Delete"
        destructive
        isPending={deleteMutation.isPending}
        onConfirm={handleConfirmDelete}
      />
    </div>
  );
}
