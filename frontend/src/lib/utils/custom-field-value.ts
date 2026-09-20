import type { CustomFieldValue } from '@foundation/src/lib/api/resource-custom-fields-api';

/**
 * Whether a value counts as filled in — the shared test behind "required" and "can submit".
 *
 * A list field never has one: its rows are not part of the resource document, and it cannot be
 * required, so nothing asks this about one.
 */
export function hasCustomFieldValue(value: CustomFieldValue | undefined): boolean {
  if (value === null || value === undefined) return false;
  if (typeof value === 'string') return value.trim().length > 0;
  // An empty selection is unfilled, the same way an empty string is — the server agrees, so a
  // required lookup with nothing picked is refused rather than silently accepted.
  if (Array.isArray(value)) return value.length > 0;
  return true;
}
