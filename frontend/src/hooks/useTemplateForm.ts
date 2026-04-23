import { useEffect, useReducer } from "react";
import type { Criterion, CriterionValue } from "@foundation/src/types/criterion";
import type { Template } from "@foundation/src/types/templates";
import type { DurationUnit } from "@foundation/src/types/requests";

export interface TemplateFormState {
  name: string;
  description: string;
  durationValue: string;
  durationUnit: DurationUnit;
  requirements: Map<string, CriterionValue | null>;
}

type TemplateFormAction =
  | { type: "SET_FIELD"; field: keyof TemplateFormState; value: TemplateFormState[keyof TemplateFormState] }
  | { type: "ADD_REQUIREMENT"; criterionId: string; criterion: Criterion }
  | { type: "REMOVE_REQUIREMENT"; criterionId: string }
  | { type: "UPDATE_REQUIREMENT"; criterionId: string; value: CriterionValue | null }
  | { type: "LOAD_TEMPLATE"; template: Template }
  | { type: "RESET" };

/** @internal Exported for unit testing */
export function getDefaultValueForCriterion(criterion: Criterion): CriterionValue | null {
  if (criterion.dataType === "Boolean") {
    return false;
  }
  return null;
}

/** @internal Exported for unit testing */
export function templateFormReducer(
  state: TemplateFormState,
  action: TemplateFormAction
): TemplateFormState {
  switch (action.type) {
    case "SET_FIELD":
      return { ...state, [action.field]: action.value };

    case "ADD_REQUIREMENT": {
      const newRequirements = new Map(state.requirements);
      const defaultValue = getDefaultValueForCriterion(action.criterion);
      newRequirements.set(action.criterionId, defaultValue);
      return { ...state, requirements: newRequirements };
    }

    case "REMOVE_REQUIREMENT": {
      const newRequirements = new Map(state.requirements);
      newRequirements.delete(action.criterionId);
      return { ...state, requirements: newRequirements };
    }

    case "UPDATE_REQUIREMENT": {
      const newRequirements = new Map(state.requirements);
      newRequirements.set(action.criterionId, action.value);
      return { ...state, requirements: newRequirements };
    }

    case "LOAD_TEMPLATE": {
      const reqMap = new Map<string, CriterionValue | null>();
      if (action.template.items) {
        action.template.items.forEach((item) => {
          reqMap.set(item.criterionId, item.value);
        });
      }
      return {
        name: action.template.name,
        description: action.template.description || "",
        durationValue: action.template.durationValue?.toString() || "1",
        durationUnit: (action.template.durationUnit || "hours") as DurationUnit,
        requirements: reqMap,
      };
    }

    case "RESET":
      return {
        name: "",
        description: "",
        durationValue: "1",
        durationUnit: "days",
        requirements: new Map(),
      };

    default:
      return state;
  }
}

const initialState: TemplateFormState = {
  name: "",
  description: "",
  durationValue: "1",
  durationUnit: "days",
  requirements: new Map(),
};

export function useTemplateForm(template?: Template | null, open?: boolean) {
  const [state, dispatch] = useReducer(templateFormReducer, initialState);

  // Initialize or reset form when dialog opens/closes or template changes
  useEffect(() => {
    if (open) {
      if (template) {
        dispatch({ type: "LOAD_TEMPLATE", template });
      } else {
        dispatch({ type: "RESET" });
      }
    }
  }, [open, template]);

  const setField = (field: keyof TemplateFormState, value: TemplateFormState[keyof TemplateFormState]) => {
    dispatch({ type: "SET_FIELD", field, value });
  };

  const addRequirement = (criterionId: string, criterion: Criterion) => {
    dispatch({ type: "ADD_REQUIREMENT", criterionId, criterion });
  };

  const removeRequirement = (criterionId: string) => {
    dispatch({ type: "REMOVE_REQUIREMENT", criterionId });
  };

  const updateRequirement = (criterionId: string, value: CriterionValue | null) => {
    dispatch({ type: "UPDATE_REQUIREMENT", criterionId, value });
  };

  const reset = () => {
    dispatch({ type: "RESET" });
  };

  return {
    state,
    setField,
    addRequirement,
    removeRequirement,
    updateRequirement,
    reset,
  };
}
