import { renderHook, act, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useExportHandler, useImportHandler } from './useImportExport';
import { useUiActionsStore } from '@foundation/src/store/ui-actions-store';
import { toast } from 'sonner';
import type { ExportFormat, ImportFormat, ExportContext } from '../lib/utils/import-export';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

/** Every export registration carries one; the content is irrelevant to firing. */
const OFFER = { label: 'Spaces', description: 'Spaces.', formats: ['csv'] as ExportFormat[] };

function resetStore() {
  useUiActionsStore.setState({
    exportTick: 0,
    importTick: 0,
    commandPaletteTick: 0,
    tourTick: 0,
    lastExport: null,
    lastImport: null,
    exportRegistry: new Map(),
    importRegistry: new Map(),
  });
}

// useImportHandler reads the query client (for options.invalidates), so hooks
// render under a provider — mirroring every real consumer.
let queryClient: QueryClient;
function wrapper({ children }: { children: React.ReactNode }) {
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

beforeEach(() => {
  vi.clearAllMocks();
  resetStore();
  queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
});

describe('useExportHandler', () => {
  it('calls handler when context matches', () => {
    const handler = vi.fn();
    const context: ExportContext = 'spaces';

    renderHook(() => useExportHandler(context, handler, OFFER), { wrapper });

    act(() => {
      useUiActionsStore.getState().triggerExport({ context: 'spaces', format: 'csv' as ExportFormat });
    });

    expect(handler).toHaveBeenCalledWith('csv');
    expect(handler).toHaveBeenCalledTimes(1);
  });

  it('does not call handler when context does not match', () => {
    const handler = vi.fn();
    const context: ExportContext = 'spaces';

    renderHook(() => useExportHandler(context, handler, OFFER), { wrapper });

    act(() => {
      useUiActionsStore.getState().triggerExport({ context: 'requests', format: 'csv' as ExportFormat });
    });

    expect(handler).not.toHaveBeenCalled();
  });

  it('handles different export formats', () => {
    const handler = vi.fn();
    const context: ExportContext = 'requests';

    renderHook(() => useExportHandler(context, handler, OFFER), { wrapper });

    act(() => {
      useUiActionsStore.getState().triggerExport({ context: 'requests', format: 'csv' as ExportFormat });
    });
    act(() => {
      useUiActionsStore.getState().triggerExport({ context: 'requests', format: 'xlsx' as ExportFormat });
    });

    expect(handler).toHaveBeenCalledWith('csv');
    expect(handler).toHaveBeenCalledWith('xlsx');
    expect(handler).toHaveBeenCalledTimes(2);
  });

  it('does not re-fire after unmount', () => {
    const handler = vi.fn();
    const context: ExportContext = 'spaces';

    const { unmount } = renderHook(() => useExportHandler(context, handler, OFFER), { wrapper });
    unmount();

    act(() => {
      useUiActionsStore.getState().triggerExport({ context: 'spaces', format: 'csv' as ExportFormat });
    });

    expect(handler).not.toHaveBeenCalled();
  });
});

describe('useImportHandler', () => {
  const mockFile = new File(['test'], 'test.csv', { type: 'text/csv' });

  it('calls handler when context matches', () => {
    const handler = vi.fn();
    const context: ExportContext = 'spaces';

    renderHook(() => useImportHandler(context, handler), { wrapper });

    act(() => {
      useUiActionsStore.getState().triggerImport({ context: 'spaces', format: 'csv' as ImportFormat, file: mockFile });
    });

    expect(handler).toHaveBeenCalledWith(mockFile, 'csv');
    expect(handler).toHaveBeenCalledTimes(1);
  });

  it('does not call handler when context does not match', () => {
    const handler = vi.fn();
    const context: ExportContext = 'spaces';

    renderHook(() => useImportHandler(context, handler), { wrapper });

    act(() => {
      useUiActionsStore.getState().triggerImport({ context: 'requests', format: 'csv' as ImportFormat, file: mockFile });
    });

    expect(handler).not.toHaveBeenCalled();
  });

  it('handles different import formats', () => {
    const handler = vi.fn();
    const context: ExportContext = 'requests';
    const csvFile = new File(['test'], 'test.csv', { type: 'text/csv' });
    const xlsxFile = new File(['test'], 'test.xlsx', { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });

    renderHook(() => useImportHandler(context, handler), { wrapper });

    act(() => {
      useUiActionsStore.getState().triggerImport({ context: 'requests', format: 'csv' as ImportFormat, file: csvFile });
    });
    act(() => {
      useUiActionsStore.getState().triggerImport({ context: 'requests', format: 'xlsx' as ImportFormat, file: xlsxFile });
    });

    expect(handler).toHaveBeenCalledWith(csvFile, 'csv');
    expect(handler).toHaveBeenCalledWith(xlsxFile, 'xlsx');
    expect(handler).toHaveBeenCalledTimes(2);
  });

  it('does not re-fire after unmount', () => {
    const handler = vi.fn();
    const context: ExportContext = 'spaces';

    const { unmount } = renderHook(() => useImportHandler(context, handler), { wrapper });
    unmount();

    act(() => {
      useUiActionsStore.getState().triggerImport({ context: 'spaces', format: 'csv' as ImportFormat, file: mockFile });
    });

    expect(handler).not.toHaveBeenCalled();
  });

  // ── Centralized feedback options (mirrors the mutation meta convention) ──

  it('fires success toast (function form) + invalidates keys when options are set', async () => {
    const handler = vi.fn().mockResolvedValue(3);
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    renderHook(
      () =>
        useImportHandler('users', handler, {
          successMessage: (n) => `Successfully imported ${n} users`,
          errorMessage: 'Failed to import users',
          invalidates: [['users'], ['invitations']],
        }),
      { wrapper },
    );

    act(() => {
      useUiActionsStore.getState().triggerImport({ context: 'users', format: 'csv' as ImportFormat, file: mockFile });
    });

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Successfully imported 3 users'));
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['users'], exact: false });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['invitations'], exact: false });
    expect(toast.error).not.toHaveBeenCalled();
  });

  it('fires the error toast with the thrown message as description when the handler rejects', async () => {
    const handler = vi.fn().mockRejectedValue(new Error('No valid users found in file'));

    renderHook(
      () =>
        useImportHandler('users', handler, {
          successMessage: 'Imported',
          errorMessage: 'Failed to import users',
        }),
      { wrapper },
    );

    act(() => {
      useUiActionsStore.getState().triggerImport({ context: 'users', format: 'csv' as ImportFormat, file: mockFile });
    });

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith('Failed to import users', {
        description: 'No valid users found in file',
      }),
    );
    expect(toast.success).not.toHaveBeenCalled();
  });

  it('stays silent without options (legacy consumers own their feedback)', async () => {
    const handler = vi.fn().mockResolvedValue(undefined);

    renderHook(() => useImportHandler('spaces', handler), { wrapper });

    act(() => {
      useUiActionsStore.getState().triggerImport({ context: 'spaces', format: 'csv' as ImportFormat, file: mockFile });
    });

    await waitFor(() => expect(handler).toHaveBeenCalled());
    expect(toast.success).not.toHaveBeenCalled();
    expect(toast.error).not.toHaveBeenCalled();
  });
});

describe('capability registration', () => {
  it('registers the offer while mounted and withdraws it on unmount', () => {
    const { unmount } = renderHook(
      () => useExportHandler('resources:tool', vi.fn(), {
        label: 'Tools',
        description: 'Tools.',
        formats: ['csv', 'json'],
      }),
      { wrapper },
    );

    const registered = useUiActionsStore.getState().exportRegistry.get('resources:tool');
    expect(registered).toEqual({ label: 'Tools', description: 'Tools.', formats: ['csv', 'json'] });

    unmount();
    // The TopBar reads this map: a page that is gone must stop offering export.
    expect(useUiActionsStore.getState().exportRegistry.has('resources:tool')).toBe(false);
  });

  it('registers import formats separately, defaulting to CSV', () => {
    renderHook(() => useImportHandler('spaces', vi.fn()), { wrapper });
    expect(useUiActionsStore.getState().importRegistry.get('spaces')).toEqual({ formats: ['csv'] });
  });

  it('records the declared import formats when given', () => {
    renderHook(
      () => useImportHandler('criteria', vi.fn(), { formats: ['csv', 'json'] }),
      { wrapper },
    );
    expect(useUiActionsStore.getState().importRegistry.get('criteria')).toEqual({
      formats: ['csv', 'json'],
    });
  });
});
