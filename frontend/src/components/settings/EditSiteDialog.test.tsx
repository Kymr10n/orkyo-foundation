import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { EditSiteDialog } from './EditSiteDialog';

vi.mock('@/components/ui/dialog', () => ({
  Dialog: ({ children, open }: { children: ReactNode; open: boolean }) => open ? <div role="dialog">{children}</div> : null,
  DialogContent: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DialogHeader: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DialogTitle: ({ children }: { children: ReactNode }) => <h2>{children}</h2>,
  DialogDescription: ({ children }: { children: ReactNode }) => <p>{children}</p>,
  DialogFooter: ({ children }: { children: ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/components/ui/ErrorAlert', () => ({
  ErrorAlert: ({ message }: { message: string | null }) => message ? <div role="alert">{message}</div> : null,
}));

vi.mock('@/components/ui/DialogFormFooter', () => ({
  DialogFormFooter: ({ submitLabel, onCancel, isSubmitting }: { submitLabel: string; onCancel: () => void; isSubmitting: boolean }) => (
    <div>
      <button type="button" onClick={onCancel}>Cancel</button>
      <button type="submit" disabled={isSubmitting}>{submitLabel || 'Save'}</button>
    </div>
  ),
}));

const mockUpdateMutateAsync = vi.fn(() =>
  Promise.resolve({ id: 's1', code: 'hq', name: 'Updated HQ', createdAt: '2024-01-01T00:00:00Z', updatedAt: '2024-01-02T00:00:00Z' }),
);

vi.mock('@/hooks/useSites', () => ({
  useUpdateSite: () => ({
    mutateAsync: mockUpdateMutateAsync,
    isPending: false,
  }),
}));

describe('EditSiteDialog', () => {
  const site = {
    id: 's1',
    code: 'hq',
    name: 'Headquarters',
    description: 'Main office',
    address: '123 Main St',
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
  };

  const defaultProps = {
    site,
    open: true,
    onOpenChange: vi.fn(),
    onSuccess: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders dialog with edit title', () => {
    render(<EditSiteDialog {...defaultProps} />);
    expect(screen.getByText('Edit Site')).toBeInTheDocument();
  });

  it('shows code as disabled field', () => {
    render(<EditSiteDialog {...defaultProps} />);
    const codeInput = screen.getByDisplayValue('hq');
    expect(codeInput).toBeDisabled();
  });

  it('pre-fills name, description, and address', () => {
    render(<EditSiteDialog {...defaultProps} />);
    expect(screen.getByDisplayValue('Headquarters')).toBeInTheDocument();
    expect(screen.getByDisplayValue('Main office')).toBeInTheDocument();
    expect(screen.getByDisplayValue('123 Main St')).toBeInTheDocument();
  });

  it('validates empty name on submit', async () => {
    render(<EditSiteDialog {...defaultProps} />);
    const nameInput = screen.getByDisplayValue('Headquarters');
    fireEvent.change(nameInput, { target: { value: '' } });
    fireEvent.submit(nameInput.closest('form')!);
    await waitFor(() => {
      expect(screen.getByText('Name is required')).toBeInTheDocument();
    });
  });

  it('does not render when closed', () => {
    render(<EditSiteDialog {...defaultProps} open={false} />);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('submits updated site successfully', async () => {
    render(<EditSiteDialog {...defaultProps} />);
    const nameInput = screen.getByDisplayValue('Headquarters');
    fireEvent.change(nameInput, { target: { value: 'New HQ' } });
    fireEvent.submit(nameInput.closest('form')!);
    await waitFor(() => {
      expect(mockUpdateMutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({
          id: 's1',
          data: expect.objectContaining({ name: 'New HQ' }),
        }),
      );
      expect(defaultProps.onSuccess).toHaveBeenCalled();
      expect(defaultProps.onOpenChange).toHaveBeenCalledWith(false);
    });
  });

  it('shows error on update failure', async () => {
    mockUpdateMutateAsync.mockRejectedValueOnce(new Error('Update failed'));
    render(<EditSiteDialog {...defaultProps} />);
    fireEvent.submit(screen.getByDisplayValue('Headquarters').closest('form')!);
    await waitFor(() => {
      expect(screen.getByText('Update failed')).toBeInTheDocument();
    });
  });

  it('calls onOpenChange on cancel', () => {
    render(<EditSiteDialog {...defaultProps} />);
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(defaultProps.onOpenChange).toHaveBeenCalledWith(false);
  });
});
