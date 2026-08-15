import { Label } from '@foundation/src/components/ui/label';
import { ScalarValueInput } from '@foundation/src/components/fields/ScalarValueInput';
import { ListRowsEditor } from '@foundation/src/components/lists/ListRowsEditor';
import { useListDefinition } from '@foundation/src/hooks/useListDefinitions';
import { useResourceListInstance } from '@foundation/src/hooks/useListRows';
import type {
  CustomFieldValue,
  ResourceCustomField,
} from '@foundation/src/lib/api/resource-custom-fields-api';

interface CustomFieldInputProps {
  field: ResourceCustomField;
  value: CustomFieldValue;
  onChange: (value: CustomFieldValue) => void;
  /**
   * The resource being edited, or null while it is being created.
   *
   * Only a `list` field needs it: its rows hang off the resource, so there is nowhere to put
   * them until the resource exists. Null renders the list read-only with a note saying so.
   */
  resourceId?: string | null;
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
export function CustomFieldInput({ field, value, onChange, resourceId }: CustomFieldInputProps) {
  const inputId = `custom-field-${field.key}`;

  // A list is not a value: it renders its own rows, and `value`/`onChange` go unused.
  if (field.dataType === 'list') {
    return (
      <ListFieldInput field={field} resourceId={resourceId ?? null} labelId={inputId} />
    );
  }

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

/**
 * A list field: its own rows, edited in place and committed as they are entered.
 *
 * Rows do not travel with the resource form's save button — they are written the moment the user
 * adds one, which is why the list can be edited without the surrounding form being dirty. On a
 * resource that does not exist yet there is nothing to hang them off, so the list is read-only
 * and says when it becomes available.
 */
function ListFieldInput({
  field,
  resourceId,
  labelId,
}: {
  field: ResourceCustomField;
  resourceId: string | null;
  labelId: string;
}) {
  const { data: definition } = useListDefinition(field.listDefinitionId ?? null);
  const { instanceId, ensureInstanceId } = useResourceListInstance(resourceId, field.id);

  return (
    <div className="space-y-2">
      <Label id={labelId}>
        {field.label}
      </Label>
      {field.description && <p className="text-muted-foreground text-xs">{field.description}</p>}

      {resourceId === null ? (
        <p className="text-muted-foreground rounded-md border border-dashed p-3 text-sm">
          Rows can be added once this has been created.
        </p>
      ) : (
        <ListRowsEditor
          columns={definition?.columns ?? []}
          instanceId={instanceId}
          ensureInstanceId={ensureInstanceId}
          emptyMessage="No rows yet."
        />
      )}
    </div>
  );
}

/**
 * Whether a value counts as filled in — the shared test behind "required" and "can submit".
 *
 * A list field never has one: its rows are not part of the resource document, and it cannot be
 * required, so nothing asks this about one.
 */
export function hasCustomFieldValue(value: CustomFieldValue | undefined): boolean {
  if (value === null || value === undefined) return false;
  if (typeof value === 'string') return value.trim().length > 0;
  return true;
}
