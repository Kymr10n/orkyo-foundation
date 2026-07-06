import { useState, useEffect, useMemo } from 'react';
import { useMutation } from '@tanstack/react-query';
import { FormDialog } from '@foundation/src/components/ui/FormDialog';
import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import { Textarea } from '@foundation/src/components/ui/textarea';
import { useDialogDirtyGuard } from '@foundation/src/hooks/useDialogDirtyGuard';
import { createResourceGroup, updateResourceGroup, type ResourceGroupInfo } from '@foundation/src/lib/api/resource-groups-api';
import { qk } from '@foundation/src/lib/api/query-keys';

interface ResourceGroupEditDialogProps {
  resourceTypeKey: string;
  group: ResourceGroupInfo | null;
  isOpen: boolean;
  onClose: () => void;
  onSaved: () => void;
  entityLabel?: string;
}

export function ResourceGroupEditDialog({ resourceTypeKey, group, isOpen, onClose, onSaved, entityLabel = 'Group' }: ResourceGroupEditDialogProps) {
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [defaultAvailabilityPercent, setDefaultAvailabilityPercent] = useState(100);

  useEffect(() => {
    if (group) {
      setName(group.name);
      setDescription(group.description ?? '');
      setDefaultAvailabilityPercent(group.defaultAvailabilityPercent);
    } else {
      setName('');
      setDescription('');
      setDefaultAvailabilityPercent(100);
    }
  }, [group, isOpen]);

  const saveMutation = useMutation({
    mutationFn: () =>
      group
        ? updateResourceGroup(group.id, {
            name,
            description: description || undefined,
            defaultAvailabilityPercent,
          })
        : createResourceGroup({
            resourceTypeKey,
            name,
            description: description || undefined,
            defaultAvailabilityPercent,
          }),
    meta: {
      successMessage: `${entityLabel} ${group ? 'updated' : 'created'}`,
      errorMessage: `Failed to ${group ? 'update' : 'create'} ${entityLabel.toLowerCase()}`,
      invalidates: [qk.resourceGroups.byType(resourceTypeKey)],
    },
    onSuccess: onSaved,
  });

  const handleSubmit = () => {
    if (!name.trim()) return;
    saveMutation.mutate();
  };

  // Mirror EditSpaceDialog: a single isDirty convention (current form vs. the
  // group's persisted values, or vs. empty when creating) feeds the discard guard.
  const isDirty = useMemo(() => {
    if (group) {
      return (
        name !== group.name ||
        description !== (group.description ?? '') ||
        defaultAvailabilityPercent !== group.defaultAvailabilityPercent
      );
    }
    return name !== '' || description !== '' || defaultAvailabilityPercent !== 100;
  }, [group, name, description, defaultAvailabilityPercent]);

  const { guardedOnOpenChange, ConfirmDiscardDialog } = useDialogDirtyGuard({
    isDirty,
    onOpenChange: (o) => { if (!o) onClose(); },
  });

  const errorMessage = saveMutation.error
    ? saveMutation.error instanceof Error
      ? saveMutation.error.message
      : `Failed to ${group ? 'update' : 'create'} ${entityLabel.toLowerCase()}`
    : null;

  return (
    <>
    <FormDialog
      open={isOpen}
      onOpenChange={guardedOnOpenChange}
      title={group ? `Edit ${entityLabel}` : `Add ${entityLabel}`}
      error={errorMessage}
      onSubmit={handleSubmit}
      isSubmitting={saveMutation.isPending}
      submitLabel="Save"
      submitDisabled={!name.trim()}
    >
      <div className="space-y-2">
        <Label htmlFor="name">Name *</Label>
        <Input
          id="name"
          value={name}
          onChange={(e) => setName(e.target.value)}
          required
        />
      </div>

      <div className="space-y-2">
        <Label htmlFor="description">Description</Label>
        <Textarea
          id="description"
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          rows={3}
        />
      </div>

      <div className="space-y-2">
        <Label htmlFor="defaultAvailability">Default Availability (%)</Label>
        <Input
          id="defaultAvailability"
          type="number"
          value={defaultAvailabilityPercent}
          onChange={(e) => setDefaultAvailabilityPercent(Number(e.target.value))}
          min={0}
          max={100}
        />
      </div>
    </FormDialog>
    {ConfirmDiscardDialog}
    </>
  );
}
