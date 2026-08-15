import { FormDialog } from '@foundation/src/components/ui/FormDialog';
import { Checkbox } from '@foundation/src/components/ui/checkbox';
import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import { Textarea } from '@foundation/src/components/ui/textarea';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@foundation/src/components/ui/select';
import { useEntityFormDialog } from '@foundation/src/hooks/useEntityFormDialog';
import { CUSTOM_FIELD_INVALIDATES } from '@foundation/src/hooks/useResourceCustomFields';
import {
  createResourceCustomField,
  updateResourceCustomField,
  customFieldDataTypeLabel,
  CUSTOM_FIELD_DATA_TYPES,
  type CustomFieldDataType,
  type ResourceCustomField,
} from '@foundation/src/lib/api/resource-custom-fields-api';
import { keyFromLabel } from '@foundation/src/lib/key-from-label';
import { useListDefinitions } from '@foundation/src/hooks/useListDefinitions';

interface CustomFieldEditDialogProps {
  resourceTypeId: string;
  /** Null creates a new field; a field edits that one. */
  field: ResourceCustomField | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

interface FormState {
  key: string;
  label: string;
  description: string;
  dataType: CustomFieldDataType;
  listDefinitionId: string;
  isRequired: boolean;
  /** Raw input text: an emptied box is '' while retyping, not 0. */
  sortOrder: string;
  isActive: boolean;
}

/** Stable identifier inside every stored value document; mirrors the server rule. */
const KEY_PATTERN = /^[a-z][a-z0-9_]{0,49}$/;

export function CustomFieldEditDialog({
  resourceTypeId,
  field,
  open,
  onOpenChange,
}: CustomFieldEditDialogProps) {
  // Only active definitions: an inactive one is out of circulation and the server refuses a
  // new binding to it.
  const { data: listDefinitions = [] } = useListDefinitions();

  const { form, set, isDirty, error, submit, isSubmitting } = useEntityFormDialog<
    ResourceCustomField,
    FormState,
    ResourceCustomField
  >({
    open,
    onOpenChange,
    entity: field,
    emptyForm: () => ({
      key: '',
      label: '',
      description: '',
      dataType: 'text',
      listDefinitionId: '',
      isRequired: false,
      sortOrder: '0',
      isActive: true,
    }),
    toForm: (f) => ({
      key: f.key,
      label: f.label,
      description: f.description ?? '',
      dataType: f.dataType,
      listDefinitionId: f.listDefinitionId ?? '',
      isRequired: f.isRequired,
      sortOrder: String(f.sortOrder),
      isActive: f.isActive,
    }),
    save: (form, f) =>
      f
        ? updateResourceCustomField(resourceTypeId, f.id, {
            label: form.label,
            description: form.description || undefined,
            isRequired: form.isRequired,
            sortOrder: Number(form.sortOrder) || 0,
            isActive: form.isActive,
          })
        : createResourceCustomField(resourceTypeId, {
            key: form.key || keyFromLabel(form.label),
            label: form.label,
            description: form.description || undefined,
            dataType: form.dataType,
            // Only a list field carries a binding; the server rejects one on any other type.
            ...(form.dataType === 'list' ? { listDefinitionId: form.listDefinitionId } : {}),
            isRequired: form.isRequired,
            sortOrder: Number(form.sortOrder) || 0,
          }),
    entityLabel: 'custom field',
    invalidates: CUSTOM_FIELD_INVALIDATES(resourceTypeId),
  });

  const isEditing = field !== null;
  const effectiveKey = form.key || keyFromLabel(form.label);
  const keyIsValid = KEY_PATTERN.test(effectiveKey);
  const canSubmit = form.label.trim().length > 0 && (isEditing || keyIsValid);

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title={isEditing ? `Edit "${field.label}"` : 'New custom field'}
      description="A descriptive property shown on the resource form."
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
        <Label htmlFor="custom-field-label">Label</Label>
        <Input
          id="custom-field-label"
          value={form.label}
          onChange={(e) => set({ label: e.target.value })}
          maxLength={100}
          autoFocus
          required
        />
      </div>

      {/* The key names the value inside every resource's document, so it is fixed once a
          resource could be holding a value under it. */}
      {isEditing ? (
        <p className="text-muted-foreground text-xs">
          Key <code>{field.key}</code> and type{' '}
          <span className="font-medium">
            {customFieldDataTypeLabel(field.dataType)}
          </span>{' '}
          are fixed — resources already store values under them. Remove the field and add a new
          one to change either.
        </p>
      ) : (
        <>
          <div className="space-y-2">
            <Label htmlFor="custom-field-key">Key</Label>
            <Input
              id="custom-field-key"
              value={form.key}
              onChange={(e) => set({ key: e.target.value })}
              placeholder={keyFromLabel(form.label) || 'serial_number'}
              maxLength={50}
            />
            <p className="text-muted-foreground text-xs">
              {(form.key || form.label) && !keyIsValid
                ? 'Lowercase letters, numbers and underscores; must start with a letter.'
                : 'Used in exports and the API. Derived from the label when left blank, and fixed afterwards.'}
            </p>
          </div>

          <div className="space-y-2">
            <Label htmlFor="custom-field-type">Type</Label>
            <Select
              value={form.dataType}
              onValueChange={(v) => set({ dataType: v as CustomFieldDataType })}
            >
              <SelectTrigger id="custom-field-type">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {CUSTOM_FIELD_DATA_TYPES.map((o) => (
                  <SelectItem key={o.value} value={o.value}>
                    {o.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <p className="text-muted-foreground text-xs">
              {CUSTOM_FIELD_DATA_TYPES.find((o) => o.value === form.dataType)?.hint}
            </p>
          </div>

          {form.dataType === 'list' && (
            <div className="space-y-2">
              <Label htmlFor="custom-field-list-definition">
                List definition<span className="text-destructive ml-1">*</span>
              </Label>
              <Select
                value={form.listDefinitionId || undefined}
                onValueChange={(v) => set({ listDefinitionId: v })}
              >
                <SelectTrigger id="custom-field-list-definition">
                  <SelectValue placeholder="Choose a list definition…" />
                </SelectTrigger>
                <SelectContent>
                  {listDefinitions.map((definition) => (
                    <SelectItem key={definition.id} value={definition.id}>
                      {definition.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <p className="text-muted-foreground text-xs">
                {listDefinitions.length === 0
                  ? 'No list definitions yet — create one under Resources first.'
                  : 'The shape this list takes. Fixed once the field exists.'}
              </p>
            </div>
          )}
        </>
      )}

      <div className="space-y-2">
        <Label htmlFor="custom-field-description">Description</Label>
        <Textarea
          id="custom-field-description"
          value={form.description}
          onChange={(e) => set({ description: e.target.value })}
          maxLength={2000}
          rows={2}
          placeholder="Shown under the field on the resource form."
        />
      </div>

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <div className="space-y-2">
          <Label htmlFor="custom-field-order">Order</Label>
          <Input
            id="custom-field-order"
            type="number"
            value={form.sortOrder}
            onChange={(e) => set({ sortOrder: e.target.value })}
          />
          <p className="text-muted-foreground text-xs">Lower numbers appear first.</p>
        </div>
        <div className="space-y-3 pt-6">
          <div className="flex items-center gap-2">
            <Checkbox
              id="custom-field-required"
              checked={form.isRequired}
              onCheckedChange={(c) => set({ isRequired: !!c })}
            />
            <Label htmlFor="custom-field-required" className="cursor-pointer text-sm">
              Required
            </Label>
          </div>
          {isEditing && (
            <div className="flex items-center gap-2">
              <Checkbox
                id="custom-field-active"
                checked={form.isActive}
                onCheckedChange={(c) => set({ isActive: !!c })}
              />
              <Label htmlFor="custom-field-active" className="cursor-pointer text-sm">
                Shown on the form
              </Label>
            </div>
          )}
        </div>
      </div>

      {form.isRequired && (
        <p className="text-muted-foreground text-xs">
          Existing resources keep working, but the next person to edit one has to fill this in
          before they can save.
        </p>
      )}
    </FormDialog>
  );
}
