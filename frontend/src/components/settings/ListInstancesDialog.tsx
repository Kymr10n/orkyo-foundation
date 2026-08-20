import { useState } from 'react';
import { Pencil, Plus, Rows3, Trash2 } from 'lucide-react';
import { Button } from '@foundation/src/components/ui/button';
import { ConfirmDialog } from '@foundation/src/components/ui/ConfirmDialog';
import { ScaffoldDialog } from '@foundation/src/components/ui/ScaffoldDialog';
import { FormDialog } from '@foundation/src/components/ui/FormDialog';
import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import { ListRowsEditor } from '@foundation/src/components/lists/ListRowsEditor';
import { useEntityFormDialog } from '@foundation/src/hooks/useEntityFormDialog';
import {
  useCreateSharedListInstance,
  useDeleteSharedListInstance,
  useSharedListInstances,
  useUpdateSharedListInstance,
} from '@foundation/src/hooks/useListDefinitions';
import { useListDefinition } from '@foundation/src/hooks/useListDefinitions';
import { qk } from '@foundation/src/lib/api/query-keys';
import type { ListInstance } from '@foundation/src/lib/api/lists-api';

interface ListInstancesDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  definitionId: string;
  definitionName: string;
}

/**
 * The shared instances of one definition — named sets of rows that many resources point at.
 *
 * A shared instance is where "edit the price once and every machine sees it" happens: resources
 * hold row ids, not copies, so the rows are maintained here and nowhere else.
 */
export function ListInstancesDialog({
  open,
  onOpenChange,
  definitionId,
  definitionName,
}: ListInstancesDialogProps) {
  const { data: instances = [], isLoading } = useSharedListInstances(open ? definitionId : null);
  const { data: definition } = useListDefinition(open ? definitionId : null);
  const deleteInstance = useDeleteSharedListInstance(definitionId);

  const [editing, setEditing] = useState<ListInstance | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [removing, setRemoving] = useState<ListInstance | null>(null);
  const [managingRows, setManagingRows] = useState<ListInstance | null>(null);

  return (
    <ScaffoldDialog
      open={open}
      onOpenChange={onOpenChange}
      title={`Shared lists — ${definitionName}`}
      description="Named sets of rows built from this definition. A resource field can point at one, and every resource pointing at it sees the same rows."
    >
      <div className="space-y-4">
        <div className="flex justify-end">
          <Button size="sm" onClick={() => setCreateOpen(true)}>
            <Plus className="mr-2 h-4 w-4" />
            Add shared list
          </Button>
        </div>

        {isLoading && <p className="text-muted-foreground text-sm">Loading…</p>}

        {!isLoading && instances.length === 0 && (
          <p className="text-muted-foreground rounded-md border border-dashed p-4 text-sm">
            No shared lists yet. Add one to hold rows that many resources can pick from.
          </p>
        )}

        <ul className="divide-y rounded-md border">
          {instances.map((instance) => (
            <li key={instance.id} className="flex items-center justify-between gap-3 p-3">
              <span className="min-w-0 truncate font-medium">{instance.name}</span>
              <div className="flex shrink-0 gap-1">
                <Button
                  variant="ghost"
                  size="icon"
                  aria-label={`Rows of ${instance.name}`}
                  onClick={() => setManagingRows(instance)}
                >
                  <Rows3 className="h-4 w-4" />
                </Button>
                <Button
                  variant="ghost"
                  size="icon"
                  aria-label={`Rename ${instance.name}`}
                  onClick={() => setEditing(instance)}
                >
                  <Pencil className="h-4 w-4" />
                </Button>
                <Button
                  variant="ghost"
                  size="icon"
                  aria-label={`Remove ${instance.name}`}
                  onClick={() => setRemoving(instance)}
                >
                  <Trash2 className="text-destructive h-4 w-4" />
                </Button>
              </div>
            </li>
          ))}
        </ul>
      </div>

      <InstanceNameDialog
        open={createOpen}
        onOpenChange={setCreateOpen}
        definitionId={definitionId}
        instance={null}
      />

      {editing && (
        <InstanceNameDialog
          open={editing !== null}
          onOpenChange={(next) => !next && setEditing(null)}
          definitionId={definitionId}
          instance={editing}
        />
      )}

      {managingRows && (
        <ScaffoldDialog
          open={managingRows !== null}
          onOpenChange={(next) => !next && setManagingRows(null)}
          title={`Rows — ${managingRows.name}`}
          description="Every resource pointing at this list sees these rows. Editing one is seen everywhere; deleting one removes it from the resources that picked it."
        >
          <ListRowsEditor
            columns={definition?.columns ?? []}
            instanceId={managingRows.id}
            displayColumnId={definition?.displayColumnId ?? null}
            emptyMessage="No rows yet."
          />
        </ScaffoldDialog>
      )}

      <ConfirmDialog
        open={removing !== null}
        onOpenChange={(next) => !next && setRemoving(null)}
        title={`Delete ${removing?.name ?? 'this list'}?`}
        description="This is refused while any field still points at it. Its rows are deleted with it."
        confirmLabel="Delete"
        destructive
        onConfirm={async () => {
          if (removing) await deleteInstance.mutateAsync(removing.id);
          setRemoving(null);
        }}
      />
    </ScaffoldDialog>
  );
}

/** Create or rename one shared instance — a name is all it carries. */
function InstanceNameDialog({
  open,
  onOpenChange,
  definitionId,
  instance,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  definitionId: string;
  instance: ListInstance | null;
}) {
  const createInstance = useCreateSharedListInstance(definitionId);
  const updateInstance = useUpdateSharedListInstance(definitionId);

  const { form, set, isDirty, error, submit, isSubmitting } = useEntityFormDialog<
    ListInstance,
    { name: string },
    unknown
  >({
    open,
    onOpenChange,
    entity: instance,
    emptyForm: () => ({ name: '' }),
    toForm: (entity) => ({ name: entity.name ?? '' }),
    save: (values, entity) =>
      entity
        ? updateInstance.mutateAsync({ instanceId: entity.id, request: { name: values.name.trim() } })
        : createInstance.mutateAsync({ name: values.name.trim() }),
    entityLabel: 'Shared list',
    invalidates: [qk.lists.all()],
  });

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title={instance ? 'Rename shared list' : 'New shared list'}
      error={error}
      onSubmit={submit}
      isSubmitting={isSubmitting}
      submitLabel={instance ? 'Save' : 'Create'}
      submitDisabled={form.name.trim().length === 0}
      dirty={isDirty}
    >
      <div className="space-y-2">
        <Label htmlFor="list-instance-name">
          Name<span className="text-destructive ml-1">*</span>
        </Label>
        <Input
          id="list-instance-name"
          value={form.name}
          onChange={(e) => set({ name: e.target.value })}
          placeholder="Standard components"
          maxLength={100}
        />
      </div>
    </FormDialog>
  );
}
