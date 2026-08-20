import { FormDialog } from '@foundation/src/components/ui/FormDialog';
import { Checkbox } from '@foundation/src/components/ui/checkbox';
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
  displayNamePlural: string;
  description: string;
  icon: string;
  hasGeometry: boolean;
  hasDirectoryProfile: boolean;
  singleGroupMembership: boolean;
  isActive: boolean;
}

/**
 * What a type's resources can do. Stored on the type rather than inferred from its key, which
 * is what lets a tenant-defined type behave like a built-in one. Locked for system types: the
 * product's own Spaces and People pages are built on these, so a tenant could break a page
 * they cannot repair.
 */
const BEHAVIOUR_FLAGS = [
  {
    field: 'hasGeometry',
    label: 'Can be placed on a floorplan',
    hint: 'Adds a code, a capacity and a drawable shape. Its site owns it and it cannot travel.',
  },
  {
    field: 'hasDirectoryProfile',
    label: 'Has directory details',
    hint: 'Adds an email, job title and department, and can be linked to a user account.',
  },
  {
    field: 'singleGroupMembership',
    label: 'Belongs to one group at a time',
    hint: 'Adding it to a second group moves it, rather than listing it in both.',
  },
] as const satisfies readonly { field: keyof FormState; label: string; hint: string }[];

/** Rendered inline in the icon hint so "the default" is shown rather than named. */
function DefaultIconPreview() {
  return (
    <DEFAULT_RESOURCE_TYPE_ICON className="ml-1 inline h-4 w-4 align-text-bottom" />
  );
}

/** Keys are stable identifiers used in URLs and metadata documents; mirrors the server rule. */
const KEY_PATTERN = /^[a-z][a-z0-9_]{0,49}$/;

/** Starting guess for the plural, editable — English is irregular and tenants aren't all English. */
function defaultPlural(displayName: string): string {
  return displayName.trim() ? `${displayName}s` : '';
}

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
    emptyForm: () => ({
      key: '', displayName: '', displayNamePlural: '', description: '', icon: '',
      hasGeometry: false, hasDirectoryProfile: false, singleGroupMembership: false,
      isActive: true,
    }),
    toForm: (rt) => ({
      key: rt.key,
      displayName: rt.displayName,
      displayNamePlural: rt.displayNamePlural,
      description: rt.description ?? '',
      icon: rt.icon ?? '',
      hasGeometry: rt.hasGeometry,
      hasDirectoryProfile: rt.hasDirectoryProfile,
      singleGroupMembership: rt.singleGroupMembership,
      isActive: rt.isActive,
    }),
    save: (form, rt) =>
      rt
        ? updateResourceType(rt.id, {
            displayName: form.displayName,
            displayNamePlural: form.displayNamePlural,
            description: form.description || undefined,
            icon: form.icon || undefined,
            hasGeometry: form.hasGeometry,
            hasDirectoryProfile: form.hasDirectoryProfile,
            singleGroupMembership: form.singleGroupMembership,
            isActive: form.isActive,
          })
        : createResourceType({
            key: form.key,
            displayName: form.displayName,
            displayNamePlural: form.displayNamePlural,
            description: form.description || undefined,
            icon: form.icon || undefined,
            hasGeometry: form.hasGeometry,
            hasDirectoryProfile: form.hasDirectoryProfile,
            singleGroupMembership: form.singleGroupMembership,
          }),
    entityLabel: 'Resource type',
    invalidates: RESOURCE_TYPE_INVALIDATES,
    onSaved,
  });

  const isEdit = resourceType !== null;
  const keyValid = isEdit || KEY_PATTERN.test(form.key);
  const canSubmit =
    form.displayName.trim().length > 0 && form.displayNamePlural.trim().length > 0 && keyValid;

  // In create mode the key tracks the display name until the user edits it directly.
  const handleDisplayNameChange = (displayName: string) => {
    setForm((prev) => {
      const next = { ...prev, displayName };
      // Both the key and the plural track the name until the user edits them directly, so the
      // common case is one field. "s" is only a starting guess — irregular nouns and other
      // languages are exactly why the plural is stored rather than derived.
      if (!isEdit && prev.key === toKey(prev.displayName)) next.key = toKey(displayName);
      if (!isEdit && prev.displayNamePlural === defaultPlural(prev.displayName)) {
        next.displayNamePlural = defaultPlural(displayName);
      }
      return next;
    });
  };

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title={isEdit ? 'Edit Resource Type' : 'New Resource Type'}
      description="Resource types define what your organization manages — machines, people, tools, or anything you add. Describe each type with criteria."
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
        <Label htmlFor="rt-display-name-plural">Plural name</Label>
        <Input
          id="rt-display-name-plural"
          value={form.displayNamePlural}
          onChange={(e) => set({ displayNamePlural: e.target.value })}
          maxLength={100}
          placeholder="Cars"
          required
        />
        <p className="text-sm text-muted-foreground">
          Used wherever a list of them is named — the sidebar, the utilization tab, the page title.
        </p>
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

      <fieldset className="space-y-3">
        <legend className="text-sm font-medium">What these can do</legend>
        {BEHAVIOUR_FLAGS.map(({ field, label, hint }) => (
          <div key={field} className="flex items-start gap-2">
            <Checkbox
              id={`rt-${field}`}
              checked={form[field]}
              onCheckedChange={(checked) => set({ [field]: checked === true })}
            />
            <div className="grid gap-1 leading-none">
              <Label htmlFor={`rt-${field}`} className="font-normal">{label}</Label>
              <p className="text-sm text-muted-foreground">{hint}</p>
            </div>
          </div>
        ))}
      </fieldset>

      {/* Edit-only: this is the only way here to reactivate a type the server
          auto-deactivated when it was deleted while still in use. */}
      {isEdit && (
        <div className="flex items-start gap-2">
          <Checkbox
            id="rt-is-active"
            checked={form.isActive}
            onCheckedChange={(checked) => set({ isActive: checked === true })}
          />
          <div className="grid gap-1 leading-none">
            <Label htmlFor="rt-is-active" className="font-normal">Active</Label>
            <p className="text-sm text-muted-foreground">
              Inactive types stay in the catalogue so existing resources keep working,
              but they are hidden from pickers and cannot receive new resources.
            </p>
          </div>
        </div>
      )}
    </FormDialog>
  );
}
