import { renderHook, act } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { useExportHandler, useImportHandler } from './useImportExport';
import { useUiActionsStore } from '@foundation/src/store/ui-actions-store';
import type { ExportFormat, ImportFormat, ExportContext } from '../lib/utils/import-export';

function resetStore() {
  useUiActionsStore.setState({
    exportTick: 0,
    importTick: 0,
    commandPaletteTick: 0,
    tourTick: 0,
    lastExport: null,
    lastImport: null,
  });
}

describe('useExportHandler', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    resetStore();
  });

  it('calls handler when context matches', () => {
    const handler = vi.fn();
    const context: ExportContext = 'spaces';

    renderHook(() => useExportHandler(context, handler));

    act(() => {
      useUiActionsStore.getState().triggerExport({ context: 'spaces', format: 'csv' as ExportFormat });
    });

    expect(handler).toHaveBeenCalledWith('csv');
    expect(handler).toHaveBeenCalledTimes(1);
  });

  it('does not call handler when context does not match', () => {
    const handler = vi.fn();
    const context: ExportContext = 'spaces';

    renderHook(() => useExportHandler(context, handler));

    act(() => {
      useUiActionsStore.getState().triggerExport({ context: 'requests', format: 'csv' as ExportFormat });
    });

    expect(handler).not.toHaveBeenCalled();
  });

  it('handles different export formats', () => {
    const handler = vi.fn();
    const context: ExportContext = 'requests';

    renderHook(() => useExportHandler(context, handler));

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

    const { unmount } = renderHook(() => useExportHandler(context, handler));
    unmount();

    act(() => {
      useUiActionsStore.getState().triggerExport({ context: 'spaces', format: 'csv' as ExportFormat });
    });

    expect(handler).not.toHaveBeenCalled();
  });
});

describe('useImportHandler', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    resetStore();
  });

  it('calls handler when context matches', () => {
    const handler = vi.fn();
    const context: ExportContext = 'spaces';
    const mockFile = new File(['test'], 'test.csv', { type: 'text/csv' });

    renderHook(() => useImportHandler(context, handler));

    act(() => {
      useUiActionsStore.getState().triggerImport({ context: 'spaces', format: 'csv' as ImportFormat, file: mockFile });
    });

    expect(handler).toHaveBeenCalledWith(mockFile, 'csv');
    expect(handler).toHaveBeenCalledTimes(1);
  });

  it('does not call handler when context does not match', () => {
    const handler = vi.fn();
    const context: ExportContext = 'spaces';
    const mockFile = new File(['test'], 'test.csv', { type: 'text/csv' });

    renderHook(() => useImportHandler(context, handler));

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

    renderHook(() => useImportHandler(context, handler));

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
    const mockFile = new File(['test'], 'test.csv', { type: 'text/csv' });

    const { unmount } = renderHook(() => useImportHandler(context, handler));
    unmount();

    act(() => {
      useUiActionsStore.getState().triggerImport({ context: 'spaces', format: 'csv' as ImportFormat, file: mockFile });
    });

    expect(handler).not.toHaveBeenCalled();
  });
});
