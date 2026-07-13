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
import { useEntityFormDialog } from '@foundation/src/hooks/useEntityFormDialog';

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

export function JobTitleEditDialog({
  jobTitle,
  open,
  onOpenChange,
  onSaved,
  initialName,
}: JobTitleEditDialogProps) {
  const { form, set, isDirty, error, submit, isSubmitting } = useEntityFormDialog<
    JobTitleInfo,
    FormState,
    JobTitleInfo
  >({
    open,
    onOpenChange,
    entity: jobTitle,
    emptyForm: () => ({ name: initialName ?? '', description: '' }),
    toForm: (jt) => ({ name: jt.name, description: jt.description ?? '' }),
    save: (form, jt) =>
      jt
        ? updateJobTitle(jt.id, { name: form.name, description: form.description || undefined })
        : createJobTitle({ name: form.name, description: form.description || undefined }),
    entityLabel: 'Job title',
    invalidates: [qk.jobTitles.all()],
    onSaved,
  });

  const handleSubmit = () => {
    if (!form.name.trim()) return;
    submit();
  };

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title={jobTitle ? 'Edit Job Title' : 'New Job Title'}
      description="Job titles are tenant-wide reference data assigned to person resources."
      onSubmit={handleSubmit}
      isSubmitting={isSubmitting}
      submitLabel="Save"
      submitDisabled={!form.name.trim()}
      error={error}
      dirty={isDirty}
    >
      <div className="space-y-2">
        <Label htmlFor="jt-name">Name</Label>
        <Input
          id="jt-name"
          value={form.name}
          onChange={(e) => set({ name: e.target.value })}
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
          onChange={(e) => set({ description: e.target.value })}
          maxLength={2000}
          rows={3}
        />
      </div>
    </FormDialog>
  );
}
