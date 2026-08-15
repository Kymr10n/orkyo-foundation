import { Label } from '@foundation/src/components/ui/label';
import { ScalarValueInput } from '@foundation/src/components/fields/ScalarValueInput';
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
 *
 * The control itself is `ScalarValueInput`, shared with list-row cells; what stays here is the
 * field-form chrome around it — the label row, the required marker and the description.
 */
export function CustomFieldInput({ field, value, onChange }: CustomFieldInputProps) {
  const inputId = `custom-field-${field.key}`;

  // A checkbox carries its own label inline, so it gets no label row above it.
  if (field.dataType === 'boolean') {
    return (
      <div className="space-y-2">
        <ScalarValueInput
          id={inputId}
          dataType="boolean"
          value={value}
          onChange={onChange}
          required={field.isRequired}
          label={field.label}
        />
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
      <ScalarValueInput
        id={inputId}
        dataType={field.dataType}
        value={value}
        onChange={onChange}
        required={field.isRequired}
      />
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
