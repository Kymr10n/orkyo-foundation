import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { CreateCriterionDialog } from './CreateCriterionDialog';

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
      <button type="submit" disabled={isSubmitting}>{submitLabel || 'Create'}</button>
    </div>
  ),
}));

const mockMutateAsync = vi.fn(() =>
  Promise.resolve({ id: 'new-id', name: 'Test', dataType: 'Boolean', description: '', unit: null, enumValues: [] }),
);

vi.mock('@/hooks/useCriteria', () => ({
  useCreateCriterion: () => ({
    mutateAsync: mockMutateAsync,
    isPending: false,
  }),
}));

vi.mock('@/lib/utils', async (importOriginal) => {
  const actual = await importOriginal<Record<string, unknown>>();
  return {
    ...actual,
    isValidSlug: (s: string) => /^[a-z][a-z0-9-]*$/.test(s),
  };
});

vi.mock('./EnumValueEditor', () => ({
  EnumValueEditor: () => <div data-testid="enum-editor" />,
}));

describe('CreateCriterionDialog', () => {
  const defaultProps = {
    open: true,
    onOpenChange: vi.fn(),
    onSuccess: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders dialog with title', () => {
    render(<CreateCriterionDialog {...defaultProps} />);
    expect(screen.getByRole('heading', { name: 'Create Criterion' })).toBeInTheDocument();
  });

  it('renders name, description, data type, and unit fields', () => {
    render(<CreateCriterionDialog {...defaultProps} />);
    expect(screen.getByLabelText(/name/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/description/i)).toBeInTheDocument();
  });

  it('does not render when closed', () => {
    const { container } = render(<CreateCriterionDialog {...defaultProps} open={false} />);
    expect(container.querySelector('[role="dialog"]')).toBeNull();
  });

  it('submits form with valid name', async () => {
    render(<CreateCriterionDialog {...defaultProps} />);
    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'test-criterion' } });
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

    await waitFor(() => {
      expect(mockMutateAsync).toHaveBeenCalledWith({
        name: 'test-criterion',
        description: undefined,
        dataType: 'Boolean',
        enumValues: undefined,
        unit: undefined,
      });
    });
  });

  it('shows validation error when name is empty', async () => {
    render(<CreateCriterionDialog {...defaultProps} />);
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(/name is required/i);
    });
  });

  it('shows slug validation error for invalid name', async () => {
    render(<CreateCriterionDialog {...defaultProps} />);
    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Invalid Name!' } });
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(/alphanumeric/i);
    });
  });

  it('shows error on mutation failure', async () => {
    mockMutateAsync.mockRejectedValueOnce(new Error('Create failed'));
    render(<CreateCriterionDialog {...defaultProps} />);
    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'valid-name' } });
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent('Create failed');
    });
  });

  it('calls onOpenChange on cancel', () => {
    render(<CreateCriterionDialog {...defaultProps} />);
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(defaultProps.onOpenChange).toHaveBeenCalledWith(false);
  });
});
