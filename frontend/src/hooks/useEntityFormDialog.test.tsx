import { renderHook, act, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createFeedbackMutationCache } from '@foundation/src/lib/core/query-client';
import { toast } from 'sonner';
import { useEntityFormDialog } from './useEntityFormDialog';

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

let queryClient: QueryClient;
function wrapper({ children }: { children: React.ReactNode }) {
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

function renderDialogHook(overrides: {
  entity?: Widget | null;
  open?: boolean;
  save?: (form: WidgetForm, entity: Widget | null) => Promise<Widget>;
  onSaved?: (saved: Widget) => void;
  onOpenChange?: (open: boolean) => void;
}) {
  const props = {
    open: overrides.open ?? true,
    onOpenChange: overrides.onOpenChange ?? vi.fn(),
    entity: overrides.entity ?? null,
    emptyForm: () => ({ name: '' }),
    toForm: (w: Widget) => ({ name: w.name }),
    save: overrides.save ?? vi.fn().mockResolvedValue({ id: 'w1', name: 'saved' }),
    entityLabel: 'Widget',
    invalidates: [['widgets']] as const,
    onSaved: overrides.onSaved,
  };
  return renderHook(
    (p: typeof props) => useEntityFormDialog<Widget, WidgetForm, Widget>(p),
    { initialProps: props, wrapper },
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    mutationCache: createFeedbackMutationCache(() => queryClient, toast),
  });
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

  it('create mode: saves, toasts "<label> created", invalidates, closes, calls onSaved', async () => {
    const save = vi.fn().mockResolvedValue({ id: 'w9', name: 'New' });
    const onSaved = vi.fn();
    const onOpenChange = vi.fn();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderDialogHook({ entity: null, save, onSaved, onOpenChange });
    act(() => result.current.set({ name: 'New' }));
    act(() => result.current.submit());

    await waitFor(() => expect(onSaved).toHaveBeenCalledWith({ id: 'w9', name: 'New' }));
    expect(save).toHaveBeenCalledWith({ name: 'New' }, null);
    expect(toast.success).toHaveBeenCalledWith('Widget created');
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['widgets'], exact: false });
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('edit mode: passes the entity to save and toasts "<label> updated"', async () => {
    const entity = { id: 'w1', name: 'Existing' };
    const save = vi.fn().mockResolvedValue(entity);

    const { result } = renderDialogHook({ entity, save });
    act(() => result.current.submit());

    await waitFor(() => expect(save).toHaveBeenCalledWith({ name: 'Existing' }, entity));
    expect(toast.success).toHaveBeenCalledWith('Widget updated');
  });

  it('failure: sets the inline error AND fires the derived error toast', async () => {
    const save = vi.fn().mockRejectedValue(new Error('Name already exists'));
    const onOpenChange = vi.fn();

    const { result } = renderDialogHook({ entity: null, save, onOpenChange });
    act(() => result.current.submit());

    await waitFor(() => expect(result.current.error).toBe('Name already exists'));
    expect(toast.error).toHaveBeenCalledWith('Failed to create widget', {
      description: 'Name already exists',
    });
    expect(onOpenChange).not.toHaveBeenCalled();
  });

  it('re-open resets form, baseline, and error', async () => {
    const save = vi.fn().mockRejectedValue(new Error('boom'));
    const props = {
      open: true,
      onOpenChange: vi.fn(),
      entity: null as Widget | null,
      emptyForm: () => ({ name: '' }),
      toForm: (w: Widget) => ({ name: w.name }),
      save,
      entityLabel: 'Widget',
      invalidates: [['widgets']] as const,
    };
    const { result, rerender } = renderHook(
      (p: typeof props) => useEntityFormDialog<Widget, WidgetForm, Widget>(p),
      { initialProps: props, wrapper },
    );

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
