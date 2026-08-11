import { useResourceCustomFields } from './useResourceCustomFields';
import { hasCustomFieldValue } from '@foundation/src/components/resources/CustomFieldInput';
import type {
  CustomFieldValue,
  ResourceCustomField,
} from '@foundation/src/lib/api/resource-custom-fields-api';

export type CustomFieldValues = Record<string, CustomFieldValue>;

/**
 * The custom-field half of a resource form: which fields to ask for, and the rules for the
 * values behind them. Shared because every dialog that creates a resource needs the same
 * answers — the generic one, the person one, and whatever a type grows next — and getting
 * "required" or "empty" subtly different between them is how a form ends up unsaveable with
 * nothing on screen to fix.
 *
 * @param enabled false while the dialog is closed, so nothing is fetched until it is looked at.
 */
export function useResourceCustomFieldForm(resourceTypeId: string | undefined, enabled: boolean) {
  const {
    data: all = [],
    isLoading: queryLoading,
    isError,
  } = useResourceCustomFields(resourceTypeId ?? '', enabled && !!resourceTypeId);

  // A caller that resolves the type id from another query hands us undefined until that lands.
  // The query is correctly disabled meanwhile — but a disabled query reports isLoading false,
  // so without this the form would look like "this type has no required fields" and let a
  // submit through in the window before the id arrives.
  const isLoading = queryLoading || (enabled && !resourceTypeId);

  // Retired fields keep the values already captured, but stop being asked for.
  const fields = all.filter((f) => f.isActive);

  // A checkbox cannot show "unfilled": an untouched required boolean would disable Save with
  // nothing on screen to fix. "No" is an answer the server accepts, so show it as the standing
  // one — a required boolean then means "acknowledge this", not "you must tick it".
  const valueOf = (field: ResourceCustomField, values: CustomFieldValues): CustomFieldValue =>
    values[field.key] ?? (field.dataType === 'boolean' ? false : null);

  /**
   * An emptied field is an absent one, so clearing a box you never filled leaves no trace.
   *
   * Replaces in place rather than removing and re-appending: dirty-checking compares
   * JSON.stringify of the whole form, so moving a key to the end would make "edit a value,
   * then type the original back" read as an unsaved change forever.
   */
  const withValue = (
    values: CustomFieldValues,
    key: string,
    value: CustomFieldValue,
  ): CustomFieldValues => {
    if (!hasCustomFieldValue(value)) {
      return Object.fromEntries(Object.entries(values).filter(([k]) => k !== key));
    }
    return { ...values, [key]: value };
  };

  /** The required fields still waiting for an answer — what to name, and where to point. */
  const missingRequired = (values: CustomFieldValues) =>
    fields.filter((f) => f.isRequired && !hasCustomFieldValue(valueOf(f, values)));

  /**
   * Whether the form may be submitted. False while the definitions are still arriving: nothing
   * knows what is required then, and submitting would skip the check and come back as a raw
   * rejection from the server. An error blocks only when it left us with nothing — a failed
   * refetch that still has the previous definitions can be worked with, and locking the form
   * over it would strand an otherwise complete edit.
   */
  const isSatisfied = (values: CustomFieldValues): boolean =>
    !isLoading &&
    !(isError && fields.length === 0) &&
    missingRequired(values).length === 0;

  /** What the form shows is what gets saved — see valueOf on unticked required checkboxes. */
  const forSave = (values: CustomFieldValues): CustomFieldValues => {
    const filled = { ...values };
    for (const field of fields) {
      if (field.dataType === 'boolean' && field.isRequired && filled[field.key] === undefined) {
        filled[field.key] = false;
      }
    }
    return filled;
  };

  return { fields, isLoading, isError, valueOf, withValue, missingRequired, isSatisfied, forSave };
}
