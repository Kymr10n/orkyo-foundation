import { useEffect, useRef, useState } from 'react';
import { useMutation } from '@tanstack/react-query';

/**
 * Shared scaffold for the standard entity edit dialog (see docs/dialog-feedback.md):
 * form + baseline state, reset-on-open, JSON dirty check, and a create-or-update
 * mutation wired to the central meta feedback (toast + invalidation) with the
 * inline setError kept for in-context display. Extracted from the repeated
 * pattern in JobTitleEditDialog / DepartmentEditDialog (W2.4).
 *
 * The caller keeps: field rendering, validity (`submitDisabled`), and any
 * entity-specific data fetching. `entity === null` means create mode.
 */
export interface UseEntityFormDialogOptions<TEntity, TForm, TSaved> {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** The entity being edited, or null to create. */
  entity: TEntity | null;
  /** Create-mode initial form; may close over caller props (e.g. initialName). */
  emptyForm: () => TForm;
  /** Maps the entity to its form state for edit mode. */
  toForm: (entity: TEntity) => TForm;
  /** Persists the form; receives the entity for update-vs-create branching. */
  save: (form: TForm, entity: TEntity | null) => Promise<TSaved>;
  /** Human label for the derived toasts, e.g. "Job title" → "Job title updated". */
  entityLabel: string;
  /** Query keys invalidated on success, prefix-style (exact: false). */
  invalidates: readonly (readonly unknown[])[];
  /** Invoked with the saved entity on success (inline-create flows). */
  onSaved?: (saved: TSaved) => void;
}

export interface UseEntityFormDialogResult<TForm> {
  form: TForm;
  setForm: React.Dispatch<React.SetStateAction<TForm>>;
  /** Patch-style field updater: `set({ name: e.target.value })`. */
  set: (patch: Partial<TForm>) => void;
  isDirty: boolean;
  /** Inline error for the dialog's ErrorAlert (kept alongside the error toast). */
  error: string | null;
  submit: () => void;
  isSubmitting: boolean;
}

export function useEntityFormDialog<TEntity, TForm, TSaved>({
  open,
  onOpenChange,
  entity,
  emptyForm,
  toForm,
  save,
  entityLabel,
  invalidates,
  onSaved,
}: UseEntityFormDialogOptions<TEntity, TForm, TSaved>): UseEntityFormDialogResult<TForm> {
  // Callers pass inline closures; keep the latest without making them effect deps.
  const emptyFormRef = useRef(emptyForm);
  emptyFormRef.current = emptyForm;
  const toFormRef = useRef(toForm);
  toFormRef.current = toForm;

  const [form, setForm] = useState<TForm>(() => emptyFormRef.current());
  // Snapshot of the form as last synced; the dirty guard compares against it.
  const [baseline, setBaseline] = useState<TForm>(form);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setError(null);
    const next = entity ? toFormRef.current(entity) : emptyFormRef.current();
    setForm(next);
    setBaseline(next);
  }, [open, entity]);

  const isDirty = JSON.stringify(form) !== JSON.stringify(baseline);

  const lowerLabel = entityLabel.toLowerCase();
  const mutation = useMutation({
    mutationFn: () => save(form, entity),
    meta: {
      successMessage: entity ? `${entityLabel} updated` : `${entityLabel} created`,
      errorMessage: entity ? `Failed to update ${lowerLabel}` : `Failed to create ${lowerLabel}`,
      invalidates,
    },
    onSuccess: (saved) => {
      setError(null);
      onSaved?.(saved);
      onOpenChange(false);
    },
    onError: (err) => setError(err instanceof Error ? err.message : 'Save failed'),
  });

  return {
    form,
    setForm,
    set: (patch) => setForm((prev) => ({ ...prev, ...patch })),
    isDirty,
    error,
    submit: () => mutation.mutate(),
    isSubmitting: mutation.isPending,
  };
}
