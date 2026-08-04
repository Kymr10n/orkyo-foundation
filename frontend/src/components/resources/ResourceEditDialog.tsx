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

interface ResourceEditDialogProps {
  resourceType: ResourceTypeInfo;
  resource: ResourceInfo | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

interface FormState {
  name: string;
  description: string;
  externalReference: string;
  allocationMode: string;
  baseAvailabilityPercent: number;
  /** Empty string = unset. Select cannot hold an empty value, hence the sentinel below. */
  homeSiteId: string;
  crossSiteAllowed: boolean;
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
      crossSiteAllowed: true,
    }),
    toForm: (r) => ({
      name: r.name,
      description: r.description ?? '',
      externalReference: r.externalReference ?? '',
      allocationMode: r.allocationMode ?? ALLOCATION_MODE.EXCLUSIVE,
      baseAvailabilityPercent: r.baseAvailabilityPercent ?? 100,
      homeSiteId: r.homeSiteId ?? '',
      crossSiteAllowed: r.crossSiteAllowed ?? true,
    }),
    save: (form, r) => {
      const fields = {
        name: form.name,
        description: form.description || undefined,
        externalReference: form.externalReference || undefined,
        allocationMode: form.allocationMode,
        baseAvailabilityPercent: form.baseAvailabilityPercent,
        homeSiteId: form.homeSiteId || null,
        crossSiteAllowed: form.crossSiteAllowed,
      };
      return r
        ? updateResource(r.id, fields)
        : createResource({ resourceTypeKey: resourceType.key, ...fields });
    },
    entityLabel: resourceType.displayName,
    invalidates: [qk.resources.byType(resourceType.key), qk.resources.allFlat()],
  });

  const canSubmit = form.name.trim().length > 0;

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
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
        <div className="space-y-2">
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
        <div className="space-y-2">
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
        </>
      )}
    </FormDialog>
  );
}
