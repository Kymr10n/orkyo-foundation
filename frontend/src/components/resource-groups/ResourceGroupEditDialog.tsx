import { FormDialog } from '@foundation/src/components/ui/FormDialog';
import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import { Textarea } from '@foundation/src/components/ui/textarea';
import { createResourceGroup, updateResourceGroup, type ResourceGroupInfo } from '@foundation/src/lib/api/resource-groups-api';
import { qk } from '@foundation/src/lib/api/query-keys';
import { useEntityFormDialog } from '@foundation/src/hooks/useEntityFormDialog';

interface ResourceGroupEditDialogProps {
  resourceTypeKey: string;
  group: ResourceGroupInfo | null;
  isOpen: boolean;
  onClose: () => void;
  onSaved: () => void;
  entityLabel?: string;
}

interface FormState {
  name: string;
  description: string;
  defaultAvailabilityPercent: number;
}

const EMPTY: FormState = { name: '', description: '', defaultAvailabilityPercent: 100 };

export function ResourceGroupEditDialog({ resourceTypeKey, group, isOpen, onClose, onSaved, entityLabel = 'Group' }: ResourceGroupEditDialogProps) {
  // The shared scaffold owns form + baseline, reseed-on-open, the dirty compare and the
  // create-or-update mutation with its meta feedback — this dialog is exactly the shape it
  // was extracted for, so it keeps only field rendering and the name-required rule.
  const { form, set, isDirty, error, submit, isSubmitting } = useEntityFormDialog({
    open: isOpen,
    onOpenChange: (o: boolean) => { if (!o) onClose(); },
    entity: group,
    emptyForm: () => EMPTY,
    toForm: (g: ResourceGroupInfo): FormState => ({
      name: g.name,
      description: g.description ?? '',
      defaultAvailabilityPercent: g.defaultAvailabilityPercent,
    }),
    save: (f: FormState, g: ResourceGroupInfo | null) =>
      g
        ? updateResourceGroup(g.id, {
            name: f.name,
            description: f.description || undefined,
            defaultAvailabilityPercent: f.defaultAvailabilityPercent,
          })
        : createResourceGroup({
            resourceTypeKey,
            name: f.name,
            description: f.description || undefined,
            defaultAvailabilityPercent: f.defaultAvailabilityPercent,
          }),
    entityLabel,
    invalidates: [qk.resourceGroups.byType(resourceTypeKey)],
    onSaved,
  });

  const nameValid = !!form.name.trim();

  return (
    <FormDialog
      open={isOpen}
      onOpenChange={(o) => { if (!o) onClose(); }}
      dirty={isDirty}
      title={group ? `Edit ${entityLabel}` : `Add ${entityLabel}`}
      error={error}
      onSubmit={() => { if (nameValid) submit(); }}
      isSubmitting={isSubmitting}
      submitLabel="Save"
      submitDisabled={!nameValid}
    >
      <div className="space-y-2">
        <Label htmlFor="name">Name *</Label>
        <Input
          id="name"
          value={form.name}
          onChange={(e) => set({ name: e.target.value })}
          required
        />
      </div>

      <div className="space-y-2">
        <Label htmlFor="description">Description</Label>
        <Textarea
          id="description"
          value={form.description}
          onChange={(e) => set({ description: e.target.value })}
          rows={3}
        />
      </div>

      <div className="space-y-2">
        <Label htmlFor="defaultAvailability">Default Availability (%)</Label>
        <Input
          id="defaultAvailability"
          type="number"
          value={form.defaultAvailabilityPercent}
          onChange={(e) => set({ defaultAvailabilityPercent: Number(e.target.value) })}
          min={0}
          max={100}
        />
      </div>
    </FormDialog>
  );
}
