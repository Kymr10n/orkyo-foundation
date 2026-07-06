import { useEffect, useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { FormDialog } from '@foundation/src/components/ui/FormDialog';
import { Input } from '@foundation/src/components/ui/input';
import { Label } from '@foundation/src/components/ui/label';
import { Textarea } from '@foundation/src/components/ui/textarea';
import {
  createJobTitle,
  updateJobTitle,
  type JobTitleInfo,
} from '@foundation/src/lib/api/job-titles-api';
import { qk } from '@foundation/src/lib/api/query-keys';

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
    meta: {
      successMessage: jobTitle ? 'Job title updated' : 'Job title created',
      errorMessage: jobTitle ? 'Failed to update job title' : 'Failed to create job title',
      invalidates: [qk.jobTitles.all()],
    },
    onSuccess: (saved) => {
      setError(null);
      onSaved?.(saved);
      onOpenChange(false);
    },
    onError: (err) => {
      setError(err instanceof Error ? err.message : 'Save failed');
    },
  });

  const handleSubmit = () => {
    if (!form.name.trim()) return;
    mutation.mutate();
  };

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title={jobTitle ? 'Edit Job Title' : 'New Job Title'}
      description="Job titles are tenant-wide reference data assigned to person resources."
      onSubmit={handleSubmit}
      isSubmitting={mutation.isPending}
      submitLabel="Save"
      submitDisabled={!form.name.trim()}
      error={error}
    >
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
    </FormDialog>
  );
}
