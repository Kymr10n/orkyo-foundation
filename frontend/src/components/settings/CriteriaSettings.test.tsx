/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { CriteriaSettings } from './CriteriaSettings';

const mockDeleteMutateAsync = vi.fn(() => Promise.resolve());
const mockRefetch = vi.fn();

let mockCriteriaData: { data: unknown[]; isLoading: boolean; error: Error | null; refetch?: () => void };

vi.mock('@foundation/src/hooks/useCriteria', () => ({
  useCriteria: () => mockCriteriaData,
  useCreateCriterion: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useDeleteCriterion: () => ({ mutateAsync: mockDeleteMutateAsync, isPending: false }),
}));

vi.mock('@foundation/src/hooks/useImportExport', () => ({
  useExportHandler: vi.fn(),
  useImportHandler: vi.fn(),
}));

vi.mock('@foundation/src/lib/utils/export-handlers', () => ({
  exportCriteria: vi.fn(),
  importCriteria: vi.fn(),
}));

vi.mock('@foundation/src/lib/utils', async (importOriginal) => {
  const actual = await importOriginal<Record<string, unknown>>();
  return {
    ...actual,
    getDataTypeColor: () => 'bg-blue-100 text-blue-800',
  };
});

vi.mock('./CreateCriterionDialog', () => ({
  CreateCriterionDialog: ({ open, onSuccess }: any) =>
    open ? <button data-testid="create-success-btn" onClick={() => onSuccess({})}>Confirm Create</button> : null,
}));

vi.mock('./EditCriterionDialog', () => ({
  EditCriterionDialog: ({ open, onSuccess }: any) =>
    open ? <button data-testid="edit-success-btn" onClick={() => onSuccess({})}>Confirm Edit</button> : null,
}));

const mockCriteria = [
  { id: 'c1', name: 'Capacity', dataType: 'Number', description: 'Room capacity', unit: 'seats', enumValues: [], createdAt: '2024-01-15T00:00:00Z' },
  { id: 'c2', name: 'HasProjector', dataType: 'Boolean', description: '', unit: null, enumValues: [], createdAt: '2024-02-01T00:00:00Z' },
  { id: 'c3', name: 'RoomType', dataType: 'Enum', description: 'Type of room', unit: null, enumValues: ['Conference', 'Classroom', 'Lab'], createdAt: '2024-03-01T00:00:00Z' },
];

describe('CriteriaSettings', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockCriteriaData = { data: mockCriteria, isLoading: false, error: null };
    mockCriteriaData.refetch = mockRefetch;
    global.confirm = vi.fn(() => true);
    global.alert = vi.fn();
  });

  it('renders header and create button', () => {
    render(<CriteriaSettings />);
    expect(screen.getByText('Criteria Definitions')).toBeInTheDocument();
    expect(screen.getByText('Add Criterion')).toBeInTheDocument();
  });

  it('renders criteria list', () => {
    render(<CriteriaSettings />);
    expect(screen.getByText('Capacity')).toBeInTheDocument();
    expect(screen.getByText('HasProjector')).toBeInTheDocument();
    expect(screen.getByText('RoomType')).toBeInTheDocument();
  });

  it('shows data type badges', () => {
    render(<CriteriaSettings />);
    expect(screen.getByText('Number')).toBeInTheDocument();
    expect(screen.getByText('Boolean')).toBeInTheDocument();
    expect(screen.getByText('Enum')).toBeInTheDocument();
  });

  it('shows loading state', () => {
    mockCriteriaData = { data: [], isLoading: true, error: null };
    render(<CriteriaSettings />);
    expect(screen.getByText('Loading criteria...')).toBeInTheDocument();
  });

  it('shows empty state when no criteria', () => {
    mockCriteriaData = { data: [], isLoading: false, error: null };
    render(<CriteriaSettings />);
    expect(screen.getByText('No criteria defined yet')).toBeInTheDocument();
    expect(screen.getByText('Create your first criterion')).toBeInTheDocument();
  });

  it('shows error state', () => {
    mockCriteriaData = { data: [], isLoading: false, error: new Error('Network error') };
    mockCriteriaData.refetch = mockRefetch;
    render(<CriteriaSettings />);
    expect(screen.getByText('Network error')).toBeInTheDocument();
    expect(screen.getByText('Retry')).toBeInTheDocument();
  });

  it('shows unit for Number criteria', () => {
    render(<CriteriaSettings />);
    expect(screen.getByText('(seats)')).toBeInTheDocument();
  });

  it('shows description when present', () => {
    render(<CriteriaSettings />);
    expect(screen.getByText('Room capacity')).toBeInTheDocument();
    expect(screen.getByText('Type of room')).toBeInTheDocument();
  });

  it('shows enum values as badges', () => {
    render(<CriteriaSettings />);
    expect(screen.getByText('Conference')).toBeInTheDocument();
    expect(screen.getByText('Classroom')).toBeInTheDocument();
    expect(screen.getByText('Lab')).toBeInTheDocument();
  });

  it('deletes a criterion with confirmation', async () => {
    const user = userEvent.setup();
    render(<CriteriaSettings />);
    const deleteButtons = screen.getAllByRole('button').filter(b => b.querySelector('.text-destructive'));
    await user.click(deleteButtons[0]);
    await waitFor(() => {
      expect(global.confirm).toHaveBeenCalledWith(expect.stringContaining('Capacity'));
      expect(mockDeleteMutateAsync).toHaveBeenCalledWith('c1');
    });
  });

  it('does not delete when confirmation is declined', async () => {
    global.confirm = vi.fn(() => false);
    const user = userEvent.setup();
    render(<CriteriaSettings />);
    const deleteButtons = screen.getAllByRole('button').filter(b => b.querySelector('.text-destructive'));
    await user.click(deleteButtons[0]);
    expect(mockDeleteMutateAsync).not.toHaveBeenCalled();
  });

  it('shows alert on delete error', async () => {
    mockDeleteMutateAsync.mockRejectedValueOnce(new Error('Delete failed'));
    const user = userEvent.setup();
    render(<CriteriaSettings />);
    const deleteButtons = screen.getAllByRole('button').filter(b => b.querySelector('.text-destructive'));
    await user.click(deleteButtons[0]);
    await waitFor(() => {
      expect(global.alert).toHaveBeenCalledWith('Delete failed');
    });
  });

  it('clicking Add Criterion button opens create dialog', async () => {
    const user = userEvent.setup();
    render(<CriteriaSettings />);
    const addBtn = screen.getAllByRole('button').find(b => b.textContent?.includes('Add') || b.textContent?.includes('Criterion') || b.textContent?.includes('Create'));
    if (addBtn) await user.click(addBtn);
    await waitFor(() => expect(screen.getByTestId('create-success-btn')).toBeInTheDocument());
  });

  it('handleCreateSuccess closes create dialog when onSuccess called', async () => {
    const user = userEvent.setup();
    render(<CriteriaSettings />);
    const addBtn = screen.getAllByRole('button').find(b => b.textContent?.includes('Add') || b.textContent?.includes('Criterion') || b.textContent?.includes('Create'));
    if (addBtn) await user.click(addBtn);
    await waitFor(() => screen.getByTestId('create-success-btn'));
    await user.click(screen.getByTestId('create-success-btn'));
    await waitFor(() => expect(screen.queryByTestId('create-success-btn')).not.toBeInTheDocument());
  });

  it('clicking edit icon opens edit dialog (setEditingCriterion)', async () => {
    const user = userEvent.setup();
    render(<CriteriaSettings />);
    // Find edit buttons (pencil/edit icon, not delete)
    const nonDestructiveIconBtns = screen.getAllByRole('button').filter(
      b => !b.querySelector('.text-destructive') && !b.textContent?.trim() && b.querySelector('svg'),
    );
    if (nonDestructiveIconBtns.length > 0) await user.click(nonDestructiveIconBtns[0]);
    await waitFor(() => expect(screen.getByTestId('edit-success-btn')).toBeInTheDocument());
  });

  it('handleUpdateSuccess closes edit dialog when onSuccess called', async () => {
    const user = userEvent.setup();
    render(<CriteriaSettings />);
    const editBtns = screen.getAllByRole('button').filter(
      b => !b.querySelector('.text-destructive') && !b.textContent?.trim() && b.querySelector('svg'),
    );
    if (editBtns.length > 0) await user.click(editBtns[0]);
    await waitFor(() => screen.getByTestId('edit-success-btn'));
    await user.click(screen.getByTestId('edit-success-btn'));
    await waitFor(() => expect(screen.queryByTestId('edit-success-btn')).not.toBeInTheDocument());
  });

  it('Retry button in error state calls refetch', async () => {
    mockCriteriaData = { data: [], isLoading: false, error: new Error('Failed'), refetch: mockRefetch };
    const user = userEvent.setup();
    render(<CriteriaSettings />);
    await user.click(screen.getByRole('button', { name: /Retry/i }));
    expect(mockRefetch).toHaveBeenCalled();
  });
});
