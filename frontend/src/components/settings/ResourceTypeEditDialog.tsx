import { FormDialog } from '@foundation/src/components/ui/FormDialog';
import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import { Textarea } from '@foundation/src/components/ui/textarea';
import {
  createResourceType,
  updateResourceType,
  type ResourceTypeInfo,
} from '@foundation/src/lib/api/resource-types-api';
import { useEntityFormDialog } from '@foundation/src/hooks/useEntityFormDialog';
import { RESOURCE_TYPE_INVALIDATES } from '@foundation/src/hooks/useResourceTypes';
import {
  RESOURCE_TYPE_ICONS,
  DEFAULT_RESOURCE_TYPE_ICON,
} from '@foundation/src/components/resources/resource-type-icon';
import { cn } from '@foundation/src/lib/utils';

interface ResourceTypeEditDialogProps {
  resourceType: ResourceTypeInfo | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSaved?: (type: ResourceTypeInfo) => void;
}

interface FormState {
  key: string;
  displayName: string;
  description: string;
  icon: string;
}

/** Rendered inline in the icon hint so "the default" is shown rather than named. */
function DefaultIconPreview() {
  return (
    <DEFAULT_RESOURCE_TYPE_ICON className="ml-1 inline h-4 w-4 align-text-bottom" />
  );
}

/** Keys are stable identifiers used in URLs and metadata documents; mirrors the server rule. */
const KEY_PATTERN = /^[a-z][a-z0-9_]{0,49}$/;

/** Suggests "company_car" from "Company Car" so the key is one less thing to think about. */
function toKey(displayName: string): string {
  return displayName
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '_')
    .replace(/^_+|_+$/g, '')
    .slice(0, 50);
}

export function ResourceTypeEditDialog({
  resourceType,
  open,
  onOpenChange,
  onSaved,
}: ResourceTypeEditDialogProps) {
  const { form, set, setForm, isDirty, error, submit, isSubmitting } = useEntityFormDialog<
    ResourceTypeInfo,
    FormState,
    ResourceTypeInfo
  >({
    open,
    onOpenChange,
    entity: resourceType,
    emptyForm: () => ({ key: '', displayName: '', description: '', icon: '' }),
    toForm: (rt) => ({
      key: rt.key,
      displayName: rt.displayName,
      description: rt.description ?? '',
      icon: rt.icon ?? '',
    }),
    save: (form, rt) =>
      rt
        ? updateResourceType(rt.id, {
            displayName: form.displayName,
            description: form.description || undefined,
            icon: form.icon || undefined,
          })
        : createResourceType({
            key: form.key,
            displayName: form.displayName,
            description: form.description || undefined,
            icon: form.icon || undefined,
          }),
    entityLabel: 'Resource type',
    invalidates: RESOURCE_TYPE_INVALIDATES,
    onSaved,
  });

  const isEdit = resourceType !== null;
  const keyValid = isEdit || KEY_PATTERN.test(form.key);
  const canSubmit = form.displayName.trim().length > 0 && keyValid;

  // In create mode the key tracks the display name until the user edits it directly.
  const handleDisplayNameChange = (displayName: string) => {
    setForm((prev) =>
      isEdit || prev.key !== toKey(prev.displayName)
        ? { ...prev, displayName }
        : { ...prev, displayName, key: toKey(displayName) },
    );
  };

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title={isEdit ? 'Edit Resource Type' : 'New Resource Type'}
      description="Resource types define what your organization manages — spaces, people, tools, or anything you add. Each type can carry its own custom fields."
      onSubmit={() => {
        if (canSubmit) submit();
      }}
      isSubmitting={isSubmitting}
      submitLabel="Save"
      submitDisabled={!canSubmit}
      error={error}
      dirty={isDirty}
    >
      <div className="space-y-2">
        <Label htmlFor="rt-display-name">Name</Label>
        <Input
          id="rt-display-name"
          value={form.displayName}
          onChange={(e) => handleDisplayNameChange(e.target.value)}
          maxLength={100}
          placeholder="Car"
          autoFocus
          required
        />
      </div>

      <div className="space-y-2">
        <Label htmlFor="rt-key">Key</Label>
        <Input
          id="rt-key"
          value={form.key}
          onChange={(e) => set({ key: e.target.value })}
          maxLength={50}
          placeholder="car"
          disabled={isEdit}
          aria-invalid={!keyValid}
        />
        <p className="text-sm text-muted-foreground">
          {isEdit
            ? 'The key identifies this type in links and stored data, so it cannot be changed.'
            : 'Lowercase letters, numbers, and underscores; must start with a letter.'}
        </p>
      </div>

      <div className="space-y-2">
        <Label>Icon</Label>
        <div
          role="radiogroup"
          aria-label="Icon"
          className="flex flex-wrap gap-1 rounded-md border p-2"
        >
          {Object.entries(RESOURCE_TYPE_ICONS).map(([name, Icon]) => {
            const selected = form.icon === name;
            return (
              <button
                key={name}
                type="button"
                role="radio"
                aria-checked={selected}
                aria-label={name}
                title={name}
                onClick={() => set({ icon: selected ? '' : name })}
                className={cn(
                  'flex h-9 w-9 items-center justify-center rounded-md border border-transparent text-muted-foreground',
                  'hover:bg-accent hover:text-accent-foreground',
                  selected && 'border-primary bg-accent text-accent-foreground',
                )}
              >
                <Icon className="h-4 w-4" />
              </button>
            );
          })}
        </div>
        <p className="text-sm text-muted-foreground">
          Shown in the sidebar and resource lists. Leave unset to use the default
          <DefaultIconPreview />
        </p>
      </div>

      <div className="space-y-2">
        <Label htmlFor="rt-description">Description</Label>
        <Textarea
          id="rt-description"
          value={form.description}
          onChange={(e) => set({ description: e.target.value })}
          maxLength={2000}
          rows={3}
        />
      </div>
    </FormDialog>
  );
}
