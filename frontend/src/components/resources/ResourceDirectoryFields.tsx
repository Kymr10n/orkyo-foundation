import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import { Textarea } from '@foundation/src/components/ui/textarea';

export interface DirectoryFormValues {
  email: string;
  notes: string;
}

interface ResourceDirectoryFieldsProps {
  value: DirectoryFormValues;
  onChange: (patch: Partial<DirectoryFormValues>) => void;
}

/**
 * Email and notes — what a resource carries when its type declares `hasDirectoryProfile`.
 *
 * Job title and department used to live here too. Migration 1820 made them organization lists, so
 * they are ordinary `list_lookup` custom fields now and render through `CustomFieldInput` with
 * every other custom field. Nothing here special-cases them, which is the point: a tenant can add
 * a third organization lookup and it appears the same way.
 */
export function ResourceDirectoryFields({ value, onChange }: ResourceDirectoryFieldsProps) {
  return (
    <>
      <div className="space-y-2">
        <Label htmlFor="resource-email">Email</Label>
        <Input
          id="resource-email"
          type="email"
          value={value.email}
          onChange={(e) => onChange({ email: e.target.value })}
        />
      </div>

      <div className="space-y-2">
        <Label htmlFor="resource-notes">Notes</Label>
        <Textarea
          id="resource-notes"
          value={value.notes}
          onChange={(e) => onChange({ notes: e.target.value })}
          rows={2}
        />
      </div>
    </>
  );
}
