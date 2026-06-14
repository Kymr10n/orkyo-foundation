import { useEffect, useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from '@foundation/src/components/ui/dialog';
import { Button } from '@foundation/src/components/ui/button';
import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import { Textarea } from '@foundation/src/components/ui/textarea';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';
import { Loader2 } from 'lucide-react';
import {
  createJobTitle,
  updateJobTitle,
  type JobTitleInfo,
} from '@foundation/src/lib/api/job-titles-api';

interface JobTitleEditDialogProps {
  jobTitle: JobTitleInfo | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Optional: invoked with the saved entity on successful create or update. Used by inline-create flows. */
  onSaved?: (jt: JobTitleInfo) => void;
  /** Optional pre-populated name (used when caller opens this dialog from a "Create X" inline action). */
  initialName?: string;
}

interface FormState {
  name: string;
  description: string;
}

const empty: FormState = { name: '', description: '' };

function fromInfo(jt: JobTitleInfo): FormState {
  return {
    name: jt.name,
    description: jt.description ?? '',
  };
}

export function JobTitleEditDialog({
  jobTitle,
  open,
  onOpenChange,
  onSaved,
  initialName,
}: JobTitleEditDialogProps) {
  const [form, setForm] = useState<FormState>(empty);
  const [error, setError] = useState<string | null>(null);
  const canEdit = useCanEdit();
  const queryClient = useQueryClient();

  useEffect(() => {
    if (!open) return;
    setError(null);
    if (jobTitle) setForm(fromInfo(jobTitle));
    else setForm({ ...empty, name: initialName ?? '' });
  }, [jobTitle, open, initialName]);

  const mutation = useMutation({
    mutationFn: async () => {
      if (jobTitle) {
        return updateJobTitle(jobTitle.id, {
          name: form.name,
          description: form.description || undefined,
        });
      }
      return createJobTitle({
        name: form.name,
        description: form.description || undefined,
      });
    },
    onSuccess: (saved) => {
      queryClient.invalidateQueries({ queryKey: ['job-titles'] });
      onSaved?.(saved);
      onOpenChange(false);
    },
    onError: (err) => setError(err instanceof Error ? err.message : 'Save failed'),
  });

  const handleSubmit = (e: React.SyntheticEvent<HTMLFormElement>) => {
    e.preventDefault();
    if (!form.name.trim()) return;
    mutation.mutate();
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{jobTitle ? 'Edit Job Title' : 'New Job Title'}</DialogTitle>
          <DialogDescription>
            Job titles are tenant-wide reference data assigned to person resources.
          </DialogDescription>
        </DialogHeader>
        <form onSubmit={handleSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="jt-name">Name</Label>
            <Input
              id="jt-name"
              value={form.name}
              onChange={(e) => setForm({ ...form, name: e.target.value })}
              maxLength={200}
              autoFocus
              required
            />
          </div>
          <div className="space-y-2">
            <Label htmlFor="jt-description">Description</Label>
            <Textarea
              id="jt-description"
              value={form.description}
              onChange={(e) => setForm({ ...form, description: e.target.value })}
              maxLength={2000}
              rows={3}
            />
          </div>
          {error && <p className="text-sm text-destructive">{error}</p>}
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={mutation.isPending || !form.name.trim() || !canEdit}>
              {mutation.isPending ? (
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
