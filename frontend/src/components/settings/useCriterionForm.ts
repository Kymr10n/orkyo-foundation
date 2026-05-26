import { useState, useEffect, useCallback } from 'react';
import type { CriterionDataType, ResourceTypeKey } from '@foundation/src/types/criterion';

export const CRITERION_RESOURCE_TYPE_OPTIONS: { key: ResourceTypeKey; label: string }[] = [
  { key: 'space', label: 'Spaces' },
  { key: 'person', label: 'People' },
  { key: 'tool', label: 'Tools' },
];

export interface CriterionFormState {
  description: string;
  unit: string;
  enumValues: string[];
  resourceTypeKeys: ResourceTypeKey[];
}

export interface UseCriterionFormResult {
  description: string;
  setDescription: (v: string) => void;
  unit: string;
  setUnit: (v: string) => void;
  enumValues: string[];
  setEnumValues: (v: string[]) => void;
  resourceTypeKeys: ResourceTypeKey[];
  setResourceTypeKeys: (v: ResourceTypeKey[]) => void;
  toggleResourceType: (key: ResourceTypeKey, checked: boolean) => void;
  /** Returns an error message if validation fails, else null. Pass the dataType in scope. */
  validate: (dataType: CriterionDataType) => string | null;
  reset: (init?: Partial<CriterionFormState>) => void;
}

const EMPTY: CriterionFormState = {
  description: '',
  unit: '',
  enumValues: [],
  resourceTypeKeys: [],
};

/**
 * Form state shared by CreateCriterionDialog and EditCriterionDialog.
 * The Name and DataType fields differ between create (mutable) and edit
 * (read-only), so they're not handled here.
 */
export function useCriterionForm(initial?: Partial<CriterionFormState>): UseCriterionFormResult {
  const [description, setDescription] = useState(initial?.description ?? '');
  const [unit, setUnit] = useState(initial?.unit ?? '');
  const [enumValues, setEnumValues] = useState<string[]>(initial?.enumValues ?? []);
  const [resourceTypeKeys, setResourceTypeKeys] = useState<ResourceTypeKey[]>(
    initial?.resourceTypeKeys ?? [],
  );

  const toggleResourceType = useCallback((key: ResourceTypeKey, checked: boolean) => {
    setResourceTypeKeys((prev) =>
      checked ? [...prev, key] : prev.filter((k) => k !== key),
    );
  }, []);

  const validate = useCallback(
    (dataType: CriterionDataType): string | null => {
      if (dataType === 'Enum' && enumValues.length === 0) {
        return 'At least one enum value is required';
      }
      if (resourceTypeKeys.length === 0) {
        return 'At least one applicability scope must be selected';
      }
      return null;
    },
    [enumValues, resourceTypeKeys],
  );

  const reset = useCallback((next?: Partial<CriterionFormState>) => {
    const v = { ...EMPTY, ...next };
    setDescription(v.description);
    setUnit(v.unit);
    setEnumValues(v.enumValues);
    setResourceTypeKeys(v.resourceTypeKeys);
  }, []);

  return {
    description,
    setDescription,
    unit,
    setUnit,
    enumValues,
    setEnumValues,
    resourceTypeKeys,
    setResourceTypeKeys,
    toggleResourceType,
    validate,
    reset,
  };
}

/**
 * Edit-dialog convenience: seed the form from the criterion on mount/change.
 */
export function useSeedCriterionForm(
  form: UseCriterionFormResult,
  source: {
    description?: string | null;
    unit?: string | null;
    enumValues?: string[] | null;
    resourceTypeKeys?: ResourceTypeKey[] | null;
  } | undefined,
) {
  // We deliberately depend on the source object identity to seed on prop change.
  useEffect(() => {
    if (!source) return;
    form.reset({
      description: source.description ?? '',
      unit: source.unit ?? '',
      enumValues: source.enumValues ?? [],
      resourceTypeKeys: [...(source.resourceTypeKeys ?? [])],
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [source]);
}
