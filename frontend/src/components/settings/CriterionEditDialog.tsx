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
import type {
  Criterion,
  CriterionDataType,
  ResourceTypeKey,
  UpdateCriterionRequest,
} from '@foundation/src/types/criterion';
import {
  useCreateCriterion,
  useUpdateCriterion,
  useUpdateCriterionApplicability,
} from '@foundation/src/hooks/useCriteria';
import { EnumValueEditor } from './EnumValueEditor';
import {
  CRITERION_RESOURCE_TYPE_OPTIONS as RESOURCE_TYPE_OPTIONS,
  useCriterionForm,
} from './useCriterionForm';

interface CriterionEditDialogProps {
  criterion: Criterion | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Optional: invoked with the saved entity on successful create or update. Used by inline-create flows. */
  onSaved?: (criterion: Criterion) => void;
  /** Pre-select a resource type when creating from a resource-domain page. Ignored when editing. */
  defaultResourceType?: ResourceTypeKey;
}

export function CriterionEditDialog({
  criterion,
  open,
  onOpenChange,
  onSaved,
  defaultResourceType,
}: CriterionEditDialogProps) {
  const createMutation = useCreateCriterion();
  const updateMutation = useUpdateCriterion();
  const applicabilityMutation = useUpdateCriterionApplicability();
  const form = useCriterionForm();
  // Name and DataType are mutable (DataType only while not in use), so they live in
  // local state here rather than in the shared form hook.
  const [name, setName] = useState('');
  const [dataType, setDataType] = useState<CriterionDataType>('Boolean');
  const [error, setError] = useState<string | null>(null);
  const isSubmitting =
    createMutation.isPending || updateMutation.isPending || applicabilityMutation.isPending;
  const dataTypeLocked = !!criterion?.inUse;

  useEffect(() => {
    if (!open) return;
    setError(null);
    setName(criterion?.name ?? '');
    setDataType(criterion?.dataType ?? 'Boolean');
    form.reset({
      description: criterion?.description ?? '',
      unit: criterion?.unit ?? '',
      enumValues: criterion?.enumValues ?? [],
      resourceTypeKeys: criterion
        ? [...(criterion.resourceTypeKeys ?? [])]
        : [defaultResourceType ?? 'space'],
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [criterion, open, defaultResourceType]);

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
      if (!criterion) {
        const created = await createMutation.mutateAsync({
          name: name.trim(),
          description: form.description.trim() || undefined,
          dataType,
          enumValues: dataType === 'Enum' ? form.enumValues : undefined,
          unit: dataType === 'Number' && form.unit.trim() ? form.unit.trim() : undefined,
          resourceTypeKeys: form.resourceTypeKeys,
        });
        onSaved?.(created);
        onOpenChange(false);
        return;
      }

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

      onSaved?.(updated);
      onOpenChange(false);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save criterion');
    }
  };

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title={criterion ? 'Edit Criterion' : 'Create Criterion'}
      description={
        criterion
          ? 'Update the criterion details.'
          : 'Define a new criterion for evaluating spaces and requests.'
      }
      onSubmit={handleSubmit}
      isSubmitting={isSubmitting}
      submitLabel={criterion ? 'Save Changes' : 'Create'}
      submittingLabel={criterion ? undefined : 'Creating...'}
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
        <Label htmlFor="dataType">Data Type{dataTypeLocked ? '' : ' *'}</Label>
        {dataTypeLocked ? (
          <>
            <div className="px-3 py-2 bg-muted rounded-md">
              <Badge variant="secondary">{dataType}</Badge>
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
          helpText={
            criterion
              ? 'Warning: Removing values may cause validation errors for existing assignments'
              : undefined
          }
        />
      )}

      {/* Applies To */}
      <div className="space-y-2">
        <Label>Applies to *</Label>
        <div className="flex gap-4">
          {RESOURCE_TYPE_OPTIONS.map(({ key, label }) => (
            <div key={key} className="flex items-center gap-2">
              <Checkbox
                id={`applies-to-${key}`}
                checked={form.resourceTypeKeys.includes(key)}
                onCheckedChange={(checked) => form.toggleResourceType(key, !!checked)}
                disabled={isSubmitting}
              />
              <Label htmlFor={`applies-to-${key}`} className="font-normal cursor-pointer">
                {label}
              </Label>
            </div>
          ))}
        </div>
        <p className="text-xs text-muted-foreground">
          Select at least one resource domain this criterion applies to
        </p>
      </div>
    </FormDialog>
  );
}
