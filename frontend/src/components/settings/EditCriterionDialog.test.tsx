import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { EditCriterionDialog } from './EditCriterionDialog';

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
  DialogFormFooter: () => <div><button type="submit">Save</button></div>,
}));

const mockMutateAsync = vi.fn(() =>
  Promise.resolve({ id: 'c1', name: 'Capacity', dataType: 'Number', description: 'Updated', unit: 'seats', enumValues: [] }),
);

vi.mock('@/hooks/useCriteria', () => ({
  useUpdateCriterion: () => ({
    mutateAsync: mockMutateAsync,
    isPending: false,
  }),
}));

vi.mock('./EnumValueEditor', () => ({
  EnumValueEditor: () => <div data-testid="enum-editor" />,
}));

describe('EditCriterionDialog', () => {
  const criterion = {
    id: 'c1',
    name: 'Capacity',
    dataType: 'Number' as const,
    description: 'Room capacity',
    unit: 'seats',
    enumValues: [],
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
  };

  const defaultProps = {
    criterion,
    open: true,
    onOpenChange: vi.fn(),
    onSuccess: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders dialog with edit title', () => {
    render(<EditCriterionDialog {...defaultProps} />);
    expect(screen.getByText('Edit Criterion')).toBeInTheDocument();
  });

  it('shows criterion name as read-only badge', () => {
    render(<EditCriterionDialog {...defaultProps} />);
    expect(screen.getByText('Capacity')).toBeInTheDocument();
  });

  it('shows data type as read-only badge', () => {
    render(<EditCriterionDialog {...defaultProps} />);
    expect(screen.getByText('Number')).toBeInTheDocument();
  });

  it('submits updated description', async () => {
    render(<EditCriterionDialog {...defaultProps} />);
    fireEvent.change(screen.getByLabelText(/description/i), { target: { value: 'Updated desc' } });
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

    await waitFor(() => {
      expect(mockMutateAsync).toHaveBeenCalledWith({
        id: 'c1',
        data: {
          description: 'Updated desc',
          enumValues: undefined,
          unit: 'seats',
        },
      });
    });
  });

  it('shows unit field for Number data type', () => {
    render(<EditCriterionDialog {...defaultProps} />);
    expect(screen.getByLabelText(/unit/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/unit/i)).toHaveValue('seats');
  });
});
