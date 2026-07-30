import { FormDialog } from '@foundation/src/components/ui/FormDialog';
import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import { Switch } from '@foundation/src/components/ui/switch';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@foundation/src/components/ui/select';
import { EnumValueEditor } from './EnumValueEditor';
import {
  createResourceTypeField,
  updateResourceTypeField,
  type ResourceFieldDataType,
  type ResourceTypeFieldInfo,
} from '@foundation/src/lib/api/resource-types-api';
import { useEntityFormDialog } from '@foundation/src/hooks/useEntityFormDialog';
import { RESOURCE_TYPE_INVALIDATES } from '@foundation/src/hooks/useResourceTypes';

interface ResourceTypeFieldEditDialogProps {
  resourceTypeId: string;
  field: ResourceTypeFieldInfo | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

interface FormState {
  key: string;
  label: string;
  dataType: ResourceFieldDataType;
  isRequired: boolean;
  options: string[];
  /** Constraint inputs are kept as strings so a cleared box round-trips as "no constraint". */
  min: string;
  max: string;
  maxLength: string;
  regex: string;
}

const DATA_TYPE_LABELS: Record<ResourceFieldDataType, string> = {
  text: 'Text',
  number: 'Number',
  boolean: 'Yes / No',
  date: 'Date',
  select: 'Choice list',
};

const KEY_PATTERN = /^[a-z][a-z0-9_]{0,49}$/;

function toKey(label: string): string {
  return label
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '_')
    .replace(/^_+|_+$/g, '')
    .slice(0, 50);
}

/** Drops constraints that are blank or not meaningful for the chosen data type. */
function buildValidation(form: FormState) {
  const validation: Record<string, number | string> = {};
  if (form.dataType === 'number') {
    if (form.min.trim() !== '' && !Number.isNaN(Number(form.min))) validation.min = Number(form.min);
    if (form.max.trim() !== '' && !Number.isNaN(Number(form.max))) validation.max = Number(form.max);
  }
  if (form.dataType === 'text') {
    const maxLength = Number(form.maxLength);
    if (form.maxLength.trim() !== '' && Number.isInteger(maxLength) && maxLength > 0) {
      validation.maxLength = maxLength;
    }
    if (form.regex.trim() !== '') validation.regex = form.regex.trim();
  }
  return Object.keys(validation).length > 0 ? validation : undefined;
}

export function ResourceTypeFieldEditDialog({
  resourceTypeId,
  field,
  open,
  onOpenChange,
}: ResourceTypeFieldEditDialogProps) {
  const { form, set, setForm, isDirty, error, submit, isSubmitting } = useEntityFormDialog<
    ResourceTypeFieldInfo,
    FormState,
    ResourceTypeFieldInfo
  >({
    open,
    onOpenChange,
    entity: field,
    emptyForm: () => ({
      key: '',
      label: '',
      dataType: 'text',
      isRequired: false,
      options: [],
      min: '',
      max: '',
      maxLength: '',
      regex: '',
    }),
    toForm: (f) => ({
      key: f.key,
      label: f.label,
      dataType: f.dataType,
      isRequired: f.isRequired,
      options: f.options?.values ?? [],
      min: f.validation?.min?.toString() ?? '',
      max: f.validation?.max?.toString() ?? '',
      maxLength: f.validation?.maxLength?.toString() ?? '',
      regex: f.validation?.regex ?? '',
    }),
    save: (form, f) => {
      const payload = {
        label: form.label,
        isRequired: form.isRequired,
        options: form.dataType === 'select' ? { values: form.options } : undefined,
        validation: buildValidation(form),
      };
      return f
        ? updateResourceTypeField(resourceTypeId, f.id, payload)
        : createResourceTypeField(resourceTypeId, {
            ...payload,
            key: form.key,
            dataType: form.dataType,
          });
    },
    entityLabel: 'Field',
    invalidates: RESOURCE_TYPE_INVALIDATES,
  });

  const isEdit = field !== null;
  const keyValid = isEdit || KEY_PATTERN.test(form.key);
  const optionsValid = form.dataType !== 'select' || form.options.length > 0;
  const canSubmit = form.label.trim().length > 0 && keyValid && optionsValid;

  const handleLabelChange = (label: string) => {
    setForm((prev) =>
      isEdit || prev.key !== toKey(prev.label)
        ? { ...prev, label }
        : { ...prev, label, key: toKey(label) },
    );
  };

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title={isEdit ? 'Edit Field' : 'New Field'}
      description="Fields capture the details specific to this resource type, like mileage for a car or purchase date for a tool."
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
        <Label htmlFor="rtf-label">Label</Label>
        <Input
          id="rtf-label"
          value={form.label}
          onChange={(e) => handleLabelChange(e.target.value)}
          maxLength={100}
          placeholder="Mileage"
          autoFocus
          required
        />
      </div>

      <div className="space-y-2">
        <Label htmlFor="rtf-key">Key</Label>
        <Input
          id="rtf-key"
          value={form.key}
          onChange={(e) => set({ key: e.target.value })}
          maxLength={50}
          placeholder="mileage"
          disabled={isEdit}
          aria-invalid={!keyValid}
        />
        <p className="text-sm text-muted-foreground">
          {isEdit
            ? 'The key identifies stored values, so it cannot be changed.'
            : 'Lowercase letters, numbers, and underscores; must start with a letter.'}
        </p>
      </div>

      <div className="space-y-2">
        <Label htmlFor="rtf-data-type">Type</Label>
        <Select
          value={form.dataType}
          onValueChange={(value) => set({ dataType: value as ResourceFieldDataType })}
          disabled={isEdit}
        >
          <SelectTrigger id="rtf-data-type">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {(Object.keys(DATA_TYPE_LABELS) as ResourceFieldDataType[]).map((type) => (
              <SelectItem key={type} value={type}>
                {DATA_TYPE_LABELS[type]}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        {isEdit && (
          <p className="text-sm text-muted-foreground">
            Changing the type would invalidate values already recorded. Deactivate this field and add
            a new one instead.
          </p>
        )}
      </div>

      {form.dataType === 'select' && (
        <EnumValueEditor
          values={form.options}
          onChange={(values) => set({ options: values })}
          helpText="The choices offered for this field. Add at least one."
        />
      )}

      {form.dataType === 'number' && (
        <div className="grid grid-cols-2 gap-4">
          <div className="space-y-2">
            <Label htmlFor="rtf-min">Minimum</Label>
            <Input
              id="rtf-min"
              type="number"
              value={form.min}
              onChange={(e) => set({ min: e.target.value })}
            />
          </div>
          <div className="space-y-2">
            <Label htmlFor="rtf-max">Maximum</Label>
            <Input
              id="rtf-max"
              type="number"
              value={form.max}
              onChange={(e) => set({ max: e.target.value })}
            />
          </div>
        </div>
      )}

      {form.dataType === 'text' && (
        <div className="space-y-2">
          <Label htmlFor="rtf-max-length">Maximum length</Label>
          <Input
            id="rtf-max-length"
            type="number"
            min={1}
            value={form.maxLength}
            onChange={(e) => set({ maxLength: e.target.value })}
          />
        </div>
      )}

      <div className="flex items-center justify-between">
        <Label htmlFor="rtf-required">Required</Label>
        <Switch
          id="rtf-required"
          checked={form.isRequired}
          onCheckedChange={(checked) => set({ isRequired: checked })}
        />
      </div>
    </FormDialog>
  );
}
