import { useState } from 'react';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { ErrorAlert } from '@/components/ui/ErrorAlert';
import { DialogFormFooter } from '@/components/ui/DialogFormFooter';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import type { Criterion, CriterionDataType } from '@/types/criterion';
import { useCreateCriterion } from '@/hooks/useCriteria';
import { isValidSlug } from '@/lib/utils';
import { EnumValueEditor } from './EnumValueEditor';

interface CreateCriterionDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSuccess: (criterion: Criterion) => void;
}

export function CreateCriterionDialog({
  open,
  onOpenChange,
  onSuccess,
}: CreateCriterionDialogProps) {
  const createMutation = useCreateCriterion();
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [dataType, setDataType] = useState<CriterionDataType>('Boolean');
  const [unit, setUnit] = useState('');
  const [enumValues, setEnumValues] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
  const isSubmitting = createMutation.isPending;

  const resetForm = () => {
    setName('');
    setDescription('');
    setDataType('Boolean');
    setUnit('');
    setEnumValues([]);
    setError(null);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);

    // Validation
    if (!name.trim()) {
      setError('Name is required');
      return;
    }

    if (!isValidSlug(name)) {
      setError('Name must contain only alphanumeric characters, underscores, and hyphens');
      return;
    }

    if (dataType === 'Enum' && enumValues.length === 0) {
      setError('At least one enum value is required');
      return;
    }

    try {
      const newCriterion = await createMutation.mutateAsync({
        name: name.trim(),
        description: description.trim() || undefined,
        dataType,
        enumValues: dataType === 'Enum' ? enumValues : undefined,
        unit: dataType === 'Number' && unit.trim() ? unit.trim() : undefined,
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
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent className="sm:max-w-[500px]">
        <DialogHeader>
          <DialogTitle>Create Criterion</DialogTitle>
          <DialogDescription>
            Define a new criterion for evaluating spaces and requests.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit}>
          <div className="space-y-4 py-4">
            {/* Name */}
            <div className="space-y-2">
              <Label htmlFor="name">Name *</Label>
              <Input
                id="name"
                placeholder="e.g., max_load_kg, power_400v"
                value={name}
                onChange={(e) => setName(e.target.value)}
                disabled={isSubmitting}
              />
              <p className="text-xs text-muted-foreground">
                Use lowercase with underscores or hyphens (alphanumeric only)
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
            {dataType === 'Enum' && (
              <EnumValueEditor
                values={enumValues}
                onChange={setEnumValues}
                disabled={isSubmitting}
              />
            )}

            {/* Error Display */}
            <ErrorAlert message={error} />
          </div>

          <DialogFormFooter
            onCancel={() => handleOpenChange(false)}
            isSubmitting={isSubmitting}
            submitLabel="Create"
            submittingLabel="Creating..."
          />
        </form>
      </DialogContent>
    </Dialog>
  );
}
