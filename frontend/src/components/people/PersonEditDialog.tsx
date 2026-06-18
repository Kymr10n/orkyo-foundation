import { useState, useEffect } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { toast } from 'sonner';
import { ScrollableDialogBody } from '@foundation/src/components/ui/dialog';
import { ScaffoldDialog } from '@foundation/src/components/ui/ScaffoldDialog';
import { DialogFormFooter } from '@foundation/src/components/ui/DialogFormFooter';
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from '@foundation/src/components/ui/tabs';
import { Separator } from '@foundation/src/components/ui/separator';
import {
  StatusBanner,
  TabIndicatorDot,
  severityDotClass,
  type StatusItem,
} from '@foundation/src/components/ui/status-indicator';
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
import { ALLOCATION_MODE } from '@foundation/src/constants/allocation-mode';
import { Checkbox } from '@foundation/src/components/ui/checkbox';
import { useSites, useIsMultiSite } from '@foundation/src/hooks/useSites';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';
import { Plus } from 'lucide-react';
import {
  createResource,
  updateResource,
  type ResourceInfo,
  type CreateResourceRequest,
} from '@foundation/src/lib/api/resources-api';
import {
  getPersonProfile,
  upsertPersonProfile,
  type PersonProfileInfo,
} from '@foundation/src/lib/api/person-profiles-api';
import {
  getJobTitles,
  type JobTitleInfo,
} from '@foundation/src/lib/api/job-titles-api';
import {
  getDepartmentTree,
  type DepartmentTreeNode,
} from '@foundation/src/lib/api/departments-api';
import { JobTitleEditDialog } from '@foundation/src/components/settings/JobTitleEditDialog';
import { DepartmentEditDialog } from '@foundation/src/components/settings/DepartmentEditDialog';
import { isValidEmail } from '@foundation/src/lib/utils/validation';

interface PersonEditDialogProps {
  person: ResourceInfo | null;
  isOpen: boolean;
  onClose: () => void;
  onSaved: () => void;
}

interface FormState {
  // Resource side
  name: string;
  description: string;
  allocationMode: string;
  baseAvailabilityPercent: number;
  // Location (Home-Site model)
  homeSiteId: string;     // empty = unset
  crossSiteAllowed: boolean;
  // Profile side
  email: string;
  jobTitleId: string;      // empty = unassigned
  departmentId: string;    // empty = unassigned
  notes: string;
}

const emptyForm: FormState = {
  name: '',
  description: '',
  allocationMode: ALLOCATION_MODE.EXCLUSIVE,
  baseAvailabilityPercent: 100,
  homeSiteId: '',
  crossSiteAllowed: true,
  email: '',
  jobTitleId: '',
  departmentId: '',
  notes: '',
};

const UNASSIGNED = '__unassigned__';
const CREATE_NEW = '__create_new__';

function fromResourceAndProfile(person: ResourceInfo, profile: PersonProfileInfo | null): FormState {
  // Defensive coalescing: form contract requires strings/numbers (never null/undefined),
  // otherwise form.name.trim() etc. would crash. The API normally returns populated
  // ResourceInfo, but a partial/stale shape would otherwise leak through.
  return {
    name: person.name ?? '',
    description: person.description ?? '',
    allocationMode: person.allocationMode ?? emptyForm.allocationMode,
    baseAvailabilityPercent: person.baseAvailabilityPercent ?? emptyForm.baseAvailabilityPercent,
    homeSiteId: person.homeSiteId ?? '',
    crossSiteAllowed: person.crossSiteAllowed ?? true,
    email: profile?.email ?? '',
    jobTitleId: profile?.jobTitleId ?? '',
    departmentId: profile?.departmentId ?? '',
    notes: profile?.notes ?? '',
  };
}

/** GET /person-profiles/{id} returns 404 when no row exists; treat that as "empty profile". */
async function loadProfileOrNull(resourceId: string): Promise<PersonProfileInfo | null> {
  try {
    return await getPersonProfile(resourceId);
  } catch {
    return null;
  }
}

/**
 * Flatten the department tree to depth-indented options. Used by the Department
 * select. Inactive departments are excluded by the API (we pass includeInactive=false),
 * but a department the person is *already* assigned to is preserved via the always-
 * rendered fallback option in the Select so admins can clearly see the current value.
 */
function flattenForSelect(tree: DepartmentTreeNode[], depth = 0): { id: string; label: string }[] {
  const out: { id: string; label: string }[] = [];
  for (const node of tree) {
    out.push({ id: node.id, label: `${'  '.repeat(depth)}${node.name}` });
    out.push(...flattenForSelect(node.children, depth + 1));
  }
  return out;
}

const SITE_UNSET = '__unset_site__';

export function PersonEditDialog({ person, isOpen, onClose, onSaved }: PersonEditDialogProps) {
  const { data: sites = [] } = useSites();
  const isMultiSite = useIsMultiSite();
  const canEdit = useCanEdit();
  const [form, setForm] = useState<FormState>(emptyForm);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [tab, setTab] = useState<'details' | 'allocation' | 'location'>('details');

  // Inline-create dialog state for each reference list. `undefined` = closed,
  // string = pre-populated name (from a Create-new sentinel selection).
  const [createJobTitleName, setCreateJobTitleName] = useState<string | undefined>(undefined);
  const [createDeptName, setCreateDeptName] = useState<string | undefined>(undefined);

  // Reference data
  const { data: jobTitles = [], isLoading: jobTitlesLoading } = useQuery({
    queryKey: ['job-titles', { includeInactive: false }],
    queryFn: () => getJobTitles(false),
    enabled: isOpen,
  });
  const { data: deptTree = [], isLoading: deptTreeLoading } = useQuery({
    queryKey: ['departments', 'tree', { includeInactive: false }],
    queryFn: () => getDepartmentTree(false),
    enabled: isOpen,
  });
  const deptOptions = flattenForSelect(deptTree);

  // Person profile — fetched via React Query for proper cancellation and caching
  const { data: profile } = useQuery({
    queryKey: ['person-profile', person?.id],
    queryFn: () => loadProfileOrNull(person!.id),
    enabled: isOpen && !!person?.id,
  });

  // Sync server data into form state when it arrives or when the dialog opens
  useEffect(() => {
    if (!isOpen) return;
    setTab('details');
    if (!person) {
      setForm(emptyForm);
      return;
    }
    setForm(fromResourceAndProfile(person, profile ?? null));
  }, [person, isOpen, profile]);

  const set = <K extends keyof FormState>(key: K, value: FormState[K]) =>
    setForm((prev) => ({ ...prev, [key]: value }));

  const isEditing = !!person;

  const saveMutation = useMutation({
    mutationFn: async (): Promise<ResourceInfo> => {
      const resourceFields = {
        resourceTypeKey: 'person',
        name: form.name,
        description: form.description || undefined,
        allocationMode: form.allocationMode,
        baseAvailabilityPercent: form.baseAvailabilityPercent,
        homeSiteId: form.homeSiteId || null,
        crossSiteAllowed: form.crossSiteAllowed,
      };

      const saved = person
        ? await updateResource(person.id, resourceFields)
        : await createResource(resourceFields as CreateResourceRequest);

      await upsertPersonProfile(saved.id, {
        email: form.email || undefined,
        jobTitleId: form.jobTitleId === '' ? null : form.jobTitleId,
        departmentId: form.departmentId === '' ? null : form.departmentId,
        notes: form.notes || undefined,
      });

      return saved;
    },
    meta: {
      successMessage: isEditing ? 'Person updated' : 'Person created',
      errorMessage: isEditing ? 'Failed to update person' : 'Failed to create person',
      invalidates: [['resources', 'person'], ['person-profile']],
    },
    onSettled: () => setIsSubmitting(false),
    onSuccess: () => onSaved(),
  });

  const handleSubmit = (e: React.SyntheticEvent<HTMLFormElement>) => {
    e.preventDefault();
    if (form.email && !isValidEmail(form.email)) {
      toast.error(isEditing ? 'Failed to update person' : 'Failed to create person', {
        description: 'Please enter a valid email address',
      });
      return;
    }
    setIsSubmitting(true);
    saveMutation.mutate();
  };

  // Sentinel handlers for the two reference selects: a "Create new…" value
  // opens the corresponding edit dialog with an empty name. The user types the
  // name there; on save, the new id is auto-applied to the form.
  // radix-select 2.3+ fires onValueChange('') when the controlled value has no
  // matching mounted item — e.g. an assignment that was deactivated and is only
  // surfaced as a placeholder option. Ignoring that spurious empty-string clear
  // keeps the saved value intact (so saving never silently drops the assignment);
  // every genuine selection sends the UNASSIGNED/CREATE_NEW sentinel or a real id.
  const onJobTitleChange = (value: string) => {
    if (value === '') return;
    if (value === CREATE_NEW) {
      setCreateJobTitleName('');
      return;
    }
    set('jobTitleId', value === UNASSIGNED ? '' : value);
  };
  const onDepartmentChange = (value: string) => {
    if (value === '') return;
    if (value === CREATE_NEW) {
      setCreateDeptName('');
      return;
    }
    set('departmentId', value === UNASSIGNED ? '' : value);
  };

  // If the currently-saved value isn't in the active list (e.g. it was
  // deactivated), inject a placeholder option so the Select still shows
  // something rather than going blank.
  const jobTitleMissing =
    !jobTitlesLoading && !!form.jobTitleId && !jobTitles.some((j) => j.id === form.jobTitleId);
  const departmentMissing =
    !deptTreeLoading && !!form.departmentId && !deptOptions.some((o) => o.id === form.departmentId);

  // Validation status surfaced as tab dots + a summary banner. All current items live on
  // the Details tab; the dot is still computed per-tab so future per-tab warnings are trivial.
  const detailsItems: StatusItem[] = [
    ...(form.email && !isValidEmail(form.email)
      ? [{ id: 'email', message: 'Email address is not valid.', severity: 'error' as const }]
      : []),
    ...(jobTitleMissing
      ? [{ id: 'jobTitle', message: 'Assigned job title is no longer active.', severity: 'warning' as const }]
      : []),
    ...(departmentMissing
      ? [{ id: 'department', message: 'Assigned department is no longer active.', severity: 'warning' as const }]
      : []),
  ];
  const allStatusItems = detailsItems;

  return (
    <>
      <ScaffoldDialog
        open={isOpen}
        onOpenChange={onClose}
        contentClassName="sm:max-w-[520px] h-[600px] max-h-[85dvh]"
        title={person ? 'Edit Person' : 'Add Person'}
      >
        <StatusBanner items={allStatusItems} className="mx-6 mb-2 shrink-0" />

        <form onSubmit={handleSubmit} className="flex flex-col flex-1 min-h-0">
          <Tabs
            value={tab}
            onValueChange={(v) => setTab(v as typeof tab)}
            className="flex flex-col flex-1 min-h-0"
          >
            <TabsList className="mx-6 shrink-0">
              <TabsTrigger value="details" className="relative">
                Details
                <TabIndicatorDot dotClass={severityDotClass(detailsItems)} label="details warning" />
              </TabsTrigger>
              <TabsTrigger value="allocation" className="relative">
                Allocation
              </TabsTrigger>
              {isMultiSite && (
                <TabsTrigger value="location" className="relative">
                  Location
                </TabsTrigger>
              )}
            </TabsList>

            {/* forceMount on every panel keeps all controlled fields in the DOM so switching
                tabs mid-edit never remounts an input. Visibility is driven by the active tab. */}
            <ScrollableDialogBody className="px-6 py-4">
              <TabsContent
                value="details"
                forceMount
                className={tab === 'details' ? 'mt-0 space-y-4' : 'mt-0 hidden'}
              >
                <div className="space-y-2">
                  <Label htmlFor="name">Name *</Label>
                  <Input
                    id="name"
                    value={form.name}
                    onChange={(e) => set('name', e.target.value)}
                    required
                  />
                </div>

                <div className="space-y-2">
                  <Label htmlFor="email">Email</Label>
                  <Input
                    id="email"
                    type="email"
                    value={form.email}
                    onChange={(e) => set('email', e.target.value)}
                  />
                </div>

                <div className="grid grid-cols-2 gap-4">
                  <div className="space-y-2">
                    <Label htmlFor="jobTitle">Job Title</Label>
                    <Select
                      value={form.jobTitleId === '' ? UNASSIGNED : form.jobTitleId}
                      onValueChange={onJobTitleChange}
                    >
                      <SelectTrigger id="jobTitle">
                        <SelectValue placeholder="(unassigned)" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value={UNASSIGNED}>(unassigned)</SelectItem>
                        {jobTitleMissing && (
                          <SelectItem value={form.jobTitleId} disabled>
                            (current assignment — no longer active)
                          </SelectItem>
                        )}
                        {jobTitles.map((jt: JobTitleInfo) => (
                          <SelectItem key={jt.id} value={jt.id}>
                            {jt.name}
                          </SelectItem>
                        ))}
                        <SelectItem value={CREATE_NEW}>
                          <span className="flex items-center gap-1">
                            <Plus className="h-3 w-3" />
                            Create new…
                          </span>
                        </SelectItem>
                      </SelectContent>
                    </Select>
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="department">Department</Label>
                    <Select
                      value={form.departmentId === '' ? UNASSIGNED : form.departmentId}
                      onValueChange={onDepartmentChange}
                    >
                      <SelectTrigger id="department">
                        <SelectValue placeholder="(unassigned)" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value={UNASSIGNED}>(unassigned)</SelectItem>
                        {departmentMissing && (
                          <SelectItem value={form.departmentId} disabled>
                            (current assignment — no longer active)
                          </SelectItem>
                        )}
                        {deptOptions.map((opt) => (
                          <SelectItem key={opt.id} value={opt.id}>
                            {opt.label}
                          </SelectItem>
                        ))}
                        <SelectItem value={CREATE_NEW}>
                          <span className="flex items-center gap-1">
                            <Plus className="h-3 w-3" />
                            Create new…
                          </span>
                        </SelectItem>
                      </SelectContent>
                    </Select>
                  </div>
                </div>

                <div className="space-y-2">
                  <Label htmlFor="description">Description</Label>
                  <Textarea
                    id="description"
                    value={form.description}
                    onChange={(e) => set('description', e.target.value)}
                    rows={2}
                  />
                </div>

                <div className="space-y-2">
                  <Label htmlFor="notes">Notes</Label>
                  <Textarea
                    id="notes"
                    value={form.notes}
                    onChange={(e) => set('notes', e.target.value)}
                    rows={2}
                  />
                </div>
              </TabsContent>

              <TabsContent
                value="allocation"
                forceMount
                className={tab === 'allocation' ? 'mt-0 space-y-4' : 'mt-0 hidden'}
              >
                <div className="grid grid-cols-2 gap-4">
                  <div className="space-y-2">
                    <Label htmlFor="allocationMode">Allocation Mode</Label>
                    <Select
                      value={form.allocationMode}
                      onValueChange={(v) => set('allocationMode', v)}
                    >
                      <SelectTrigger>
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
                    <Label htmlFor="availability">Base Availability (%)</Label>
                    <Input
                      id="availability"
                      type="number"
                      value={form.baseAvailabilityPercent}
                      onChange={(e) => set('baseAvailabilityPercent', Number(e.target.value))}
                      min={0}
                      max={100}
                    />
                  </div>
                </div>
              </TabsContent>

              {/* Location (Home-Site model) — tab shown only for multi-site/paid tenants.
                  The home site is the administrative anchor and the idle-time location; where the
                  person actually is at a point in time is derived from their assignments. */}
              {isMultiSite && (
                <TabsContent
                  value="location"
                  forceMount
                  className={tab === 'location' ? 'mt-0 space-y-4' : 'mt-0 hidden'}
                >
                  <div className="space-y-2">
                    <Label htmlFor="homeSite">Home Site</Label>
                    <Select value={form.homeSiteId || SITE_UNSET} onValueChange={(v) => set('homeSiteId', v === SITE_UNSET ? '' : v)} disabled={!canEdit}>
                      <SelectTrigger id="homeSite"><SelectValue placeholder="Unset" /></SelectTrigger>
                      <SelectContent>
                        <SelectItem value={SITE_UNSET}><span className="text-muted-foreground">Unset</span></SelectItem>
                        {sites.map((s) => <SelectItem key={s.id} value={s.id}>{s.name}</SelectItem>)}
                      </SelectContent>
                    </Select>
                  </div>
                  <div className="flex items-center gap-2">
                    <Checkbox id="crossSite" checked={form.crossSiteAllowed} onCheckedChange={(c) => set('crossSiteAllowed', !!c)} disabled={!canEdit} />
                    <Label htmlFor="crossSite" className="text-sm cursor-pointer">Available for other sites</Label>
                  </div>
                </TabsContent>
              )}
            </ScrollableDialogBody>
          </Tabs>

          <Separator className="shrink-0" />
          <DialogFormFooter
            className="px-6 py-4 shrink-0"
            onCancel={onClose}
            isSubmitting={isSubmitting}
            submitLabel="Save"
            submitDisabled={!form.name.trim()}
          />
        </form>
      </ScaffoldDialog>

      {/* Inline-create sub-dialogs. When the user saves a new job title or
          department, we auto-select it in the form so they don't have to find
          it in the dropdown again. */}
      {createJobTitleName !== undefined && (
        <JobTitleEditDialog
          jobTitle={null}
          initialName={createJobTitleName}
          open={createJobTitleName !== undefined}
          onOpenChange={(open) => !open && setCreateJobTitleName(undefined)}
          onSaved={(jt) => set('jobTitleId', jt.id)}
        />
      )}
      {createDeptName !== undefined && (
        <DepartmentEditDialog
          department={null}
          initialName={createDeptName}
          open={createDeptName !== undefined}
          onOpenChange={(open) => !open && setCreateDeptName(undefined)}
          onSaved={(d) => set('departmentId', d.id)}
        />
      )}
    </>
  );
}
