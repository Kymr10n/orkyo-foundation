import { useState } from 'react';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@foundation/src/components/ui/dialog';
import { ErrorAlert } from '@foundation/src/components/ui/ErrorAlert';
import { DialogFormFooter } from '@foundation/src/components/ui/DialogFormFooter';
import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import { Textarea } from '@foundation/src/components/ui/textarea';
import { Badge } from '@foundation/src/components/ui/badge';
import { Checkbox } from '@foundation/src/components/ui/checkbox';
import type { Criterion } from '@foundation/src/types/criterion';
import { useUpdateCriterion, useUpdateCriterionApplicability } from '@foundation/src/hooks/useCriteria';
import { EnumValueEditor } from './EnumValueEditor';
import {
  CRITERION_RESOURCE_TYPE_OPTIONS as RESOURCE_TYPE_OPTIONS,
  useCriterionForm,
  useSeedCriterionForm,
} from './useCriterionForm';

interface EditCriterionDialogProps {
  criterion: Criterion;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSuccess: (criterion: Criterion) => void;
}

export function EditCriterionDialog({
  criterion,
  open,
  onOpenChange,
  onSuccess,
}: EditCriterionDialogProps) {
  const updateMutation = useUpdateCriterion();
  const applicabilityMutation = useUpdateCriterionApplicability();
  const form = useCriterionForm();
  const [error, setError] = useState<string | null>(null);
  const isSubmitting = updateMutation.isPending || applicabilityMutation.isPending;

  useSeedCriterionForm(form, criterion);

  const handleSubmit = async (e: React.SyntheticEvent<HTMLFormElement>) => {
    e.preventDefault();
    setError(null);

    const validationError = form.validate(criterion.dataType);
    if (validationError) {
      setError(validationError);
      return;
    }

    try {
      const updated = await updateMutation.mutateAsync({
        id: criterion.id,
        data: {
          description: form.description.trim() || undefined,
          enumValues: criterion.dataType === 'Enum' ? form.enumValues : undefined,
          unit: criterion.dataType === 'Number' && form.unit.trim() ? form.unit.trim() : undefined,
        },
      });

      // Update applicability if it changed
      const currentKeys = [...(criterion.resourceTypeKeys ?? [])].sort().join(',');
      const newKeys = [...form.resourceTypeKeys].sort().join(',');
      if (currentKeys !== newKeys) {
        await applicabilityMutation.mutateAsync({
          id: criterion.id,
          data: { resourceTypeKeys: form.resourceTypeKeys },
        });
      }

      onSuccess(updated);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to update criterion');
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-[500px]">
        <DialogHeader>
          <DialogTitle>Edit Criterion</DialogTitle>
          <DialogDescription>
            Update the criterion details. Name and data type cannot be changed.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit}>
          <div className="space-y-4 py-4">
            {/* Name (read-only) */}
            <div className="space-y-2">
              <Label>Name</Label>
              <div className="px-3 py-2 bg-muted rounded-md font-mono text-sm">
                {criterion.name}
              </div>
              <p className="text-xs text-muted-foreground">
                Name cannot be changed to maintain referential integrity
              </p>
            </div>

            {/* Data Type (read-only) */}
            <div className="space-y-2">
              <Label>Data Type</Label>
              <div className="px-3 py-2 bg-muted rounded-md">
                <Badge variant="secondary">{criterion.dataType}</Badge>
              </div>
              <p className="text-xs text-muted-foreground">
                Data type cannot be changed
              </p>
            </div>

            {/* Description */}
            <div className="space-y-2">
              <Label htmlFor="description">Description</Label>
              <Textarea
                id="description"
                placeholder="Describe what this criterion represents"
                value={form.description}
                onChange={(e) => form.setDescription(e.target.value)}
                disabled={isSubmitting}
                rows={2}
              />
            </div>

            {/* Unit (for Number type) */}
            {criterion.dataType === 'Number' && (
              <div className="space-y-2">
                <Label htmlFor="unit">Unit</Label>
                <Input
                  id="unit"
                  placeholder="e.g., kg, m², kW"
                  value={form.unit}
                  onChange={(e) => form.setUnit(e.target.value)}
                  disabled={isSubmitting}
                />
                <p className="text-xs text-muted-foreground">
                  Optional unit of measurement
                </p>
              </div>
            )}

            {/* Enum Values (for Enum type) */}
            {criterion.dataType === 'Enum' && (
              <EnumValueEditor
                values={form.enumValues}
                onChange={form.setEnumValues}
                disabled={isSubmitting}
                helpText="Warning: Removing values may cause validation errors for existing assignments"
              />
            )}

            {/* Applies To */}
            <div className="space-y-2">
              <Label>Applies to *</Label>
              <div className="flex gap-4">
                {RESOURCE_TYPE_OPTIONS.map(({ key, label }) => (
                  <div key={key} className="flex items-center gap-2">
                    <Checkbox
                      id={`edit-applies-to-${key}`}
                      checked={form.resourceTypeKeys.includes(key)}
                      onCheckedChange={(checked) => form.toggleResourceType(key, !!checked)}
                      disabled={isSubmitting}
                    />
                    <Label htmlFor={`edit-applies-to-${key}`} className="font-normal cursor-pointer">
                      {label}
                    </Label>
                  </div>
                ))}
              </div>
            </div>

            {/* Error Display */}
            <ErrorAlert message={error} />
          </div>

          <DialogFormFooter
            onCancel={() => onOpenChange(false)}
            isSubmitting={isSubmitting}
            submitLabel="Save Changes"
          />
        </form>
      </DialogContent>
    </Dialog>
  );
}
