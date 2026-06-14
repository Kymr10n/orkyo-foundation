import { useState } from 'react';
import { FormDialog } from '@foundation/src/components/ui/FormDialog';
import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import { Textarea } from '@foundation/src/components/ui/textarea';
import { Checkbox } from '@foundation/src/components/ui/checkbox';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@foundation/src/components/ui/select';
import type { Criterion, CriterionDataType, ResourceTypeKey } from '@foundation/src/types/criterion';
import { useCreateCriterion } from '@foundation/src/hooks/useCriteria';
import { EnumValueEditor } from './EnumValueEditor';
import {
  CRITERION_RESOURCE_TYPE_OPTIONS as RESOURCE_TYPE_OPTIONS,
  useCriterionForm,
} from './useCriterionForm';

interface CreateCriterionDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSuccess: (criterion: Criterion) => void;
  /** Pre-select a resource type when opened from a resource-domain page. */
  defaultResourceType?: ResourceTypeKey;
}

export function CreateCriterionDialog({
  open,
  onOpenChange,
  onSuccess,
  defaultResourceType,
}: CreateCriterionDialogProps) {
  const createMutation = useCreateCriterion();
  const form = useCriterionForm({ resourceTypeKeys: [defaultResourceType ?? 'space'] });
  const [name, setName] = useState('');
  const [dataType, setDataType] = useState<CriterionDataType>('Boolean');
  const [error, setError] = useState<string | null>(null);
  const isSubmitting = createMutation.isPending;

  const resetForm = () => {
    setName('');
    setDataType('Boolean');
    form.reset({ resourceTypeKeys: [defaultResourceType ?? 'space'] });
    setError(null);
  };

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
      const newCriterion = await createMutation.mutateAsync({
        name: name.trim(),
        description: form.description.trim() || undefined,
        dataType,
        enumValues: dataType === 'Enum' ? form.enumValues : undefined,
        unit: dataType === 'Number' && form.unit.trim() ? form.unit.trim() : undefined,
        resourceTypeKeys: form.resourceTypeKeys,
      });
      onSuccess(newCriterion);
      resetForm();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create criterion');
    }
  };

  const handleOpenChange = (newOpen: boolean) => {
    if (!newOpen && !isSubmitting) {
      resetForm();
    }
    onOpenChange(newOpen);
  };

  return (
    <FormDialog
      open={open}
      onOpenChange={handleOpenChange}
      title="Create Criterion"
      description="Define a new criterion for evaluating spaces and requests."
      onSubmit={handleSubmit}
      isSubmitting={isSubmitting}
      submitLabel="Create"
      submittingLabel="Creating..."
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

      {/* Data Type */}
      <div className="space-y-2">
        <Label htmlFor="dataType">Data Type *</Label>
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
