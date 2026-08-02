import { FormDialog } from '@foundation/src/components/ui/FormDialog';
import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import { Textarea } from '@foundation/src/components/ui/textarea';
import {
  createResource,
  updateResource,
  type ResourceInfo,
} from '@foundation/src/lib/api/resources-api';
import { qk } from '@foundation/src/lib/api/query-keys';
import { useEntityFormDialog } from '@foundation/src/hooks/useEntityFormDialog';
import type { ResourceTypeInfo } from '@foundation/src/lib/api/resource-types-api';

interface ResourceEditDialogProps {
  resourceType: ResourceTypeInfo;
  resource: ResourceInfo | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

interface FormState {
  name: string;
  description: string;
}

export function ResourceEditDialog({
  resourceType,
  resource,
  open,
  onOpenChange,
}: ResourceEditDialogProps) {

  const { form, set, isDirty, error, submit, isSubmitting } = useEntityFormDialog<
    ResourceInfo,
    FormState,
    ResourceInfo
  >({
    open,
    onOpenChange,
    entity: resource,
    emptyForm: () => ({ name: '', description: '' }),
    toForm: (r) => ({
      name: r.name,
      description: r.description ?? '',
    }),
    save: (form, r) =>
      r
        ? updateResource(r.id, {
            name: form.name,
            description: form.description || undefined,
          })
        : createResource({
            resourceTypeKey: resourceType.key,
            name: form.name,
            description: form.description || undefined,
            // Exclusive matches the default for physical, one-at-a-time resources.
            allocationMode: 'Exclusive',
          }),
    entityLabel: resourceType.displayName,
    invalidates: [qk.resources.byType(resourceType.key), qk.resources.allFlat()],
  });

  const canSubmit = form.name.trim().length > 0;

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title={resource ? `Edit ${resourceType.displayName}` : `New ${resourceType.displayName}`}
      description={resourceType.description || undefined}
      srOnlyDescription={!resourceType.description}
      onSubmit={() => {
        if (canSubmit) submit();
      }}
      isSubmitting={isSubmitting}
      submitLabel="Save"
      submitDisabled={!canSubmit}
      error={error}
      dirty={isDirty}
    >
      <div className="space-y-2">
        <Label htmlFor="resource-name">Name</Label>
        <Input
          id="resource-name"
          value={form.name}
          onChange={(e) => set({ name: e.target.value })}
          maxLength={255}
          autoFocus
          required
        />
      </div>

      <div className="space-y-2">
        <Label htmlFor="resource-description">Description</Label>
        <Textarea
          id="resource-description"
          value={form.description}
          onChange={(e) => set({ description: e.target.value })}
          maxLength={2000}
          rows={3}
        />
      </div>

    </FormDialog>
  );
}
