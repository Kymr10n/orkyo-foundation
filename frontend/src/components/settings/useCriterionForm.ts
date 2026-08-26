import { useState, useCallback } from 'react';
import type { CriterionDataType } from '@foundation/src/types/criterion';

export interface CriterionFormState {
  description: string;
  unit: string;
  enumValues: string[];
  resourceTypeKeys: string[];
}

export interface UseCriterionFormResult {
  description: string;
  setDescription: (v: string) => void;
  unit: string;
  setUnit: (v: string) => void;
  enumValues: string[];
  setEnumValues: (v: string[]) => void;
  resourceTypeKeys: string[];
  setResourceTypeKeys: (v: string[]) => void;
  toggleResourceType: (key: string, checked: boolean) => void;
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
  const [resourceTypeKeys, setResourceTypeKeys] = useState<string[]>(
    initial?.resourceTypeKeys ?? [],
  );

  const toggleResourceType = useCallback((key: string, checked: boolean) => {
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
