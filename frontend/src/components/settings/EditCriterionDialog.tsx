import { useState, useEffect } from 'react';
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
import type { Criterion } from '@foundation/src/types/criterion';
import { useUpdateCriterion } from '@foundation/src/hooks/useCriteria';
import { EnumValueEditor } from './EnumValueEditor';

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
  const [description, setDescription] = useState('');
  const [unit, setUnit] = useState('');
  const [enumValues, setEnumValues] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
  const isSubmitting = updateMutation.isPending;

  // Initialize form when criterion changes
  useEffect(() => {
    if (criterion) {
      setDescription(criterion.description || '');
      setUnit(criterion.unit || '');
      setEnumValues(criterion.enumValues || []);
      setError(null);
    }
  }, [criterion]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);

    // Validation
    if (criterion.dataType === 'Enum' && enumValues.length === 0) {
      setError('At least one enum value is required');
      return;
    }

    try {
      const updated = await updateMutation.mutateAsync({
        id: criterion.id,
        data: {
          description: description.trim() || undefined,
          enumValues: criterion.dataType === 'Enum' ? enumValues : undefined,
          unit: criterion.dataType === 'Number' && unit.trim() ? unit.trim() : undefined,
        },
      });
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
                value={description}
                onChange={(e) => setDescription(e.target.value)}
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
                  value={unit}
                  onChange={(e) => setUnit(e.target.value)}
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
                values={enumValues}
                onChange={setEnumValues}
                disabled={isSubmitting}
                helpText="Warning: Removing values may cause validation errors for existing assignments"
              />
            )}

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
