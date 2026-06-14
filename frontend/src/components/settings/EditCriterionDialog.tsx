import { useEffect, useState } from 'react';
import { FormDialog } from '@foundation/src/components/ui/FormDialog';
import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import { Textarea } from '@foundation/src/components/ui/textarea';
import { Badge } from '@foundation/src/components/ui/badge';
import { Checkbox } from '@foundation/src/components/ui/checkbox';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@foundation/src/components/ui/select';
import type { Criterion, CriterionDataType, UpdateCriterionRequest } from '@foundation/src/types/criterion';
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
  // Name and DataType are mutable on edit (DataType only while not in use), so they live in
  // local state here rather than in the shared form hook — mirrors CreateCriterionDialog.
  const [name, setName] = useState(criterion.name);
  const [dataType, setDataType] = useState<CriterionDataType>(criterion.dataType);
  const [error, setError] = useState<string | null>(null);
  const isSubmitting = updateMutation.isPending || applicabilityMutation.isPending;

  useSeedCriterionForm(form, criterion);
  useEffect(() => {
    setName(criterion.name);
    setDataType(criterion.dataType);
  }, [criterion]);

  const handleSubmit = async () => {
    setError(null);

    if (!name.trim()) {
      setError('Name is required');
      return;
    }
    const validationError = form.validate(dataType);
    if (validationError) {
      setError(validationError);
      return;
    }

    try {
      const detailData: UpdateCriterionRequest = {
        description: form.description.trim() || undefined,
        enumValues: dataType === 'Enum' ? form.enumValues : undefined,
        unit: dataType === 'Number' && form.unit.trim() ? form.unit.trim() : undefined,
      };
      // Name and DataType are sent only when actually changed.
      if (name.trim() !== criterion.name) detailData.name = name.trim();
      if (dataType !== criterion.dataType) detailData.dataType = dataType;

      // Only PUT the criterion when there's a detail field to update — otherwise the
      // backend rejects an empty update with 400 "No fields to update" (e.g. a Boolean
      // criterion with no description, where the user only changed applicability).
      const hasDetailChanges =
        detailData.name !== undefined ||
        detailData.dataType !== undefined ||
        detailData.description !== undefined ||
        detailData.enumValues !== undefined ||
        detailData.unit !== undefined;

      let updated = criterion;
      if (hasDetailChanges) {
        updated = await updateMutation.mutateAsync({ id: criterion.id, data: detailData });
      }

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
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title="Edit Criterion"
      description="Update the criterion details."
      onSubmit={handleSubmit}
      isSubmitting={isSubmitting}
      submitLabel="Save Changes"
      error={error}
    >
      {/* Name */}
      <div className="space-y-2">
        <Label htmlFor="name">Name *</Label>
        <Input
          id="name"
          placeholder="e.g., Project Management, Max Load (kg)"
          value={name}
          onChange={(e) => setName(e.target.value)}
          disabled={isSubmitting}
        />
      </div>

      {/* Data Type — editable until the criterion has values, then locked */}
      <div className="space-y-2">
        <Label htmlFor="dataType">Data Type{criterion.inUse ? '' : ' *'}</Label>
        {criterion.inUse ? (
          <>
            <div className="px-3 py-2 bg-muted rounded-md">
              <Badge variant="secondary">{criterion.dataType}</Badge>
            </div>
            <p className="text-xs text-amber-600 dark:text-amber-400">
              Data type is locked because this criterion has existing values
            </p>
          </>
        ) : (
          <Select
            value={dataType}
            onValueChange={(value) => setDataType(value as CriterionDataType)}
            disabled={isSubmitting}
          >
            <SelectTrigger id="dataType">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="Boolean">Boolean (true/false)</SelectItem>
              <SelectItem value="Number">Number (numeric value)</SelectItem>
              <SelectItem value="String">String (text)</SelectItem>
              <SelectItem value="Enum">Enum (predefined options)</SelectItem>
            </SelectContent>
          </Select>
        )}
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
      {dataType === 'Number' && (
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
      {dataType === 'Enum' && (
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
    </FormDialog>
  );
}
