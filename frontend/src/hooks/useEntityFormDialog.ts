import { useState } from 'react';
import type { UseMutationResult } from '@tanstack/react-query';
import { errorMessage } from './mutation-utils';
import { stableStringify } from '@foundation/src/lib/utils/stable-stringify';

/**
 * Shared scaffold for the standard entity edit dialog (see docs/dialog-feedback.md):
 * form + baseline state, reset-on-open, JSON dirty check, and the submit that runs a
 * domain save mutation with the inline setError kept for in-context display.
 *
 * The domain hook (`hooks/use*.ts`) owns the API call, the query keys and the `meta`
 * feedback; the caller keeps field rendering, validity (`submitDisabled`), and any
 * entity-specific data fetching. `entity === null` means create mode.
 */
export interface UseEntityFormDialogOptions<TEntity, TForm, TSaved, TVariables> {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** The entity being edited, or null to create. */
  entity: TEntity | null;
  /** Create-mode initial form; may close over caller props (e.g. initialName). */
  emptyForm: () => TForm;
  /** Maps the entity to its form state for edit mode. */
  toForm: (entity: TEntity) => TForm;
  /**
   * The domain save mutation (e.g. `useSaveSite()`). Its `meta` declares the success toast,
   * the invalidation and `suppressErrorToast: true`: this dialog stays open on failure and
   * shows the message inline, so a toast would report the same error twice.
   */
  mutation: UseMutationResult<TSaved, Error, TVariables, unknown>;
  /** Builds the mutation's variables; receives the entity for update-vs-create branching. */
  toVariables: (form: TForm, entity: TEntity | null) => TVariables;
  /**
   * Client-side check run on submit, for a rule a disabled Save button cannot explain. A
   * returned message shows in the inline error and nothing is sent.
   */
  validate?: (form: TForm) => string | null;
  /** Invoked with the saved entity on success (inline-create flows). */
  onSaved?: (saved: TSaved) => void;
}

export interface UseEntityFormDialogResult<TForm> {
  form: TForm;
  setForm: React.Dispatch<React.SetStateAction<TForm>>;
  /** Patch-style field updater: `set({ name: e.target.value })`. */
  set: (patch: Partial<TForm>) => void;
  isDirty: boolean;
  /** Inline error for the dialog's ErrorAlert. The failure is not also toasted. */
  error: string | null;
  submit: () => void;
  isSubmitting: boolean;
}

export function useEntityFormDialog<TEntity, TForm, TSaved, TVariables>({
  open,
  onOpenChange,
  entity,
  emptyForm,
  toForm,
  mutation,
  toVariables,
  validate,
  onSaved,
}: UseEntityFormDialogOptions<TEntity, TForm, TSaved, TVariables>): UseEntityFormDialogResult<TForm> {
  const [form, setForm] = useState<TForm>(() => (open && entity ? toForm(entity) : emptyForm()));
  // Snapshot of the form as last synced; the dirty guard compares against it.
  const [baseline, setBaseline] = useState<TForm>(form);
  const [error, setError] = useState<string | null>(null);

  // Reseed when the dialog opens, or when it swaps entity while open. This is React's
  // "adjusting state when a prop changes" render-phase update rather than an effect: an
  // effect would paint the stale form for one frame and cascade a second render pass.
  // `synced` tracks `open` even while closed, so closing and reopening the same entity
  // still reseeds (a discarded edit must not survive the reopen).
  const [synced, setSynced] = useState<{
    open: boolean;
    entity: TEntity | null | undefined;
  } | null>(null);
  if (synced?.open !== open || synced.entity !== entity) {
    setSynced({ open, entity });
    if (open) {
      setError(null);
      const next = entity ? toForm(entity) : emptyForm();
      setForm(next);
      setBaseline(next);
    }
  }

  // Stable: a form may carry a user-edited map (custom fields), where re-adding a key
  // changes insertion order without changing the data.
  const isDirty = stableStringify(form) !== stableStringify(baseline);

  // Per-call callbacks, not the domain hook's: they close over this dialog's state. The
  // toast and the invalidation come from the mutation's `meta` (see ARCHITECTURE.md).
  const submit = () => {
    const invalid = validate?.(form);
    if (invalid) {
      setError(invalid);
      return;
    }
    mutation.mutate(toVariables(form, entity), {
      onSuccess: (saved) => {
        setError(null);
        onSaved?.(saved);
        onOpenChange(false);
      },
      onError: (err) => setError(errorMessage(err)),
    });
  };

  return {
    form,
    setForm,
    set: (patch) => setForm((prev) => ({ ...prev, ...patch })),
    isDirty,
    error,
    submit,
    isSubmitting: mutation.isPending,
  };
}
