import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import type { ResourceTypeKey } from '@foundation/src/types/criterion';
import { CriterionEditDialog } from './CriterionEditDialog';

vi.mock('@foundation/src/components/ui/dialog', () => ({
  DIALOG_SIZE: { sm: '', md: '', lg: '', xl: '' },
  Dialog: ({ children, open }: { children: ReactNode; open: boolean }) => open ? <div role="dialog">{children}</div> : null,
  DialogContent: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  ScrollableDialogBody: ({ children }: { children: ReactNode }) => <div>{children}</div>,
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
      <button type="submit" disabled={isSubmitting}>{submitLabel || 'Save'}</button>
    </div>
  ),
}));

const mockCreateMutateAsync = vi.fn(() =>
  Promise.resolve({ id: 'new-id', name: 'Test', dataType: 'Boolean', description: '', unit: null, enumValues: [] }),
);
const mockUpdateMutateAsync = vi.fn(() =>
  Promise.resolve({ id: 'c1', name: 'Capacity', dataType: 'Number', description: 'Updated', unit: 'seats', enumValues: [] }),
);
const mockApplicabilityMutateAsync = vi.fn(() =>
  Promise.resolve({ criterionId: 'c1', applicableToRequests: true, resourceTypeKeys: ['space'] }),
);

vi.mock('@foundation/src/hooks/useCriteria', () => ({
  useCreateCriterion: () => ({
    mutateAsync: mockCreateMutateAsync,
    isPending: false,
  }),
  useUpdateCriterion: () => ({
    mutateAsync: mockUpdateMutateAsync,
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

describe('CriterionEditDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('create mode (criterion=null)', () => {
    const defaultProps = {
      criterion: null,
      open: true,
      onOpenChange: vi.fn(),
    };

    it('renders dialog with create title', () => {
      render(<CriterionEditDialog {...defaultProps} />);
      expect(screen.getByRole('heading', { name: 'Create Criterion' })).toBeInTheDocument();
    });

    it('renders name, description, data type, and unit fields', () => {
      render(<CriterionEditDialog {...defaultProps} />);
      expect(screen.getByLabelText(/name/i)).toBeInTheDocument();
      expect(screen.getByLabelText(/description/i)).toBeInTheDocument();
    });

    it('does not render when closed', () => {
      const { container } = render(<CriterionEditDialog {...defaultProps} open={false} />);
      expect(container.querySelector('[role="dialog"]')).toBeNull();
    });

    it('submits form with a display name containing spaces', async () => {
      render(<CriterionEditDialog {...defaultProps} />);
      fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Project Management' } });
      fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

      await waitFor(() => {
        expect(mockCreateMutateAsync).toHaveBeenCalledWith({
          name: 'Project Management',
          description: undefined,
          dataType: 'Boolean',
          enumValues: undefined,
          unit: undefined,
          resourceTypeKeys: ['space'],
        });
      });
    });

    it('shows validation error when name is empty', async () => {
      render(<CriterionEditDialog {...defaultProps} />);
      fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

      await waitFor(() => {
        expect(screen.getByRole('alert')).toHaveTextContent(/name is required/i);
      });
    });

    it('shows error on mutation failure', async () => {
      mockCreateMutateAsync.mockRejectedValueOnce(new Error('Create failed'));
      render(<CriterionEditDialog {...defaultProps} />);
      fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'valid-name' } });
      fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

      await waitFor(() => {
        expect(screen.getByRole('alert')).toHaveTextContent('Create failed');
      });
    });

    it('calls onOpenChange on cancel', () => {
      render(<CriterionEditDialog {...defaultProps} />);
      fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
      expect(defaultProps.onOpenChange).toHaveBeenCalledWith(false);
    });

    it('renders Applies to checkboxes for Spaces and People', () => {
      render(<CriterionEditDialog {...defaultProps} />);
      expect(screen.getByLabelText('Spaces')).toBeInTheDocument();
      expect(screen.getByLabelText('People')).toBeInTheDocument();
      expect(screen.queryByLabelText('Tools')).not.toBeInTheDocument();
    });

    it('defaults Spaces checkbox to checked', () => {
      render(<CriterionEditDialog {...defaultProps} />);
      const spacesCheckbox = screen.getByLabelText('Spaces');
      expect(spacesCheckbox).toHaveAttribute('aria-checked', 'true');
    });

    it('defaults to defaultResourceType when provided', () => {
      render(<CriterionEditDialog {...defaultProps} defaultResourceType="person" />);
      expect(screen.getByLabelText('People')).toHaveAttribute('aria-checked', 'true');
      expect(screen.getByLabelText('Spaces')).toHaveAttribute('aria-checked', 'false');
    });

    it('shows validation error when no applicability is selected', async () => {
      const user = userEvent.setup();
      render(<CriterionEditDialog {...defaultProps} />);
      // Uncheck Spaces (the default)
      await user.click(screen.getByLabelText('Spaces'));
      fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'valid-criterion' } });
      fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

      await waitFor(() => {
        expect(screen.getByRole('alert')).toHaveTextContent(/at least one/i);
      });
    });

    it('calls onSaved with the created criterion and closes the dialog', async () => {
      const onSaved = vi.fn();
      const onOpenChange = vi.fn();
      render(<CriterionEditDialog {...defaultProps} onSaved={onSaved} onOpenChange={onOpenChange} />);
      fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Project Management' } });
      fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

      await waitFor(() => {
        expect(onSaved).toHaveBeenCalledWith(
          expect.objectContaining({ id: 'new-id', name: 'Test' }),
        );
        expect(onOpenChange).toHaveBeenCalledWith(false);
      });
    });
  });

  describe('edit mode (criterion set)', () => {
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
    };

    it('renders dialog with edit title', () => {
      render(<CriterionEditDialog {...defaultProps} />);
      expect(screen.getByText('Edit Criterion')).toBeInTheDocument();
    });

    it('shows criterion name as an editable input', () => {
      render(<CriterionEditDialog {...defaultProps} />);
      expect(screen.getByLabelText(/name/i)).toHaveValue('Capacity');
    });

    it('shows dataType select when criterion is not in use', () => {
      render(<CriterionEditDialog {...defaultProps} />);
      expect(screen.getByTestId('datatype-select')).toHaveValue('Number');
    });

    it('shows dataType as a locked badge when criterion is in use', () => {
      const inUseCriterion = { ...criterion, inUse: true };
      render(<CriterionEditDialog {...defaultProps} criterion={inUseCriterion} />);
      expect(screen.queryByTestId('datatype-select')).not.toBeInTheDocument();
      expect(screen.getByText('Number')).toBeInTheDocument();
      expect(screen.getByText(/locked because this criterion has existing values/i)).toBeInTheDocument();
    });

    it('submits updated description', async () => {
      render(<CriterionEditDialog {...defaultProps} />);
      fireEvent.change(screen.getByLabelText(/description/i), { target: { value: 'Updated desc' } });
      fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

      await waitFor(() => {
        expect(mockUpdateMutateAsync).toHaveBeenCalledWith({
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
      render(<CriterionEditDialog {...defaultProps} />);
      fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Renamed' } });
      fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

      await waitFor(() => {
        expect(mockUpdateMutateAsync).toHaveBeenCalledWith(
          expect.objectContaining({ id: 'c1', data: expect.objectContaining({ name: 'Renamed' }) }),
        );
      });
    });

    it('does not send name in the payload when name is unchanged', async () => {
      render(<CriterionEditDialog {...defaultProps} />);
      fireEvent.change(screen.getByLabelText(/description/i), { target: { value: 'Changed' } });
      fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

      await waitFor(() => {
        expect(mockUpdateMutateAsync).toHaveBeenCalledWith(
          expect.objectContaining({ data: expect.not.objectContaining({ name: expect.anything() }) }),
        );
      });
    });

    it('includes dataType in the payload when changed', async () => {
      render(<CriterionEditDialog {...defaultProps} />);
      fireEvent.change(screen.getByTestId('datatype-select'), { target: { value: 'Boolean' } });
      fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

      await waitFor(() => {
        expect(mockUpdateMutateAsync).toHaveBeenCalledWith(
          expect.objectContaining({ id: 'c1', data: expect.objectContaining({ dataType: 'Boolean' }) }),
        );
      });
    });

    it('shows unit field for Number data type', () => {
      render(<CriterionEditDialog {...defaultProps} />);
      expect(screen.getByLabelText(/unit/i)).toBeInTheDocument();
      expect(screen.getByLabelText(/unit/i)).toHaveValue('seats');
    });

    it('renders Applies to checkboxes for Spaces and People', () => {
      render(<CriterionEditDialog {...defaultProps} />);
      expect(screen.getByLabelText('Spaces')).toBeInTheDocument();
      expect(screen.getByLabelText('People')).toBeInTheDocument();
      expect(screen.queryByLabelText('Tools')).not.toBeInTheDocument();
    });

    it('pre-checks the checkboxes matching criterion resourceTypeKeys', () => {
      render(<CriterionEditDialog {...defaultProps} />);
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
      render(<CriterionEditDialog {...defaultProps} criterion={booleanCriterion} />);

      fireEvent.click(screen.getByLabelText('People')); // toggle applicability
      fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

      await waitFor(() => {
        expect(mockApplicabilityMutateAsync).toHaveBeenCalledWith({
          id: 'c1',
          data: { resourceTypeKeys: ['space', 'person'] },
        });
      });
      expect(mockUpdateMutateAsync).not.toHaveBeenCalled();
    });

    it('calls onSaved with the updated criterion and closes the dialog', async () => {
      const onSaved = vi.fn();
      const onOpenChange = vi.fn();
      render(<CriterionEditDialog {...defaultProps} onSaved={onSaved} onOpenChange={onOpenChange} />);
      fireEvent.change(screen.getByLabelText(/description/i), { target: { value: 'Updated desc' } });
      fireEvent.submit(screen.getByRole('dialog').querySelector('form')!);

      await waitFor(() => {
        expect(onSaved).toHaveBeenCalledWith(
          expect.objectContaining({ id: 'c1', name: 'Capacity' }),
        );
        expect(onOpenChange).toHaveBeenCalledWith(false);
      });
    });
  });
});
