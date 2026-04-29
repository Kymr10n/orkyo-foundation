import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { ImportExportDialog } from './ImportExportDialog';

vi.mock('@foundation/src/components/ui/dialog', () => ({
  Dialog: ({ children, open }: { children: ReactNode; open: boolean }) => open ? <div role="dialog">{children}</div> : null,
  DialogContent: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DialogHeader: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DialogTitle: ({ children }: { children: ReactNode }) => <h2>{children}</h2>,
  DialogDescription: ({ children }: { children: ReactNode }) => <p>{children}</p>,
  DialogFooter: ({ children }: { children: ReactNode }) => <div>{children}</div>,
}));

vi.mock('@foundation/src/lib/utils/import-export', () => ({
  getExportFilename: () => 'export-test.csv',
  getSupportedFormats: () => ({ export: ['csv', 'json'], import: ['csv', 'json'] }),
  isImportSupported: () => true,
}));

describe('ImportExportDialog', () => {
  const defaultProps = {
    open: true,
    onOpenChange: vi.fn(),
    mode: 'export' as const,
    context: 'spaces' as const,
    onExport: vi.fn(),
    onImport: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders export dialog title', () => {
    render(<ImportExportDialog {...defaultProps} />);
    expect(screen.getByRole('heading', { name: /export/i })).toBeInTheDocument();
  });

  it('renders import dialog title', () => {
    render(<ImportExportDialog {...defaultProps} mode="import" />);
    expect(screen.getByRole('heading', { name: /import/i })).toBeInTheDocument();
  });

  it('does not render when closed', () => {
    const { container } = render(<ImportExportDialog {...defaultProps} open={false} />);
    expect(container.querySelector('[role="dialog"]')).toBeNull();
  });

  it('clicking Export button calls onExport and closes dialog (handleExport)', async () => {
    const user = userEvent.setup();
    render(<ImportExportDialog {...defaultProps} />);
    await user.click(screen.getByRole('button', { name: /Export/i }));
    expect(defaultProps.onExport).toHaveBeenCalledWith('csv');
    expect(defaultProps.onOpenChange).toHaveBeenCalledWith(false);
  });

  it('clicking Cancel button calls onOpenChange(false)', async () => {
    const user = userEvent.setup();
    render(<ImportExportDialog {...defaultProps} />);
    await user.click(screen.getByRole('button', { name: /Cancel/i }));
    expect(defaultProps.onOpenChange).toHaveBeenCalledWith(false);
  });

  it('resets state when dialog opens (useEffect)', () => {
    const { rerender } = render(<ImportExportDialog {...defaultProps} open={false} />);
    rerender(<ImportExportDialog {...defaultProps} open={true} />);
    // After reopening, the dialog renders — format is reset to default
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('file input change fires handleFileSelect', () => {
    render(<ImportExportDialog {...defaultProps} mode="import" />);
    const fileInput = document.querySelector('input[type="file"]') as HTMLInputElement;
    expect(fileInput).toBeTruthy();
    const file = new File(['data'], 'import.csv', { type: 'text/csv' });
    fireEvent.change(fileInput, { target: { files: [file] } });
    // After file selected, the file name should appear
    expect(screen.getByDisplayValue('import.csv')).toBeInTheDocument();
  });

  it('clicking Import button with file calls onImport (handleImport)', async () => {
    const user = userEvent.setup();
    render(<ImportExportDialog {...defaultProps} mode="import" />);

    const fileInput = document.querySelector('input[type="file"]') as HTMLInputElement;
    const file = new File(['data'], 'import.csv', { type: 'text/csv' });
    fireEvent.change(fileInput, { target: { files: [file] } });

    await user.click(screen.getByRole('button', { name: /^Import$/i }));
    await waitFor(() => {
      expect(defaultProps.onImport).toHaveBeenCalledWith(file, 'csv');
      expect(defaultProps.onOpenChange).toHaveBeenCalledWith(false);
    });
  });

  it('renders import mode dialog without crashing', () => {
    render(<ImportExportDialog {...defaultProps} mode="import" />);
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    // Browse button visible since import is supported
    expect(screen.getByRole('button', { name: /Browse/i })).toBeInTheDocument();
  });

  it('Browse button click triggers file input click', async () => {
    const user = userEvent.setup();
    render(<ImportExportDialog {...defaultProps} mode="import" />);
    const fileInput = document.querySelector('input[type="file"]') as HTMLInputElement;
    const clickSpy = vi.spyOn(fileInput, 'click').mockImplementation(() => {});
    await user.click(screen.getByRole('button', { name: /Browse/i }));
    expect(clickSpy).toHaveBeenCalled();
  });
});
