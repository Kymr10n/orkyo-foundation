import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { CriteriaSettings } from './CriteriaSettings';

const mockDeleteMutateAsync = vi.fn(() => Promise.resolve());
const mockRefetch = vi.fn();

let mockCriteriaData: { data: unknown[]; isLoading: boolean; error: Error | null; refetch?: () => void };

vi.mock('@/hooks/useCriteria', () => ({
  useCriteria: () => mockCriteriaData,
  useCreateCriterion: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useDeleteCriterion: () => ({ mutateAsync: mockDeleteMutateAsync, isPending: false }),
}));

vi.mock('@/hooks/useImportExport', () => ({
  useExportHandler: vi.fn(),
  useImportHandler: vi.fn(),
}));

vi.mock('@/lib/utils/export-handlers', () => ({
  exportCriteria: vi.fn(),
  importCriteria: vi.fn(),
}));

vi.mock('@/lib/utils', async (importOriginal) => {
  const actual = await importOriginal<Record<string, unknown>>();
  return {
    ...actual,
    getDataTypeColor: () => 'bg-blue-100 text-blue-800',
  };
});

vi.mock('./CreateCriterionDialog', () => ({
  CreateCriterionDialog: () => null,
}));

vi.mock('./EditCriterionDialog', () => ({
  EditCriterionDialog: () => null,
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
});
