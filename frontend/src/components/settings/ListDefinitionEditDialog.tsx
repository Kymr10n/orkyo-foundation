import { FormDialog } from '@foundation/src/components/ui/FormDialog';
import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import { Textarea } from '@foundation/src/components/ui/textarea';
import { Checkbox } from '@foundation/src/components/ui/checkbox';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@foundation/src/components/ui/select';
import { useEntityFormDialog } from '@foundation/src/hooks/useEntityFormDialog';
import {
  useCreateListDefinition,
  useListDefinition,
  useUpdateListDefinition,
} from '@foundation/src/hooks/useListDefinitions';
import { qk } from '@foundation/src/lib/api/query-keys';
import type { ListDefinition, ListDefinitionScope } from '@foundation/src/lib/api/lists-api';
import { useResourceTypes } from '@foundation/src/hooks/useResourceTypes';

interface ListDefinitionEditDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** The definition being edited, or null to create one. */
  definition: ListDefinition | null;
}

interface FormState {
  name: string;
  description: string;
  scope: ListDefinitionScope;
  /** Empty string means no owning type; only the `resource` scope uses it. */
  resourceTypeId: string;
  isActive: boolean;
  /** Empty string means "no designation", which the request sends as clearDisplayColumn. */
  displayColumnId: string;
}

/** Name, description and whether the definition still accepts new bindings. */
export function ListDefinitionEditDialog({
  open,
  onOpenChange,
  definition,
}: ListDefinitionEditDialogProps) {
  const { data: resourceTypes = [] } = useResourceTypes(true);
  const createDefinition = useCreateListDefinition();
  const updateDefinition = useUpdateListDefinition();
  // The collection response carries no columns, so the picker fetches the one definition. Only
  // while editing: a definition being created has no columns to choose from yet.
  const { data: loaded } = useListDefinition(open && definition ? definition.id : null);
  // row_ref is excluded: a reference is not a name, and designating one would make every row on
  // the list read as an empty cell. The server refuses it as well.
  const columns = (loaded?.columns ?? []).filter((c) => c.isActive && c.dataType !== 'row_ref');

  const { form, set, isDirty, error, submit, isSubmitting } = useEntityFormDialog<
    ListDefinition,
    FormState,
    unknown
  >({
    open,
    onOpenChange,
    entity: definition,
    emptyForm: () => ({
      name: '',
      description: '',
      scope: 'common' as ListDefinitionScope,
      resourceTypeId: '',
      isActive: true,
      displayColumnId: '',
    }),
    toForm: (entity) => ({
      name: entity.name,
      description: entity.description ?? '',
      scope: entity.scope,
      resourceTypeId: entity.resourceTypeId ?? '',
      isActive: entity.isActive,
      displayColumnId: entity.displayColumnId ?? '',
    }),
    save: (values, entity) =>
      entity
        ? updateDefinition.mutateAsync({
            definitionId: entity.id,
            request: {
              name: values.name.trim(),
              description: values.description.trim(),
              isActive: values.isActive,
              ...(values.displayColumnId
                ? { displayColumnId: values.displayColumnId }
                : { clearDisplayColumn: true }),
            },
          })
        : createDefinition.mutateAsync({
            name: values.name.trim(),
            description: values.description.trim() || undefined,
            scope: values.scope,
            // The server rejects a type on any scope but `resource`, so it is sent only there.
            ...(values.scope === 'resource' ? { resourceTypeId: values.resourceTypeId } : {}),
          }),
    entityLabel: 'List definition',
    invalidates: [qk.lists.all()],
  });

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title={definition ? 'Edit list definition' : 'New list definition'}
      description="A list definition is the shape a list takes — its columns and their types. Its scope says who owns it."
      error={error}
      onSubmit={submit}
      isSubmitting={isSubmitting}
      submitLabel={definition ? 'Save' : 'Create'}
      submitDisabled={
        form.name.trim().length === 0 ||
        (!definition && form.scope === 'resource' && !form.resourceTypeId)
      }
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

        {/* Ownership is fixed at creation. Moving a definition between scopes would change which
            names it must not collide with, and would orphan the surface it is edited from, so the
            selector is create-only. */}
        {!definition ? (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="list-definition-scope">Scope</Label>
              <Select
                value={form.scope}
                onValueChange={(v) =>
                  set({ scope: v as ListDefinitionScope, ...(v === 'resource' ? {} : { resourceTypeId: '' }) })
                }
              >
                <SelectTrigger id="list-definition-scope">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="resource">Resource type</SelectItem>
                  <SelectItem value="organization">Organization</SelectItem>
                  <SelectItem value="common">Common</SelectItem>
                </SelectContent>
              </Select>
            </div>
            {form.scope === 'resource' && (
              <div className="space-y-2">
                <Label htmlFor="list-definition-owner">
                  Resource type<span className="text-destructive ml-1">*</span>
                </Label>
                <Select
                  value={form.resourceTypeId || undefined}
                  onValueChange={(v) => set({ resourceTypeId: v })}
                >
                  <SelectTrigger id="list-definition-owner">
                    <SelectValue placeholder="Choose a type" />
                  </SelectTrigger>
                  <SelectContent>
                    {resourceTypes.map((t) => (
                      <SelectItem key={t.id} value={t.id}>
                        {t.displayNamePlural}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            )}
          </div>
        ) : null}

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

        {definition && columns.length > 0 && (
          <div className="space-y-2">
            <Label htmlFor="list-definition-display-column">Shown in forms</Label>
            <Select
              value={form.displayColumnId || 'none'}
              onValueChange={(v) => set({ displayColumnId: v === 'none' ? '' : v })}
            >
              <SelectTrigger id="list-definition-display-column">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="none">First column (default)</SelectItem>
                {columns.map((column) => (
                  <SelectItem key={column.id} value={column.id}>
                    {column.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <p className="text-muted-foreground text-xs">
              The column that names a row wherever one is shown as a single value — picking rows on
              a resource form, or a row's heading on a phone. Left at the default, the first column
              leads and the rest follow as context.
            </p>
          </div>
        )}

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
