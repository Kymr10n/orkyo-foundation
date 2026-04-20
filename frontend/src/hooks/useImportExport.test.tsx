import { renderHook } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { useExportHandler, useImportHandler } from './useImportExport';
import type { ExportFormat, ImportFormat, ExportContext } from '../lib/utils/import-export';

describe('useExportHandler', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('calls handler when context matches', () => {
    const handler = vi.fn();
    const context: ExportContext = 'spaces';

    renderHook(() => useExportHandler(context, handler));

    const event = new CustomEvent('export-data', {
      detail: { context: 'spaces', format: 'csv' as ExportFormat },
    });
    window.dispatchEvent(event);

    expect(handler).toHaveBeenCalledWith('csv');
    expect(handler).toHaveBeenCalledTimes(1);
  });

  it('does not call handler when context does not match', () => {
    const handler = vi.fn();
    const context: ExportContext = 'spaces';

    renderHook(() => useExportHandler(context, handler));

    const event = new CustomEvent('export-data', {
      detail: { context: 'requests', format: 'csv' as ExportFormat },
    });
    window.dispatchEvent(event);

    expect(handler).not.toHaveBeenCalled();
  });

  it('handles different export formats', () => {
    const handler = vi.fn();
    const context: ExportContext = 'requests';

    renderHook(() => useExportHandler(context, handler));

    const csvEvent = new CustomEvent('export-data', {
      detail: { context: 'requests', format: 'csv' as ExportFormat },
    });
    window.dispatchEvent(csvEvent);

    const xlsxEvent = new CustomEvent('export-data', {
      detail: { context: 'requests', format: 'xlsx' as ExportFormat },
    });
    window.dispatchEvent(xlsxEvent);

    expect(handler).toHaveBeenCalledWith('csv');
    expect(handler).toHaveBeenCalledWith('xlsx');
    expect(handler).toHaveBeenCalledTimes(2);
  });

  it('removes event listener on unmount', () => {
    const handler = vi.fn();
    const context: ExportContext = 'spaces';

    const { unmount } = renderHook(() => useExportHandler(context, handler));

    unmount();

    const event = new CustomEvent('export-data', {
      detail: { context: 'spaces', format: 'csv' as ExportFormat },
    });
    window.dispatchEvent(event);

    expect(handler).not.toHaveBeenCalled();
  });
});

describe('useImportHandler', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('calls handler when context matches', () => {
    const handler = vi.fn();
    const context: ExportContext = 'spaces';
    const mockFile = new File(['test'], 'test.csv', { type: 'text/csv' });

    renderHook(() => useImportHandler(context, handler));

    const event = new CustomEvent('import-data', {
      detail: { context: 'spaces', format: 'csv' as ImportFormat, file: mockFile },
    });
    window.dispatchEvent(event);

    expect(handler).toHaveBeenCalledWith(mockFile, 'csv');
    expect(handler).toHaveBeenCalledTimes(1);
  });

  it('does not call handler when context does not match', () => {
    const handler = vi.fn();
    const context: ExportContext = 'spaces';
    const mockFile = new File(['test'], 'test.csv', { type: 'text/csv' });

    renderHook(() => useImportHandler(context, handler));

    const event = new CustomEvent('import-data', {
      detail: { context: 'requests', format: 'csv' as ImportFormat, file: mockFile },
    });
    window.dispatchEvent(event);

    expect(handler).not.toHaveBeenCalled();
  });

  it('handles different import formats', () => {
    const handler = vi.fn();
    const context: ExportContext = 'requests';
    const csvFile = new File(['test'], 'test.csv', { type: 'text/csv' });
    const xlsxFile = new File(['test'], 'test.xlsx', { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });

    renderHook(() => useImportHandler(context, handler));

    const csvEvent = new CustomEvent('import-data', {
      detail: { context: 'requests', format: 'csv' as ImportFormat, file: csvFile },
    });
    window.dispatchEvent(csvEvent);

    const xlsxEvent = new CustomEvent('import-data', {
      detail: { context: 'requests', format: 'xlsx' as ImportFormat, file: xlsxFile },
    });
    window.dispatchEvent(xlsxEvent);

    expect(handler).toHaveBeenCalledWith(csvFile, 'csv');
    expect(handler).toHaveBeenCalledWith(xlsxFile, 'xlsx');
    expect(handler).toHaveBeenCalledTimes(2);
  });

  it('removes event listener on unmount', () => {
    const handler = vi.fn();
    const context: ExportContext = 'spaces';
    const mockFile = new File(['test'], 'test.csv', { type: 'text/csv' });

    const { unmount } = renderHook(() => useImportHandler(context, handler));

    unmount();

    const event = new CustomEvent('import-data', {
      detail: { context: 'spaces', format: 'csv' as ImportFormat, file: mockFile },
    });
    window.dispatchEvent(event);

    expect(handler).not.toHaveBeenCalled();
  });
});
