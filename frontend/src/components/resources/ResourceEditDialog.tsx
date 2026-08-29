import { Checkbox } from '@foundation/src/components/ui/checkbox';
import { FormDialog } from '@foundation/src/components/ui/FormDialog';
import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@foundation/src/components/ui/select';
import { Textarea } from '@foundation/src/components/ui/textarea';
import {
  createResource,
  updateResource,
  type ResourceInfo,
} from '@foundation/src/lib/api/resources-api';
import { qk } from '@foundation/src/lib/api/query-keys';
import { useEntityFormDialog } from '@foundation/src/hooks/useEntityFormDialog';
import { useIsMultiSite, useSites } from '@foundation/src/hooks/useSites';
import { ALLOCATION_MODE } from '@foundation/src/constants/allocation-mode';
import type { ResourceTypeInfo } from '@foundation/src/lib/api/resource-types-api';
import type { CustomFieldValue } from '@foundation/src/lib/api/resource-custom-fields-api';
import { useResourceCustomFieldForm } from '@foundation/src/hooks/useResourceCustomFieldForm';
import { ErrorAlert } from '@foundation/src/components/ui/ErrorAlert';
import { CustomFieldInput } from './CustomFieldInput';
import { ResourceDirectoryFields, type DirectoryFormValues } from './ResourceDirectoryFields';
import { isValidEmail } from '@foundation/src/lib/utils/validation';

interface ResourceEditDialogProps {
  resourceType: ResourceTypeInfo;
  resource: ResourceInfo | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

interface FormState extends DirectoryFormValues {
  name: string;
  description: string;
  externalReference: string;
  allocationMode: string;
  baseAvailabilityPercent: number;
  /** Empty string = unset. Select cannot hold an empty value, hence the sentinel below. */
  homeSiteId: string;
  crossSiteAllowed: boolean;
  /**
   * The resource's whole custom-field document, not just the fields on screen. Values for
   * retired fields ride along untouched, because a save replaces the document wholesale and
   * anything left out of it is discarded.
   */
  customFields: Record<string, CustomFieldValue>;
}

/** Radix Select rejects an empty item value, so "no home site" needs a stand-in. */
const SITE_UNSET = '__unset_site__';

export function ResourceEditDialog({
  resourceType,
  resource,
  open,
  onOpenChange,
}: ResourceEditDialogProps) {
  const { data: sites = [] } = useSites();
  const isMultiSite = useIsMultiSite();
  // A placeable resource is anchored: its shape belongs to one floorplan, and scheduling
  // rules pick the first resource that cannot travel to decide where work happens. So
  // "available for other sites" is not a choice for these types — the server rejects the
  // combination outright — and offering the checkbox only produces an unexplained 400.
  const isPlaceable = resourceType.hasGeometry;
  // Directory types carry email, job title, department and notes. The backend holds them on the
  // generic resource contract and rejects them for any other type, so the flag gates both.
  const hasDirectory = resourceType.hasDirectoryProfile;

  const customFields = useResourceCustomFieldForm(resourceType.id, open);

  const { form, set, isDirty, error, submit, isSubmitting } = useEntityFormDialog<
    ResourceInfo,
    FormState,
    ResourceInfo
  >({
    open,
    onOpenChange,
    entity: resource,
    emptyForm: () => ({
      name: '',
      description: '',
      externalReference: '',
      // Exclusive matches the default for physical, one-at-a-time resources.
      allocationMode: ALLOCATION_MODE.EXCLUSIVE,
      baseAvailabilityPercent: 100,
      homeSiteId: '',
      crossSiteAllowed: !isPlaceable,
      customFields: {},
      email: '',
      notes: '',
    }),
    toForm: (r) => ({
      name: r.name,
      description: r.description ?? '',
      externalReference: r.externalReference ?? '',
      allocationMode: r.allocationMode ?? ALLOCATION_MODE.EXCLUSIVE,
      baseAvailabilityPercent: r.baseAvailabilityPercent ?? 100,
      homeSiteId: r.homeSiteId ?? '',
      crossSiteAllowed: isPlaceable ? false : (r.crossSiteAllowed ?? true),
      customFields: { ...(r.customFields ?? {}) },
      email: r.email ?? '',
      notes: r.notes ?? '',
    }),
    save: (form, r) => {
      const fields = {
        name: form.name,
        description: form.description || undefined,
        externalReference: form.externalReference || undefined,
        allocationMode: form.allocationMode,
        baseAvailabilityPercent: form.baseAvailabilityPercent,
        homeSiteId: form.homeSiteId || null,
        crossSiteAllowed: isPlaceable ? false : form.crossSiteAllowed,
        customFields: customFields.forSave(form.customFields),
        // Sent only for a directory type. The backend rejects these fields on any other type,
        // so a stray empty string would turn every save into a 400.
        ...(hasDirectory
          ? {
              email: form.email || null,
              notes: form.notes || null,
            }
          : {}),
      };
      return r
        ? updateResource(r.id, fields)
        : createResource({ resourceTypeKey: resourceType.key, ...fields });
    },
    entityLabel: resourceType.displayName,
    invalidates: [qk.resources.byType(resourceType.key), qk.resources.allFlat()],
  });

  const setCustomField = (key: string, value: CustomFieldValue) =>
    set({ customFields: customFields.withValue(form.customFields, key, value) });

  const emailInvalid = hasDirectory && !!form.email && !isValidEmail(form.email);
  const canSubmit =
    form.name.trim().length > 0 && customFields.isSatisfied(form.customFields) && !emailInvalid;

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      // A list field renders a full data table, which does not fit the default form width and
      // ends up scrolling sideways inside the dialog. Widen only for the types that have one:
      // most resource types do not, and a permanently wide dialog leaves single-column inputs
      // stranded across 720px. `xl` matches ResourceTypeCustomFieldsDialog, the other table.
      size={customFields.fields.some((f) => f.dataType === 'list') ? 'xl' : 'md'}
      title={resource ? `Edit ${resourceType.displayName}` : `New ${resourceType.displayName}`}
      description={resourceType.description || undefined}
      srOnlyDescription={!resourceType.description}
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
        <Label htmlFor="resource-name">Name</Label>
        <Input
          id="resource-name"
          value={form.name}
          onChange={(e) => set({ name: e.target.value })}
          maxLength={255}
          autoFocus
          required
        />
      </div>

      <div className="space-y-2">
        <Label htmlFor="resource-description">Description</Label>
        <Textarea
          id="resource-description"
          value={form.description}
          onChange={(e) => set({ description: e.target.value })}
          maxLength={2000}
          rows={3}
        />
      </div>

      <div className="space-y-2">
        <Label htmlFor="resource-external-ref">External reference</Label>
        <Input
          id="resource-external-ref"
          value={form.externalReference}
          onChange={(e) => set({ externalReference: e.target.value })}
          maxLength={255}
          placeholder="Asset tag, serial number, ERP id…"
        />
      </div>

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <div className="min-w-0 space-y-2">
          <Label htmlFor="resource-allocation">Allocation Mode</Label>
          <Select
            value={form.allocationMode}
            onValueChange={(v) => set({ allocationMode: v })}
          >
            <SelectTrigger id="resource-allocation">
              <SelectValue placeholder="Select allocation mode" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ALLOCATION_MODE.EXCLUSIVE}>Exclusive</SelectItem>
              <SelectItem value={ALLOCATION_MODE.FRACTIONAL}>Fractional</SelectItem>
              <SelectItem value={ALLOCATION_MODE.CONCURRENT_CAPACITY} disabled>
                Concurrent Capacity (not yet supported)
              </SelectItem>
            </SelectContent>
          </Select>
        </div>
        <div className="min-w-0 space-y-2">
          <Label htmlFor="resource-availability">Base Availability (%)</Label>
          <Input
            id="resource-availability"
            type="number"
            value={form.baseAvailabilityPercent}
            onChange={(e) => set({ baseAvailabilityPercent: Number(e.target.value) })}
            min={0}
            max={100}
          />
        </div>
      </div>

      {/* Home site is the administrative anchor and the idle-time location; where the
          resource actually is at a point in time is derived from its assignments. Only
          meaningful once a tenant has more than one site. */}
      {isMultiSite && (
        <>
          <div className="space-y-2">
            <Label htmlFor="resource-home-site">Home Site</Label>
            <Select
              value={form.homeSiteId || SITE_UNSET}
              onValueChange={(v) => set({ homeSiteId: v === SITE_UNSET ? '' : v })}
            >
              <SelectTrigger id="resource-home-site">
                <SelectValue placeholder="Unset" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={SITE_UNSET}>
                  <span className="text-muted-foreground">Unset</span>
                </SelectItem>
                {sites.map((s) => (
                  <SelectItem key={s.id} value={s.id}>
                    {s.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          {isPlaceable ? (
            <p className="text-muted-foreground text-sm">
              {resourceType.displayNamePlural} are placed on a floorplan, so they belong to
              their site and cannot be used at another one.
            </p>
          ) : (
            <div className="flex items-center gap-2">
              <Checkbox
                id="resource-cross-site"
                checked={form.crossSiteAllowed}
                onCheckedChange={(c) => set({ crossSiteAllowed: !!c })}
              />
              <Label htmlFor="resource-cross-site" className="cursor-pointer text-sm">
                Available for other sites
              </Label>
            </div>
          )}
        </>
      )}

      {hasDirectory && (
        <ResourceDirectoryFields value={form} onChange={(patch) => set(patch)} />
      )}

      {emailInvalid && <ErrorAlert message="Email address is not valid." />}

      {customFields.isError && (
        <ErrorAlert message="Could not load this type's custom fields. Close and reopen to try again — saving is blocked until they load, so nothing required is missed." />
      )}

      {customFields.fields.length > 0 && (
        <div className="space-y-4 border-t pt-4">
          {customFields.fields.map((field) => (
            <CustomFieldInput
              key={field.id}
              field={field}
              value={customFields.valueOf(field, form.customFields)}
              onChange={(value) => setCustomField(field.key, value)}
              // Null while creating: a list field's rows hang off the resource, so there is
              // nowhere to put them until it exists.
              resourceId={resource?.id ?? null}
            />
          ))}
        </div>
      )}
    </FormDialog>
  );
}
