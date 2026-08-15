import { FormDialog } from '@foundation/src/components/ui/FormDialog';
import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import { Textarea } from '@foundation/src/components/ui/textarea';
import { Checkbox } from '@foundation/src/components/ui/checkbox';
import { useEntityFormDialog } from '@foundation/src/hooks/useEntityFormDialog';
import {
  useCreateListDefinition,
  useUpdateListDefinition,
} from '@foundation/src/hooks/useListDefinitions';
import { qk } from '@foundation/src/lib/api/query-keys';
import type { ListDefinition } from '@foundation/src/lib/api/lists-api';

interface ListDefinitionEditDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** The definition being edited, or null to create one. */
  definition: ListDefinition | null;
}

interface FormState {
  name: string;
  description: string;
  isActive: boolean;
}

/** Name, description and whether the definition still accepts new bindings. */
export function ListDefinitionEditDialog({
  open,
  onOpenChange,
  definition,
}: ListDefinitionEditDialogProps) {
  const createDefinition = useCreateListDefinition();
  const updateDefinition = useUpdateListDefinition();

  const { form, set, isDirty, error, submit, isSubmitting } = useEntityFormDialog<
    ListDefinition,
    FormState,
    unknown
  >({
    open,
    onOpenChange,
    entity: definition,
    emptyForm: () => ({ name: '', description: '', isActive: true }),
    toForm: (entity) => ({
      name: entity.name,
      description: entity.description ?? '',
      isActive: entity.isActive,
    }),
    save: (values, entity) =>
      entity
        ? updateDefinition.mutateAsync({
            definitionId: entity.id,
            request: {
              name: values.name.trim(),
              description: values.description.trim(),
              isActive: values.isActive,
            },
          })
        : createDefinition.mutateAsync({
            name: values.name.trim(),
            description: values.description.trim() || undefined,
          }),
    entityLabel: 'List definition',
    invalidates: [qk.lists.all()],
  });

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title={definition ? 'Edit list definition' : 'New list definition'}
      description="A list definition is the shape a list takes — its columns and their types. One definition can be used by many resource types."
      error={error}
      onSubmit={submit}
      isSubmitting={isSubmitting}
      submitLabel={definition ? 'Save' : 'Create'}
      submitDisabled={form.name.trim().length === 0}
      dirty={isDirty}
    >
      <div className="space-y-4">
        <div className="space-y-2">
          <Label htmlFor="list-definition-name">
            Name<span className="text-destructive ml-1">*</span>
          </Label>
          <Input
            id="list-definition-name"
            value={form.name}
            onChange={(e) => set({ name: e.target.value })}
            placeholder="Maintenance log"
            maxLength={100}
          />
        </div>

        <div className="space-y-2">
          <Label htmlFor="list-definition-description">Description</Label>
          <Textarea
            id="list-definition-description"
            value={form.description}
            onChange={(e) => set({ description: e.target.value })}
            placeholder="What this list records, and when to use it."
            rows={3}
          />
        </div>

        {definition && (
          <div className="space-y-2">
            <div className="flex items-center gap-2">
              <Checkbox
                id="list-definition-active"
                checked={form.isActive}
                onCheckedChange={(c) => set({ isActive: !!c })}
              />
              <Label htmlFor="list-definition-active" className="cursor-pointer text-sm">
                Available for new fields
              </Label>
            </div>
            {/* Deactivation is how a definition is taken out of circulation: deleting one that
                anything still uses is refused, so this is the path that always works. */}
            <p className="text-muted-foreground text-xs">
              Turning this off keeps existing lists working, but stops new fields from using this
              shape.
            </p>
          </div>
        )}
      </div>
    </FormDialog>
  );
}
