import { useState, useEffect } from 'react';
import { useMutation } from '@tanstack/react-query';
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
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@foundation/src/components/ui/select';
import { Textarea } from '@foundation/src/components/ui/textarea';
import { Loader2 } from 'lucide-react';
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
  jobTitle: string;
  department: string;
  notes: string;
}

const emptyForm: FormState = {
  name: '',
  description: '',
  allocationMode: 'Exclusive',
  baseAvailabilityPercent: 100,
  email: '',
  jobTitle: '',
  department: '',
  notes: '',
};

function fromResourceAndProfile(person: ResourceInfo, profile: PersonProfileInfo | null): FormState {
  return {
    name: person.name,
    description: person.description ?? '',
    allocationMode: person.allocationMode,
    baseAvailabilityPercent: person.baseAvailabilityPercent,
    email: profile?.email ?? '',
    jobTitle: profile?.jobTitle ?? '',
    department: profile?.department ?? '',
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

export function PersonEditDialog({ person, isOpen, onClose, onSaved }: PersonEditDialogProps) {
  const [form, setForm] = useState<FormState>(emptyForm);
  const [isSubmitting, setIsSubmitting] = useState(false);

  useEffect(() => {
    if (!isOpen) return;
    if (!person) {
      setForm(emptyForm);
      return;
    }
    // Load existing profile fields when editing.
    loadProfileOrNull(person.id).then(profile => setForm(fromResourceAndProfile(person, profile)));
  }, [person, isOpen]);

  const set = <K extends keyof FormState>(key: K, value: FormState[K]) =>
    setForm(prev => ({ ...prev, [key]: value }));

  // Two-step save: create/update the resource, then upsert the profile fields.
  // Wrapped in a single mutation so the dialog only shows one spinner.
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
        jobTitle: form.jobTitle || undefined,
        department: form.department || undefined,
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
              <Input
                id="jobTitle"
                value={form.jobTitle}
                onChange={(e) => set('jobTitle', e.target.value)}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="department">Department</Label>
              <Input
                id="department"
                value={form.department}
                onChange={(e) => set('department', e.target.value)}
              />
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
              <Select value={form.allocationMode} onValueChange={(v) => set('allocationMode', v)}>
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
    </Dialog>
  );
}
