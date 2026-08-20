import { Checkbox } from '@foundation/src/components/ui/checkbox';
import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@foundation/src/components/ui/select';

/** Every type a scalar value can take, across custom fields and list columns. */
export type ScalarDataType = 'text' | 'number' | 'boolean' | 'date' | 'url' | 'select';

/** A scalar value. `null` means unfilled, for every type. */
export type ScalarValue = string | number | boolean | null;

export interface ScalarValueInputProps {
  id: string;
  dataType: ScalarDataType;
  value: ScalarValue;
  onChange: (value: ScalarValue) => void;
  /** Options for `select`; ignored otherwise. */
  options?: string[] | null;
  required?: boolean;
  /** Rendered next to a checkbox, which has no separate label row of its own. */
  label?: string;
}

/**
 * One typed value input, chosen by data type.
 *
 * The frontend counterpart of the backend's CustomFieldValueRules: both places had a switch over
 * the same set of types, and keeping them in two files meant adding a type in one and forgetting
 * the other. Custom fields render through it, and so does every cell of a list row — which is
 * how a `date` behaves identically in both.
 *
 * Layout is the caller's business: this renders the control, and for `boolean` the inline label a
 * checkbox needs. Everything else (the label row, the description, spacing) belongs to whichever
 * form is hosting it.
 */
export function ScalarValueInput({
  id,
  dataType,
  value,
  onChange,
  options,
  required,
  label,
}: ScalarValueInputProps) {
  if (dataType === 'boolean') {
    return (
      <div className="flex items-center gap-2">
        <Checkbox id={id} checked={value === true} onCheckedChange={(c) => onChange(!!c)} />
        {label && (
          <Label htmlFor={id} className="cursor-pointer text-sm">
            {label}
            {required && <span className="text-destructive ml-1">*</span>}
          </Label>
        )}
      </div>
    );
  }

  if (dataType === 'select') {
    return (
      <Select
        value={typeof value === 'string' && value !== '' ? value : undefined}
        // Clearing a select yields null rather than an empty string, so "unfilled" is one value
        // everywhere instead of two that both have to be tested for.
        onValueChange={(next) => onChange(next === '' ? null : next)}
      >
        <SelectTrigger id={id}>
          <SelectValue placeholder="Choose…" />
        </SelectTrigger>
        <SelectContent>
          {(options ?? []).map((option) => (
            <SelectItem key={option} value={option}>
              {option}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    );
  }

  if (dataType === 'number') {
    return (
      <Input
        id={id}
        type="number"
        value={typeof value === 'number' ? value : ''}
        // An empty box is an unfilled field, not the number zero.
        onChange={(e) => onChange(e.target.value === '' ? null : Number(e.target.value))}
        required={required}
      />
    );
  }

  return (
    <Input
      id={id}
      type={dataType === 'date' ? 'date' : dataType === 'url' ? 'url' : 'text'}
      value={typeof value === 'string' ? value : ''}
      onChange={(e) => onChange(e.target.value)}
      maxLength={dataType === 'text' ? 2000 : undefined}
      placeholder={dataType === 'url' ? 'https://…' : undefined}
      required={required}
    />
  );
}
