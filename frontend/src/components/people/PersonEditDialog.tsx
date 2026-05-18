import { useState, useEffect } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from '@foundation/src/components/ui/dialog';
import { Button } from '@foundation/src/components/ui/button';
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
import { Loader2, Plus } from 'lucide-react';
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
  // Profile side
  email: string;
  jobTitleId: string;      // empty = unassigned
  departmentId: string;    // empty = unassigned
  notes: string;
}

const emptyForm: FormState = {
  name: '',
  description: '',
  allocationMode: 'Exclusive',
  baseAvailabilityPercent: 100,
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

export function PersonEditDialog({ person, isOpen, onClose, onSaved }: PersonEditDialogProps) {
  const [form, setForm] = useState<FormState>(emptyForm);
  const [isSubmitting, setIsSubmitting] = useState(false);
  // Inline-create dialog state for each reference list. `undefined` = closed,
  // string = pre-populated name (from a Create-new sentinel selection).
  const [createJobTitleName, setCreateJobTitleName] = useState<string | undefined>(undefined);
  const [createDeptName, setCreateDeptName] = useState<string | undefined>(undefined);

  // Reference data
  const { data: jobTitles = [] } = useQuery({
    queryKey: ['job-titles', { includeInactive: false }],
    queryFn: () => getJobTitles(false),
    enabled: isOpen,
  });
  const { data: deptTree = [] } = useQuery({
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
    if (!person) {
      setForm(emptyForm);
      return;
    }
    setForm(fromResourceAndProfile(person, profile ?? null));
  }, [person, isOpen, profile]);

  const set = <K extends keyof FormState>(key: K, value: FormState[K]) =>
    setForm((prev) => ({ ...prev, [key]: value }));

  const saveMutation = useMutation({
    mutationFn: async (): Promise<ResourceInfo> => {
      const resourceFields = {
        resourceTypeKey: 'person',
        name: form.name,
        description: form.description || undefined,
        allocationMode: form.allocationMode,
        baseAvailabilityPercent: form.baseAvailabilityPercent,
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
    onSettled: () => setIsSubmitting(false),
    onSuccess: () => onSaved(),
  });

  const handleSubmit = (e: React.SyntheticEvent<HTMLFormElement>) => {
    e.preventDefault();
    setIsSubmitting(true);
    saveMutation.mutate();
  };

  // Sentinel handlers for the two reference selects: a "Create new…" value
  // opens the corresponding edit dialog with an empty name. The user types the
  // name there; on save, the new id is auto-applied to the form.
  const onJobTitleChange = (value: string) => {
    if (value === CREATE_NEW) {
      setCreateJobTitleName('');
      return;
    }
    set('jobTitleId', value === UNASSIGNED ? '' : value);
  };
  const onDepartmentChange = (value: string) => {
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
    form.jobTitleId && !jobTitles.some((j) => j.id === form.jobTitleId);
  const departmentMissing =
    form.departmentId && !deptOptions.some((o) => o.id === form.departmentId);

  return (
    <Dialog open={isOpen} onOpenChange={onClose}>
      <DialogContent className="sm:max-w-[500px]">
        <DialogHeader>
          <DialogTitle>{person ? 'Edit Person' : 'Add Person'}</DialogTitle>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-4">
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
                  <SelectItem value="Exclusive">Exclusive</SelectItem>
                  <SelectItem value="Fractional">Fractional</SelectItem>
                  <SelectItem value="ConcurrentCapacity" disabled>
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

          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose} disabled={isSubmitting}>
              Cancel
            </Button>
            <Button type="submit" disabled={isSubmitting || !form.name.trim()}>
              {isSubmitting ? (
                <>
                  <Loader2 className="h-4 w-4 mr-2 animate-spin" />
                  Saving...
                </>
              ) : (
                'Save'
              )}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>

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
    </Dialog>
  );
}
