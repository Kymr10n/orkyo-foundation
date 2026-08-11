import { Checkbox } from '@foundation/src/components/ui/checkbox';
import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import type {
  CustomFieldValue,
  ResourceCustomField,
} from '@foundation/src/lib/api/resource-custom-fields-api';

interface CustomFieldInputProps {
  field: ResourceCustomField;
  value: CustomFieldValue;
  onChange: (value: CustomFieldValue) => void;
}

/**
 * One custom field on the resource form, rendered for its data type.
 *
 * Not `CriterionRequirementInput`: that one models a *requirement* — it offers a comparison
 * operator and a unit, because a request asks for "capacity ≥ 4". A custom field is a plain
 * value the tenant records, and there is nothing to compare it to.
 */
export function CustomFieldInput({ field, value, onChange }: CustomFieldInputProps) {
  const inputId = `custom-field-${field.key}`;

  if (field.dataType === 'boolean') {
    return (
      <div className="space-y-2">
        <div className="flex items-center gap-2">
          <Checkbox
            id={inputId}
            checked={value === true}
            onCheckedChange={(c) => onChange(!!c)}
          />
          <Label htmlFor={inputId} className="cursor-pointer text-sm">
            {field.label}
            {field.isRequired && <span className="text-destructive ml-1">*</span>}
          </Label>
        </div>
        {field.description && (
          <p className="text-muted-foreground text-xs">{field.description}</p>
        )}
      </div>
    );
  }

  return (
    <div className="space-y-2">
      <Label htmlFor={inputId}>
        {field.label}
        {field.isRequired && <span className="text-destructive ml-1">*</span>}
      </Label>
      {field.dataType === 'number' ? (
        <Input
          id={inputId}
          type="number"
          value={typeof value === 'number' ? value : ''}
          // An empty box is an unfilled field, not the number zero.
          onChange={(e) => onChange(e.target.value === '' ? null : Number(e.target.value))}
          required={field.isRequired}
        />
      ) : (
        <Input
          id={inputId}
          type={field.dataType === 'date' ? 'date' : field.dataType === 'url' ? 'url' : 'text'}
          value={typeof value === 'string' ? value : ''}
          onChange={(e) => onChange(e.target.value)}
          maxLength={field.dataType === 'text' ? 2000 : undefined}
          placeholder={field.dataType === 'url' ? 'https://…' : undefined}
          required={field.isRequired}
        />
      )}
      {field.description && (
        <p className="text-muted-foreground text-xs">{field.description}</p>
      )}
    </div>
  );
}

/** Whether a value counts as filled in — the shared test behind "required" and "can submit". */
export function hasCustomFieldValue(value: CustomFieldValue | undefined): boolean {
  if (value === null || value === undefined) return false;
  if (typeof value === 'string') return value.trim().length > 0;
  return true;
}
