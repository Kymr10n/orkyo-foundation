import { FormDialog } from '@foundation/src/components/ui/FormDialog';
import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import { Checkbox } from '@foundation/src/components/ui/checkbox';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@foundation/src/components/ui/select';
import { EnumValueEditor } from './EnumValueEditor';
import { useEntityFormDialog } from '@foundation/src/hooks/useEntityFormDialog';
import { useCreateListColumn, useUpdateListColumn } from '@foundation/src/hooks/useListDefinitions';
import { keyFromLabel } from '@foundation/src/lib/key-from-label';
import { qk } from '@foundation/src/lib/api/query-keys';
import {
  LIST_COLUMN_DATA_TYPES,
  type ListColumn,
  type ListColumnDataType,
} from '@foundation/src/lib/api/lists-api';

interface ListColumnEditDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  definitionId: string;
  /** The column being edited, or null to add one. */
  column: ListColumn | null;
}

interface FormState {
  key: string;
  label: string;
  description: string;
  dataType: ListColumnDataType;
  options: string[];
  isRequired: boolean;
  isActive: boolean;
}

/**
 * Add or edit one column of a list definition.
 *
 * Key and data type are fixed once the column exists, for the reason the migration gives: the key
 * is what every stored cell is filed under, and the type is how those cells are read. Both are
 * therefore only editable while creating.
 */
export function ListColumnEditDialog({
  open,
  onOpenChange,
  definitionId,
  column,
}: ListColumnEditDialogProps) {
  const createColumn = useCreateListColumn(definitionId);
  const updateColumn = useUpdateListColumn(definitionId);

  const { form, set, isDirty, error, submit, isSubmitting } = useEntityFormDialog<
    ListColumn,
    FormState,
    unknown
  >({
    open,
    onOpenChange,
    entity: column,
    emptyForm: () => ({
      key: '',
      label: '',
      description: '',
      dataType: 'text',
      options: [],
      isRequired: false,
      isActive: true,
    }),
    toForm: (entity) => ({
      key: entity.key,
      label: entity.label,
      description: entity.description ?? '',
      dataType: entity.dataType,
      options: entity.options ?? [],
      isRequired: entity.isRequired,
      isActive: entity.isActive,
    }),
    save: (values, entity) =>
      entity
        ? updateColumn.mutateAsync({
            columnId: entity.id,
            request: {
              label: values.label.trim(),
              description: values.description.trim(),
              isRequired: values.isRequired,
              isActive: values.isActive,
              ...(entity.dataType === 'select' ? { options: values.options } : {}),
            },
          })
        : createColumn.mutateAsync({
            key: values.key || keyFromLabel(values.label),
            label: values.label.trim(),
            description: values.description.trim() || undefined,
            dataType: values.dataType,
            ...(values.dataType === 'select' ? { options: values.options } : {}),
            isRequired: values.isRequired,
          }),
    entityLabel: 'Column',
    invalidates: [qk.lists.all()],
  });

  const isSelect = form.dataType === 'select';
  const effectiveKey = form.key || keyFromLabel(form.label);
  // A select with no options is a menu nothing can be chosen from; the server refuses it, so the
  // dialog does not offer to try.
  const submitDisabled =
    form.label.trim().length === 0 ||
    effectiveKey.length === 0 ||
    (isSelect && form.options.length === 0);

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title={column ? 'Edit column' : 'Add column'}
      error={error}
      onSubmit={submit}
      isSubmitting={isSubmitting}
      submitLabel={column ? 'Save' : 'Add'}
      submitDisabled={submitDisabled}
      dirty={isDirty}
    >
      <div className="space-y-4">
        <div className="space-y-2">
          <Label htmlFor="list-column-label">
            Label<span className="text-destructive ml-1">*</span>
          </Label>
          <Input
            id="list-column-label"
            value={form.label}
            onChange={(e) => set({ label: e.target.value })}
            placeholder="Mileage"
            maxLength={100}
          />
        </div>

        {!column && (
          <div className="space-y-2">
            <Label htmlFor="list-column-key">Key</Label>
            <Input
              id="list-column-key"
              value={form.key}
              onChange={(e) => set({ key: e.target.value })}
              placeholder={keyFromLabel(form.label) || 'mileage'}
              maxLength={50}
            />
            <p className="text-muted-foreground text-xs">
              Derived from the label unless you set it. Fixed once the column exists — it is what
              every stored value is filed under.
            </p>
          </div>
        )}

        <div className="space-y-2">
          <Label htmlFor="list-column-type">Type</Label>
          <Select
            value={form.dataType}
            onValueChange={(next) => set({ dataType: next as ListColumnDataType })}
            disabled={column !== null}
          >
            <SelectTrigger id="list-column-type">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {LIST_COLUMN_DATA_TYPES.map((type) => (
                <SelectItem key={type.value} value={type.value}>
                  {type.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <p className="text-muted-foreground text-xs">
            {column
              ? 'The type is fixed — changing it would reinterpret values already recorded.'
              : LIST_COLUMN_DATA_TYPES.find((t) => t.value === form.dataType)?.hint}
          </p>
        </div>

        {isSelect && (
          <div className="space-y-2">
            <Label>
              Options<span className="text-destructive ml-1">*</span>
            </Label>
            <EnumValueEditor
              values={form.options}
              onChange={(options) => set({ options })}
              helpText="The choices this column offers. Removing one leaves rows that already use it untouched."
            />
          </div>
        )}

        <div className="space-y-2">
          <Label htmlFor="list-column-description">Description</Label>
          <Input
            id="list-column-description"
            value={form.description}
            onChange={(e) => set({ description: e.target.value })}
            placeholder="Shown under the field in the row form."
          />
        </div>

        {/* Not offered for a row reference: the first row of a list has no other row to point
            at, so requiring one is a list nobody can add to. The server refuses it too. */}
        {form.dataType !== 'row_ref' && (
          <div className="flex items-center gap-2">
            <Checkbox
              id="list-column-required"
              checked={form.isRequired}
              onCheckedChange={(c) => set({ isRequired: !!c })}
            />
            <Label htmlFor="list-column-required" className="cursor-pointer text-sm">
              Required in every row
            </Label>
          </div>
        )}

        {column && (
          <div className="flex items-center gap-2">
            <Checkbox
              id="list-column-active"
              checked={form.isActive}
              onCheckedChange={(c) => set({ isActive: !!c })}
            />
            <Label htmlFor="list-column-active" className="cursor-pointer text-sm">
              Shown in the row form
            </Label>
          </div>
        )}
      </div>
    </FormDialog>
  );
}
