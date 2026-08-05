import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import type { ReactNode } from 'react';
import { ImportExportDialog } from './ImportExportDialog';

vi.mock('@foundation/src/components/ui/dialog', () => ({
  DIALOG_SIZE: { sm: '', md: '', lg: '', xl: '' },
  Dialog: ({ children, open }: { children: ReactNode; open: boolean }) => open ? <div role="dialog">{children}</div> : null,
  DialogContent: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DialogHeader: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DialogTitle: ({ children }: { children: ReactNode }) => <h2>{children}</h2>,
  DialogDescription: ({ children }: { children: ReactNode }) => <p>{children}</p>,
  DialogFooter: ({ children }: { children: ReactNode }) => <div>{children}</div>,
}));

vi.mock('@foundation/src/lib/utils/import-export', () => ({
  getExportFilename: () => 'export-test.csv',
}));

let mockAvailable = true;
vi.mock('@foundation/src/hooks/useDataExportAvailable', () => ({
  useDataExportAvailable: () => mockAvailable,
}));

// Labels, description and formats now come from the page's registration, so the
// dialog is driven by the store the way it is in the app.
import { useUiActionsStore } from '@foundation/src/store/ui-actions-store';

function registerSpaces() {
  useUiActionsStore.setState({
    exportRegistry: new Map([
      ['spaces', { label: 'Spaces', description: 'Export or import spaces.', formats: ['csv', 'json'] }],
    ]),
    importRegistry: new Map([['spaces', { formats: ['csv', 'json'] }]]),
  });
}

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
    mockAvailable = true;
    registerSpaces();
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

  describe('when the tenant plan does not include the feature', () => {
    beforeEach(() => {
      mockAvailable = false;
    });

    it.each(['export', 'import'] as const)(
      'offers an upgrade instead of the %s form',
      (mode) => {
        render(
          <MemoryRouter>
            <ImportExportDialog {...defaultProps} mode={mode} upgradeHref="/account?tab=plans" />
          </MemoryRouter>,
        );

        expect(screen.getByText('Data export / import')).toBeInTheDocument();
        expect(screen.getByRole('link', { name: /view plans/i }))
          .toHaveAttribute('href', '/account?tab=plans');
        // Neither the format form nor the action button renders.
        expect(screen.queryByRole('button', { name: /^(export|import)$/i })).not.toBeInTheDocument();
      },
    );

    it('omits the CTA when no plans page is configured (Community)', () => {
      render(<ImportExportDialog {...defaultProps} />);

      expect(screen.getByText(/professional and enterprise/i)).toBeInTheDocument();
      expect(screen.queryByRole('link', { name: /view plans/i })).not.toBeInTheDocument();
    });
  });
});
