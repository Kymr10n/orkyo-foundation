import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
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
});
