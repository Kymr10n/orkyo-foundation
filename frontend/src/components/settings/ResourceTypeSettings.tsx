import { useState } from 'react';
import { Plus, Pencil, Trash2, ChevronDown, ChevronRight } from 'lucide-react';
import { Button } from '@foundation/src/components/ui/button';
import { Badge } from '@foundation/src/components/ui/badge';
import { StatusBadge } from '@foundation/src/components/ui/status-badge';
import { ConfirmDialog } from '@foundation/src/components/ui/ConfirmDialog';
import { ErrorAlert } from '@foundation/src/components/ui/ErrorAlert';
import { SettingsPageHeader } from './SettingsPageHeader';
import { ResourceTypeEditDialog } from './ResourceTypeEditDialog';
import { ResourceTypeFieldEditDialog } from './ResourceTypeFieldEditDialog';
import {
  useDeactivateResourceTypeField,
  useDeleteResourceType,
  useResourceTypeFields,
  useResourceTypes,
} from '@foundation/src/hooks/useResourceTypes';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';
import { resourceTypeIcon } from '@foundation/src/components/resources/resource-type-icon';
import type {
  ResourceTypeFieldInfo,
  ResourceTypeInfo,
} from '@foundation/src/lib/api/resource-types-api';

const DATA_TYPE_LABELS: Record<string, string> = {
  text: 'Text',
  number: 'Number',
  boolean: 'Yes / No',
  date: 'Date',
  select: 'Choice list',
};

/** Field list for one type. Mounted only while the type is expanded, so it fetches lazily. */
function FieldList({ resourceType, canEdit }: { resourceType: ResourceTypeInfo; canEdit: boolean }) {
  const { data: fields = [], isLoading } = useResourceTypeFields(resourceType.id);
  const deactivateField = useDeactivateResourceTypeField();
  const [editingField, setEditingField] = useState<ResourceTypeFieldInfo | null>(null);
  const [creatingField, setCreatingField] = useState(false);
  const [removingField, setRemovingField] = useState<ResourceTypeFieldInfo | null>(null);

  return (
    <div className="space-y-3 border-t px-4 py-3">
      {isLoading ? (
        <p className="text-sm text-muted-foreground">Loading fields…</p>
      ) : fields.length === 0 ? (
        <p className="text-sm text-muted-foreground">
          No custom fields yet. Add one to record details specific to {resourceType.displayName}.
        </p>
      ) : (
        <ul className="space-y-2">
          {fields.map((field) => (
            <li key={field.id} className="flex items-center justify-between gap-2">
              <div className="min-w-0">
                <div className="flex items-center gap-2">
                  <span className="font-medium">{field.label}</span>
                  <Badge variant="secondary">
                    {DATA_TYPE_LABELS[field.dataType] ?? field.dataType}
                  </Badge>
                  {field.isRequired && <Badge variant="outline">Required</Badge>}
                </div>
                <p className="text-sm text-muted-foreground">{field.key}</p>
              </div>
              {canEdit && (
                <div className="flex gap-1">
                  <Button
                    variant="ghost"
                    size="icon"
                    onClick={() => setEditingField(field)}
                    aria-label={`Edit ${field.label}`}
                  >
                    <Pencil className="h-4 w-4" />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    className="text-destructive hover:text-destructive"
                    onClick={() => setRemovingField(field)}
                    aria-label={`Deactivate ${field.label}`}
                  >
                    <Trash2 className="h-4 w-4" />
                  </Button>
                </div>
              )}
            </li>
          ))}
        </ul>
      )}

      {canEdit && (
        <Button variant="outline" size="sm" onClick={() => setCreatingField(true)}>
          <Plus className="mr-2 h-4 w-4" />
          Add Field
        </Button>
      )}

      <ResourceTypeFieldEditDialog
        resourceTypeId={resourceType.id}
        field={null}
        open={creatingField}
        onOpenChange={setCreatingField}
      />
      {editingField && (
        <ResourceTypeFieldEditDialog
          resourceTypeId={resourceType.id}
          field={editingField}
          open={!!editingField}
          onOpenChange={(open) => !open && setEditingField(null)}
        />
      )}

      <ConfirmDialog
        open={!!removingField}
        onOpenChange={(open) => !open && setRemovingField(null)}
        title={`Deactivate "${removingField?.label}"?`}
        description="The field stops appearing on forms. Values already recorded are kept."
        confirmLabel="Deactivate"
        destructive
        isPending={deactivateField.isPending}
        onConfirm={() => {
          if (!removingField) return;
          deactivateField.mutate(
            { resourceTypeId: resourceType.id, fieldId: removingField.id },
            { onSuccess: () => setRemovingField(null) },
          );
        }}
      />
    </div>
  );
}

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

                {isExpanded && <FieldList resourceType={type} canEdit={canEdit} />}
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
