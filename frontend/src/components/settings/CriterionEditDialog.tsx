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
import type { Criterion, CriterionDataType } from '@foundation/src/types/criterion';
import { useSaveCriterion, type CriterionDraft } from '@foundation/src/hooks/useCriteria';
import { useEntityFormDialog } from '@foundation/src/hooks/useEntityFormDialog';
import { EnumValueEditor } from './EnumValueEditor';
import { useResourceTypes } from '@foundation/src/hooks/useResourceTypes';

interface CriterionEditDialogProps {
  criterion: Criterion | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Optional: invoked with the saved entity on successful create or update. Used by inline-create flows. */
  onSaved?: (criterion: Criterion) => void;
  /** Pre-select a resource type when creating from a resource-domain page. Ignored when editing. */
  defaultResourceType?: string;
}

export function CriterionEditDialog({
  criterion,
  open,
  onOpenChange,
  onSaved,
  defaultResourceType,
}: CriterionEditDialogProps) {
  // Applicability targets are whatever types the tenant has defined, not a fixed list.
  const { data: resourceTypes = [] } = useResourceTypes(true);

  const mutation = useSaveCriterion();
  const { form, set, error, submit, isSubmitting } = useEntityFormDialog({
    open,
    onOpenChange,
    entity: criterion,
    // No preselected fallback type: nothing is built in, so an unseeded dialog starts
    // empty and validation requires an explicit choice.
    emptyForm: (): CriterionDraft => ({
      name: '',
      dataType: 'Boolean',
      description: '',
      unit: '',
      enumValues: [],
      resourceTypeKeys: defaultResourceType ? [defaultResourceType] : [],
    }),
    toForm: (c: Criterion): CriterionDraft => ({
      name: c.name,
      dataType: c.dataType,
      description: c.description ?? '',
      unit: c.unit ?? '',
      enumValues: [...(c.enumValues ?? [])],
      resourceTypeKeys: [...(c.resourceTypeKeys ?? [])],
    }),
    mutation,
    toVariables: (draft: CriterionDraft, c: Criterion | null) => ({ draft, criterion: c }),
    onSaved,
  });

  const dataTypeLocked = !!criterion?.inUse;

  const toggleResourceType = (key: string, checked: boolean) => {
    set({
      resourceTypeKeys: checked
        ? [...form.resourceTypeKeys, key]
        : form.resourceTypeKeys.filter((k) => k !== key),
    });
  };

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title={criterion ? 'Edit Criterion' : 'Create Criterion'}
      description={
        criterion
          ? 'Update the criterion details.'
          : 'Define a new criterion for evaluating resources and requests.'
      }
      onSubmit={submit}
      isSubmitting={isSubmitting}
      submitLabel={criterion ? 'Save Changes' : 'Create'}
      submittingLabel={criterion ? undefined : 'Creating…'}
      error={error}
    >
      {/* Name */}
      <div className="space-y-2">
        <Label htmlFor="name">Name *</Label>
        <Input
          id="name"
          placeholder="e.g., Project Management, Max Load (kg)"
          value={form.name}
          onChange={(e) => set({ name: e.target.value })}
          disabled={isSubmitting}
        />
      </div>

      {/* Data Type — editable until the criterion has values, then locked */}
      <div className="space-y-2">
        <Label htmlFor="dataType">Data Type{dataTypeLocked ? '' : ' *'}</Label>
        {dataTypeLocked ? (
          <>
            <div className="px-3 py-2 bg-muted rounded-md">
              <Badge variant="secondary">{form.dataType}</Badge>
            </div>
            <p className="text-xs text-amber-600 dark:text-amber-400">
              Data type is locked because this criterion has existing values
            </p>
          </>
        ) : (
          <Select
            value={form.dataType}
            onValueChange={(value) => set({ dataType: value as CriterionDataType })}
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
              <SelectItem value="Date">Date (calendar date)</SelectItem>
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
          onChange={(e) => set({ description: e.target.value })}
          disabled={isSubmitting}
          rows={2}
        />
      </div>

      {/* Unit (for Number type) */}
      {form.dataType === 'Number' && (
        <div className="space-y-2">
          <Label htmlFor="unit">Unit</Label>
          <Input
            id="unit"
            placeholder="e.g., kg, m², kW"
            value={form.unit}
            onChange={(e) => set({ unit: e.target.value })}
            disabled={isSubmitting}
          />
          <p className="text-xs text-muted-foreground">
            Optional unit of measurement
          </p>
        </div>
      )}

      {/* Enum Values (for Enum type) */}
      {form.dataType === 'Enum' && (
        <EnumValueEditor
          values={form.enumValues}
          onChange={(enumValues) => set({ enumValues })}
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
        <div className="flex flex-wrap gap-4">
          {resourceTypes.map(({ key, displayName }) => (
            <div key={key} className="flex items-center gap-2">
              <Checkbox
                id={`applies-to-${key}`}
                checked={form.resourceTypeKeys.includes(key)}
                onCheckedChange={(checked) => toggleResourceType(key, !!checked)}
                disabled={isSubmitting}
              />
              <Label htmlFor={`applies-to-${key}`} className="font-normal cursor-pointer">
                {displayName}
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
