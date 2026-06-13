import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import type { ResourceTypeKey } from '@foundation/src/types/criterion';
import { EditCriterionDialog } from './EditCriterionDialog';

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
  DialogFormFooter: () => <div><button type="submit">Save</button></div>,
}));

const mockMutateAsync = vi.fn(() =>
  Promise.resolve({ id: 'c1', name: 'Capacity', dataType: 'Number', description: 'Updated', unit: 'seats', enumValues: [] }),
);

const mockApplicabilityMutateAsync = vi.fn(() =>
  Promise.resolve({ criterionId: 'c1', applicableToRequests: true, resourceTypeKeys: ['space'] }),
);

vi.mock('@foundation/src/hooks/useCriteria', () => ({
  useUpdateCriterion: () => ({
    mutateAsync: mockMutateAsync,
    isPending: false,
  }),
  useUpdateCriterionApplicability: () => ({
    mutateAsync: mockApplicabilityMutateAsync,
    isPending: false,
  }),
}));

vi.mock('./EnumValueEditor', () => ({
  EnumValueEditor: () => <div data-testid="enum-editor" />,
}));

// Render the Radix Select as a native <select> so dataType changes are driveable in jsdom.
vi.mock('@foundation/src/components/ui/select', () => ({
  Select: ({ value, onValueChange, children }: { value: string; onValueChange: (v: string) => void; children: ReactNode }) => (
    <select data-testid="datatype-select" value={value} onChange={(e) => onValueChange(e.target.value)}>
      {children}
    </select>
  ),
  SelectTrigger: () => null,
  SelectValue: () => null,
  SelectContent: ({ children }: { children: ReactNode }) => <>{children}</>,
  SelectItem: ({ value, children }: { value: string; children: ReactNode }) => <option value={value}>{children}</option>,
}));

describe('EditCriterionDialog', () => {
  const criterion = {
    id: 'c1',
    name: 'Capacity',
    dataType: 'Number' as const,
    description: 'Room capacity',
    unit: 'seats',
    enumValues: [],
    resourceTypeKeys: ['space'] as ResourceTypeKey[],
    inUse: false,
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

  it('shows criterion name as an editable input', () => {
    render(<EditCriterionDialog {...defaultProps} />);
    expect(screen.getByLabelText(/name/i)).toHaveValue('Capacity');
  });

  it('shows dataType select when criterion is not in use', () => {
    render(<EditCriterionDialog {...defaultProps} />);
    expect(screen.getByTestId('datatype-select')).toHaveValue('Number');
  });

  it('shows dataType as a locked badge when criterion is in use', () => {
    const inUseCriterion = { ...criterion, inUse: true };
    render(<EditCriterionDialog {...defaultProps} criterion={inUseCriterion} />);
    expect(screen.queryByTestId('datatype-select')).not.toBeInTheDocument();
    expect(screen.getByText('Number')).toBeInTheDocument();
    expect(screen.getByText(/locked because this criterion has existing values/i)).toBeInTheDocument();
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

  it('submits a name change when the name is edited', async () => {
    render(<EditCriterionDialog {...defaultProps} />);
    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Renamed' } });
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

    await waitFor(() => {
      expect(mockMutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({ id: 'c1', data: expect.objectContaining({ name: 'Renamed' }) }),
      );
    });
  });

  it('does not send name in the payload when name is unchanged', async () => {
    render(<EditCriterionDialog {...defaultProps} />);
    fireEvent.change(screen.getByLabelText(/description/i), { target: { value: 'Changed' } });
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

    await waitFor(() => {
      expect(mockMutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({ data: expect.not.objectContaining({ name: expect.anything() }) }),
      );
    });
  });

  it('includes dataType in the payload when changed', async () => {
    render(<EditCriterionDialog {...defaultProps} />);
    fireEvent.change(screen.getByTestId('datatype-select'), { target: { value: 'Boolean' } });
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

    await waitFor(() => {
      expect(mockMutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({ id: 'c1', data: expect.objectContaining({ dataType: 'Boolean' }) }),
      );
    });
  });

  it('shows unit field for Number data type', () => {
    render(<EditCriterionDialog {...defaultProps} />);
    expect(screen.getByLabelText(/unit/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/unit/i)).toHaveValue('seats');
  });

  it('renders Applies to checkboxes for Spaces and People', () => {
    render(<EditCriterionDialog {...defaultProps} />);
    expect(screen.getByLabelText('Spaces')).toBeInTheDocument();
    expect(screen.getByLabelText('People')).toBeInTheDocument();
    expect(screen.queryByLabelText('Tools')).not.toBeInTheDocument();
  });

  it('pre-checks the checkboxes matching criterion resourceTypeKeys', () => {
    render(<EditCriterionDialog {...defaultProps} />);
    expect(screen.getByLabelText('Spaces')).toHaveAttribute('aria-checked', 'true');
    expect(screen.getByLabelText('People')).toHaveAttribute('aria-checked', 'false');
  });

  it('skips the criterion-detail update when only applicability changed', async () => {
    // Regression: a Boolean criterion with no description has nothing to PUT on the
    // criterion itself; firing it anyway returned 400 "No fields to update" and blocked
    // the applicability change. Only applicability should be sent here.
    const booleanCriterion = {
      ...criterion,
      dataType: 'Boolean' as const,
      description: '',
      unit: undefined,
      enumValues: [],
    };
    render(<EditCriterionDialog {...defaultProps} criterion={booleanCriterion} />);

    fireEvent.click(screen.getByLabelText('People')); // toggle applicability
    fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

    await waitFor(() => {
      expect(mockApplicabilityMutateAsync).toHaveBeenCalledWith({
        id: 'c1',
        data: { resourceTypeKeys: ['space', 'person'] },
      });
    });
    expect(mockMutateAsync).not.toHaveBeenCalled();
  });
});
