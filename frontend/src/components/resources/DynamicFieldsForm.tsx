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
import type { ResourceTypeFieldInfo } from '@foundation/src/lib/api/resource-types-api';

/** Custom field values as sent to the API: field key → value. */
export type ResourceMetadata = Record<string, unknown>;

/**
 * Renders a stored value as input text. Values arrive as `unknown` from the API, so
 * anything that isn't a primitive (which the server's validation rules out) becomes
 * empty rather than "[object Object]".
 */
export function toInputValue(value: unknown): string {
  if (value === undefined || value === null) return '';
  if (typeof value === 'string') return value;
  if (typeof value === 'number' || typeof value === 'boolean') return String(value);
  return '';
}

interface DynamicFieldsFormProps {
  fields: ResourceTypeFieldInfo[];
  values: ResourceMetadata;
  onChange: (values: ResourceMetadata) => void;
  disabled?: boolean;
}

/**
 * Renders one input per custom field definition. Constraints from the definition become
 * input hints (min/max, maxLength, choice lists); the server remains the authority and
 * re-validates everything on save.
 */
export function DynamicFieldsForm({
  fields,
  values,
  onChange,
  disabled = false,
}: DynamicFieldsFormProps) {
  if (fields.length === 0) return null;

  const setValue = (key: string, value: unknown) => {
    // An empty box means "no value" rather than an empty string, so the key is dropped.
    if (value === '' || value === undefined || value === null) {
      const { [key]: _removed, ...rest } = values;
      onChange(rest);
      return;
    }
    onChange({ ...values, [key]: value });
  };

  return (
    <div className="space-y-4">
      {fields.map((field) => {
        const id = `field-${field.key}`;
        const value = values[field.key];
        const label = (
          <Label htmlFor={id}>
            {field.label}
            {field.isRequired && <span aria-hidden="true"> *</span>}
          </Label>
        );

        if (field.dataType === 'boolean') {
          return (
            <div key={field.id} className="flex items-center justify-between">
              {label}
              <Switch
                id={id}
                checked={value === true}
                onCheckedChange={(checked) => setValue(field.key, checked)}
                disabled={disabled}
              />
            </div>
          );
        }

        if (field.dataType === 'select') {
          return (
            <div key={field.id} className="space-y-2">
              {label}
              <Select
                value={typeof value === 'string' ? value : ''}
                onValueChange={(next) => setValue(field.key, next)}
                disabled={disabled}
              >
                <SelectTrigger id={id}>
                  <SelectValue placeholder="Select…" />
                </SelectTrigger>
                <SelectContent>
                  {(field.options?.values ?? []).map((option) => (
                    <SelectItem key={option} value={option}>
                      {option}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              {field.description && (
                <p className="text-sm text-muted-foreground">{field.description}</p>
              )}
            </div>
          );
        }

        const inputType =
          field.dataType === 'number' ? 'number' : field.dataType === 'date' ? 'date' : 'text';

        return (
          <div key={field.id} className="space-y-2">
            {label}
            <Input
              id={id}
              type={inputType}
              value={toInputValue(value)}
              onChange={(e) => {
                const raw = e.target.value;
                if (field.dataType === 'number') {
                  // Keep the box clearable: only send a number once one is actually typed.
                  setValue(field.key, raw === '' ? '' : Number(raw));
                } else {
                  setValue(field.key, raw);
                }
              }}
              min={field.validation?.min}
              max={field.validation?.max}
              maxLength={field.dataType === 'text' ? field.validation?.maxLength : undefined}
              required={field.isRequired}
              disabled={disabled}
            />
            {field.description && (
              <p className="text-sm text-muted-foreground">{field.description}</p>
            )}
          </div>
        );
      })}
    </div>
  );
}

/** True when every active required field has a value — mirrors the server's create rule. */
export function hasRequiredValues(
  fields: ResourceTypeFieldInfo[],
  values: ResourceMetadata,
): boolean {
  return fields
    .filter((f) => f.isRequired)
    .every((f) => {
      const value = values[f.key];
      if (value === undefined || value === null) return false;
      return typeof value === 'string' ? value.trim().length > 0 : true;
    });
}
