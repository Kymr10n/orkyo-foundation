import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { CreateSiteDialog } from './CreateSiteDialog';

const mockMutateAsync = vi.fn(() =>
  Promise.resolve({ id: 'new', code: 'hq', name: 'Headquarters' }),
);

vi.mock('@foundation/src/components/ui/dialog', () => ({
  Dialog: ({ children, open }: { children: ReactNode; open: boolean }) => open ? <div role="dialog">{children}</div> : null,
  DialogContent: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DialogHeader: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DialogTitle: ({ children }: { children: ReactNode }) => <h2>{children}</h2>,
  DialogDescription: ({ children }: { children: ReactNode }) => <p>{children}</p>,
  DialogFooter: ({ children }: { children: ReactNode }) => <div>{children}</div>,
}));

vi.mock('@foundation/src/components/ui/ErrorAlert', () => ({
  ErrorAlert: ({ message }: { message: string | null }) => message ? <div role="alert">{message}</div> : null,
}));

vi.mock('@foundation/src/components/ui/DialogFormFooter', () => ({
  DialogFormFooter: ({ submitLabel, onCancel, isSubmitting }: { submitLabel: string; onCancel: () => void; isSubmitting: boolean }) => (
    <div>
      <button type="button" onClick={onCancel}>Cancel</button>
      <button type="submit" disabled={isSubmitting}>{submitLabel || 'Create'}</button>
    </div>
  ),
}));

vi.mock('@foundation/src/hooks/useSites', () => ({
  useCreateSite: () => ({
    mutateAsync: mockMutateAsync,
    isPending: false,
  }),
}));

vi.mock('@foundation/src/lib/utils', async (importOriginal) => {
  const actual = await importOriginal<Record<string, unknown>>();
  return {
    ...actual,
    isValidSlug: (s: string) => /^[a-z][a-z0-9-]*$/.test(s),
  };
});

describe('CreateSiteDialog', () => {
  const defaultProps = {
    open: true,
    onOpenChange: vi.fn(),
    onSuccess: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders dialog with title', () => {
    render(<CreateSiteDialog {...defaultProps} />);
    expect(screen.getByRole('heading', { name: 'Create Site' })).toBeInTheDocument();
  });

  it('renders code, name, description, and address fields', () => {
    render(<CreateSiteDialog {...defaultProps} />);
    expect(screen.getByLabelText(/code/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/name/i)).toBeInTheDocument();
  });

  it('does not render when closed', () => {
    const { container } = render(<CreateSiteDialog {...defaultProps} open={false} />);
    expect(container.querySelector('[role="dialog"]')).toBeNull();
  });

  it('submits form with valid data', async () => {
    render(<CreateSiteDialog {...defaultProps} />);
    fireEvent.change(screen.getByLabelText(/code/i), { target: { value: 'hq-01' } });
    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Headquarters' } });
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

    await waitFor(() => {
      expect(mockMutateAsync).toHaveBeenCalledWith({
        code: 'hq-01',
        name: 'Headquarters',
        description: undefined,
        address: undefined,
      });
    });
  });

  it('shows validation error when code is empty', async () => {
    render(<CreateSiteDialog {...defaultProps} />);
    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Test' } });
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(/code is required/i);
    });
  });

  it('shows validation error when name is empty', async () => {
    render(<CreateSiteDialog {...defaultProps} />);
    fireEvent.change(screen.getByLabelText(/code/i), { target: { value: 'valid-code' } });
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(/name is required/i);
    });
  });
});
