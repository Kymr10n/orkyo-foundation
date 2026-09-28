
import { renderHook, act, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { useMutation, type QueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import { useEntityFormDialog } from './useEntityFormDialog';
import { savedMessage, type SaveVariables } from './mutation-utils';
import { createTestQueryClient } from '@foundation/src/test-utils';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

interface Widget {
  id: string;
  name: string;
}
interface WidgetForm {
  name: string;
}
type WidgetSave = (v: SaveVariables<WidgetForm>) => Promise<Widget>;

let queryClient: QueryClient;
let wrapper: ReturnType<typeof createTestQueryClient>['wrapper'];

/** The shape a domain hook gives the dialog: the call plus its meta feedback. */
function useSaveWidget(save: WidgetSave) {
  return useMutation({
    mutationFn: save,
    meta: {
      successMessage: savedMessage('Widget created', 'Widget updated'),
      suppressErrorToast: true,
      invalidates: [['widgets']],
    },
  });
}

function renderDialogHook(overrides: {
  entity?: Widget | null;
  open?: boolean;
  save?: WidgetSave;
  onSaved?: (saved: Widget) => void;
  onOpenChange?: (open: boolean) => void;
}) {
  const save: WidgetSave =
    overrides.save ?? vi.fn().mockResolvedValue({ id: 'w1', name: 'saved' });
  const props = {
    open: overrides.open ?? true,
    onOpenChange: overrides.onOpenChange ?? vi.fn(),
    entity: overrides.entity ?? null,
    onSaved: overrides.onSaved,
  };
  return renderHook(
    (p: typeof props) =>
      useEntityFormDialog({
        ...p,
        emptyForm: () => ({ name: '' }),
        toForm: (w: Widget) => ({ name: w.name }),
        mutation: useSaveWidget(save),
        toVariables: (form: WidgetForm, w: Widget | null): SaveVariables<WidgetForm> =>
          w ? { id: w.id, data: form } : { id: null, data: form },
      }),
    { initialProps: props, wrapper },
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  ({ queryClient, wrapper } = createTestQueryClient({ feedback: true }));
});

describe('useEntityFormDialog', () => {
  it('initializes from emptyForm in create mode and from toForm in edit mode', () => {
    const create = renderDialogHook({ entity: null });
    expect(create.result.current.form).toEqual({ name: '' });

    const edit = renderDialogHook({ entity: { id: 'w1', name: 'Existing' } });
    expect(edit.result.current.form).toEqual({ name: 'Existing' });
  });

  it('tracks dirtiness against the opened baseline', () => {
    const { result } = renderDialogHook({ entity: { id: 'w1', name: 'Existing' } });
    expect(result.current.isDirty).toBe(false);
    act(() => result.current.set({ name: 'Changed' }));
    expect(result.current.isDirty).toBe(true);
  });

  it('create mode: runs the mutation, which toasts and invalidates; closes, calls onSaved', async () => {
    const save = vi.fn().mockResolvedValue({ id: 'w9', name: 'New' });
    const onSaved = vi.fn();
    const onOpenChange = vi.fn();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderDialogHook({ entity: null, save, onSaved, onOpenChange });
    act(() => result.current.set({ name: 'New' }));
    act(() => result.current.submit());

    await waitFor(() => expect(onSaved).toHaveBeenCalledWith({ id: 'w9', name: 'New' }));
    expect(save.mock.calls[0][0]).toEqual({ id: null, data: { name: 'New' } });
    expect(toast.success).toHaveBeenCalledWith('Widget created');
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['widgets'], exact: false });
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('edit mode: builds the variables from the entity', async () => {
    const entity = { id: 'w1', name: 'Existing' };
    const save = vi.fn().mockResolvedValue(entity);

    const { result } = renderDialogHook({ entity, save });
    act(() => result.current.submit());

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Widget updated'));
    expect(save.mock.calls[0][0]).toEqual({ id: 'w1', data: { name: 'Existing' } });
  });

  it('reports isSubmitting while the mutation runs', async () => {
    let resolve!: (w: Widget) => void;
    const save = vi.fn(() => new Promise<Widget>((r) => (resolve = r)));

    const { result } = renderDialogHook({ entity: null, save });
    expect(result.current.isSubmitting).toBe(false);
    act(() => result.current.submit());
    await waitFor(() => expect(result.current.isSubmitting).toBe(true));

    act(() => resolve({ id: 'w1', name: '' }));
    await waitFor(() => expect(result.current.isSubmitting).toBe(false));
  });

  it('failure: sets the inline error and does NOT also toast it', async () => {
    const save = vi.fn().mockRejectedValue(new Error('Name already exists'));
    const onOpenChange = vi.fn();

    const { result } = renderDialogHook({ entity: null, save, onOpenChange });
    act(() => result.current.submit());

    await waitFor(() => expect(result.current.error).toBe('Name already exists'));
    // One surface per error: the dialog stays open and shows the message in its
    // ErrorAlert, so the domain hook's `meta.suppressErrorToast` keeps the MutationCache quiet.
    expect(toast.error).not.toHaveBeenCalled();
    expect(onOpenChange).not.toHaveBeenCalled();
  });

  it('validate: a returned message shows inline and nothing is sent', async () => {
    const save = vi.fn().mockResolvedValue({ id: 'w1', name: 'ok' });
    const { result } = renderHook(
      () =>
        useEntityFormDialog({
          open: true,
          onOpenChange: vi.fn(),
          entity: null as Widget | null,
          emptyForm: () => ({ name: '' }),
          toForm: (w: Widget) => ({ name: w.name }),
          mutation: useSaveWidget(save),
          toVariables: (form: WidgetForm): SaveVariables<WidgetForm> => ({ id: null, data: form }),
          validate: (form: WidgetForm) => (form.name ? null : 'Name is required'),
        }),
      { wrapper },
    );

    act(() => result.current.submit());
    expect(result.current.error).toBe('Name is required');
    expect(save).not.toHaveBeenCalled();

    act(() => result.current.set({ name: 'Named' }));
    act(() => result.current.submit());
    await waitFor(() => expect(save).toHaveBeenCalled());
  });

  it('re-open resets form, baseline, and error', async () => {
    const save = vi.fn().mockRejectedValue(new Error('boom'));
    const { result, rerender } = renderDialogHook({ entity: null, save });
    const props = { open: true, onOpenChange: vi.fn(), entity: null, onSaved: undefined };

    act(() => result.current.set({ name: 'Draft' }));
    act(() => result.current.submit());
    await waitFor(() => expect(result.current.error).toBe('boom'));

    rerender({ ...props, open: false });
    rerender({ ...props, open: true });

    expect(result.current.form).toEqual({ name: '' });
    expect(result.current.isDirty).toBe(false);
    expect(result.current.error).toBeNull();
  });
});
