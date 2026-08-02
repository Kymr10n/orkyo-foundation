import { useState } from 'react';
import { Plus, Pencil, Trash2, ChevronDown, ChevronRight } from 'lucide-react';
import { Button } from '@foundation/src/components/ui/button';
import { Badge } from '@foundation/src/components/ui/badge';
import { StatusBadge } from '@foundation/src/components/ui/status-badge';
import { ConfirmDialog } from '@foundation/src/components/ui/ConfirmDialog';
import { ErrorAlert } from '@foundation/src/components/ui/ErrorAlert';
import { SettingsPageHeader } from './SettingsPageHeader';
import { ResourceTypeEditDialog } from './ResourceTypeEditDialog';
import {
  useDeleteResourceType,
  useResourceTypes,
} from '@foundation/src/hooks/useResourceTypes';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';
import { resourceTypeIcon } from '@foundation/src/components/resources/resource-type-icon';
import type {
  ResourceTypeInfo,
} from '@foundation/src/lib/api/resource-types-api';


export function ResourceTypeSettings() {
  const canEdit = useCanEdit();
  const { data: types = [], isLoading, error } = useResourceTypes();
  const deleteType = useDeleteResourceType();

  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const [editing, setEditing] = useState<ResourceTypeInfo | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [removing, setRemoving] = useState<ResourceTypeInfo | null>(null);

  const toggle = (id: string) =>
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  const errorMsg =
    error instanceof Error ? error.message : error ? 'Failed to load resource types' : null;

  return (
    <div className="space-y-6">
      <SettingsPageHeader
        title="Resource Types"
        description="The kinds of things your organization manages. Spaces, people, and tools are built in; add your own — cars, cameras, anything — and give each the fields it needs."
      >
        {canEdit && (
          <Button onClick={() => setCreateOpen(true)}>
            <Plus className="mr-2 h-4 w-4" />
            Add Resource Type
          </Button>
        )}
      </SettingsPageHeader>

      {errorMsg && <ErrorAlert message={errorMsg} />}

      {isLoading ? (
        <p className="text-sm text-muted-foreground">Loading resource types…</p>
      ) : (
        <ul className="space-y-3">
          {types.map((type) => {
            const isExpanded = expanded.has(type.id);
            return (
              <li key={type.id} className="rounded-lg border">
                <div className="flex items-center justify-between gap-2 p-4">
                  <button
                    type="button"
                    className="flex min-w-0 flex-1 items-center gap-2 text-left"
                    onClick={() => toggle(type.id)}
                    aria-expanded={isExpanded}
                  >
                    {isExpanded ? (
                      <ChevronDown className="h-4 w-4 shrink-0" />
                    ) : (
                      <ChevronRight className="h-4 w-4 shrink-0" />
                    )}
                    <span className="min-w-0">
                      <span className="flex items-center gap-2">
                        {(() => {
                          const Icon = resourceTypeIcon(type.icon);
                          return <Icon className="h-4 w-4 shrink-0 text-muted-foreground" />;
                        })()}
                        <span className="truncate font-medium">{type.displayName}</span>
                        {type.isSystem && <Badge variant="secondary">Built-in</Badge>}
                        {!type.isActive && <StatusBadge status="inactive" label="Inactive" />}
                      </span>
                      <span className="block text-sm text-muted-foreground">
                        {type.description || type.key}
                      </span>
                    </span>
                  </button>

                  {canEdit && !type.isSystem && (
                    <div className="flex gap-1">
                      <Button
                        variant="ghost"
                        size="icon"
                        onClick={() => setEditing(type)}
                        aria-label={`Edit ${type.displayName}`}
                      >
                        <Pencil className="h-4 w-4" />
                      </Button>
                      <Button
                        variant="ghost"
                        size="icon"
                        className="text-destructive hover:text-destructive"
                        onClick={() => setRemoving(type)}
                        aria-label={`Remove ${type.displayName}`}
                      >
                        <Trash2 className="h-4 w-4" />
                      </Button>
                    </div>
                  )}
                </div>

              </li>
            );
          })}
        </ul>
      )}

      <ResourceTypeEditDialog resourceType={null} open={createOpen} onOpenChange={setCreateOpen} />
      {editing && (
        <ResourceTypeEditDialog
          resourceType={editing}
          open={!!editing}
          onOpenChange={(open) => !open && setEditing(null)}
        />
      )}

      <ConfirmDialog
        open={!!removing}
        onOpenChange={(open) => !open && setRemoving(null)}
        title={`Remove "${removing?.displayName}"?`}
        description="If resources of this type already exist, the type is deactivated instead of deleted so those resources keep working."
        confirmLabel="Remove"
        destructive
        isPending={deleteType.isPending}
        onConfirm={() => {
          if (!removing) return;
          deleteType.mutate(removing.id, { onSuccess: () => setRemoving(null) });
        }}
      />
    </div>
  );
}
