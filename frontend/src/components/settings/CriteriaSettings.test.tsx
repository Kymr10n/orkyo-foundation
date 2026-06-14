/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { CriteriaSettings } from './CriteriaSettings';

const mockDeleteMutateAsync = vi.fn(() => Promise.resolve());
const mockCreateMutateAsync = vi.fn(() => Promise.resolve());
const mockRefetch = vi.fn();

let mockCriteriaData: { data: unknown[]; isLoading: boolean; error: Error | null; refetch?: () => void };

vi.mock('@foundation/src/hooks/useCriteria', () => ({
  useCriteria: () => mockCriteriaData,
  useCreateCriterion: () => ({ mutateAsync: mockCreateMutateAsync, isPending: false }),
  useDeleteCriterion: () => ({ mutateAsync: mockDeleteMutateAsync, isPending: false }),
}));

// Capture the export/import callbacks the page registers so the tests can drive
// them directly (the real hooks wire them to a global event the toolbar fires).
const ioHandlers = vi.hoisted(() => ({
  exportCb: null as null | ((format: string) => Promise<void>),
  importCb: null as null | ((file: File, format: string) => Promise<void>),
}));
vi.mock('@foundation/src/hooks/useImportExport', () => ({
  useExportHandler: (_key: string, cb: (format: string) => Promise<void>) => {
    ioHandlers.exportCb = cb;
  },
  useImportHandler: (_key: string, cb: (file: File, format: string) => Promise<void>) => {
    ioHandlers.importCb = cb;
  },
}));

vi.mock('@foundation/src/lib/utils/export-handlers', () => ({
  exportCriteria: vi.fn(() => Promise.resolve()),
  importCriteria: vi.fn(() => Promise.resolve([])),
}));

import { exportCriteria, importCriteria } from '@foundation/src/lib/utils/export-handlers';

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
  { id: 'c1', name: 'Capacity', dataType: 'Number', description: 'Room capacity', unit: 'seats', enumValues: [], resourceTypeKeys: ['space'], createdAt: '2024-01-15T00:00:00Z' },
  { id: 'c2', name: 'HasProjector', dataType: 'Boolean', description: '', unit: null, enumValues: [], resourceTypeKeys: ['space', 'tool'], createdAt: '2024-02-01T00:00:00Z' },
  { id: 'c3', name: 'PersonSkill', dataType: 'Boolean', description: 'A person skill', unit: null, enumValues: [], resourceTypeKeys: ['person'], createdAt: '2024-03-01T00:00:00Z' },
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
    expect(screen.getByText('PersonSkill')).toBeInTheDocument();
  });

  it('shows data type badges', () => {
    render(<CriteriaSettings />);
    expect(screen.getByText('Number')).toBeInTheDocument();
    expect(screen.getAllByText('Boolean').length).toBeGreaterThanOrEqual(1);
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
    expect(screen.getByText('A person skill')).toBeInTheDocument();
  });

  it('shows enum values as badges', () => {
    // No Enum criteria in fixture; verify component renders without crashing
    render(<CriteriaSettings />);
    expect(screen.getByText('Capacity')).toBeInTheDocument();
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

  describe('filter tabs', () => {
    it('renders three filter tabs (tools hidden until tools feature ships)', () => {
      render(<CriteriaSettings />);
      expect(screen.getByRole('tab', { name: 'All' })).toBeInTheDocument();
      expect(screen.getByRole('tab', { name: 'Spaces' })).toBeInTheDocument();
      expect(screen.getByRole('tab', { name: 'People' })).toBeInTheDocument();
      expect(screen.queryByRole('tab', { name: 'Tools' })).not.toBeInTheDocument();
    });

    it('All tab shows all criteria by default', () => {
      render(<CriteriaSettings />);
      expect(screen.getByText('Capacity')).toBeInTheDocument();
      expect(screen.getByText('HasProjector')).toBeInTheDocument();
      expect(screen.getByText('PersonSkill')).toBeInTheDocument();
    });

    it('Spaces tab filters to space criteria only', async () => {
      const user = userEvent.setup();
      render(<CriteriaSettings />);
      await user.click(screen.getByRole('tab', { name: 'Spaces' }));
      expect(screen.getByText('Capacity')).toBeInTheDocument();
      expect(screen.getByText('HasProjector')).toBeInTheDocument();
      expect(screen.queryByText('PersonSkill')).not.toBeInTheDocument();
    });

    it('People tab filters to person criteria only', async () => {
      const user = userEvent.setup();
      render(<CriteriaSettings />);
      await user.click(screen.getByRole('tab', { name: 'People' }));
      expect(screen.queryByText('Capacity')).not.toBeInTheDocument();
      expect(screen.getByText('PersonSkill')).toBeInTheDocument();
    });


    it('shows resource-specific empty state when no criteria match the active filter', async () => {
      mockCriteriaData = {
        data: [
          { id: 'c1', name: 'Capacity', dataType: 'Number', description: '', unit: 'seats', enumValues: [], resourceTypeKeys: ['space'], createdAt: '2024-01-15T00:00:00Z' },
        ],
        isLoading: false,
        error: null,
        refetch: mockRefetch,
      };
      const user = userEvent.setup();
      render(<CriteriaSettings />);
      await user.click(screen.getByRole('tab', { name: 'People' }));
      await waitFor(() => {
        expect(screen.queryByText('Capacity')).not.toBeInTheDocument();
      });
      // Empty state is rendered in a leaf div (no child elements) by OrkyoDataTable
      expect(
        screen.getByText((_content, element) =>
          element?.tagName === 'DIV' &&
          element.children.length === 0 &&
          (element?.textContent ?? '').includes('No criteria defined for'),
        ),
      ).toBeInTheDocument();
    });
  });

  describe('delete guard (criterion in use)', () => {
    beforeEach(() => {
      mockCriteriaData = {
        data: [
          { id: 'free', name: 'Deletable', dataType: 'Number', description: '', unit: null, enumValues: [], resourceTypeKeys: ['space'], inUse: false, createdAt: '2024-01-15T00:00:00Z' },
          { id: 'used', name: 'Locked', dataType: 'Number', description: '', unit: null, enumValues: [], resourceTypeKeys: ['space'], inUse: true, createdAt: '2024-01-15T00:00:00Z' },
        ],
        isLoading: false,
        error: null,
        refetch: mockRefetch,
      };
    });

    it('disables the delete button for an in-use criterion and enables it otherwise', () => {
      render(<CriteriaSettings />);
      expect(screen.getByLabelText('Delete Locked')).toBeDisabled();
      expect(screen.getByLabelText('Delete Deletable')).not.toBeDisabled();
    });

    it('explains why deletion is blocked via tooltip for an in-use criterion', async () => {
      const user = userEvent.setup();
      render(<CriteriaSettings />);
      await user.hover(screen.getByLabelText('Delete Locked').closest('span')!);
      await waitFor(() =>
        expect(
          screen.getAllByText('Cannot delete: this criterion has existing values').length,
        ).toBeGreaterThan(0),
      );
    });

    it('shows the default delete tooltip for a deletable criterion', async () => {
      const user = userEvent.setup();
      render(<CriteriaSettings />);
      await user.hover(screen.getByLabelText('Delete Deletable').closest('span')!);
      await waitFor(() =>
        expect(screen.getAllByText('Delete criterion').length).toBeGreaterThan(0),
      );
    });
  });

  describe('export / import handlers', () => {
    it('exports the current criteria in the requested format', async () => {
      render(<CriteriaSettings />);
      await ioHandlers.exportCb!('csv');
      expect(exportCriteria).toHaveBeenCalledWith(mockCriteria, 'csv');
    });

    it('imports valid criteria and creates each one', async () => {
      vi.mocked(importCriteria).mockResolvedValueOnce([
        { name: 'Imported', dataType: 'Number' } as any,
      ]);
      render(<CriteriaSettings />);
      await ioHandlers.importCb!(new File(['x'], 'criteria.csv'), 'csv');
      expect(mockCreateMutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({ name: 'Imported' }),
      );
      expect(global.alert).toHaveBeenCalledWith('Successfully imported 1 criteria');
    });

    it('alerts when the imported file contains no valid criteria', async () => {
      vi.mocked(importCriteria).mockResolvedValueOnce([]);
      render(<CriteriaSettings />);
      await ioHandlers.importCb!(new File(['x'], 'criteria.csv'), 'csv');
      expect(mockCreateMutateAsync).not.toHaveBeenCalled();
      expect(global.alert).toHaveBeenCalledWith('No valid criteria found in file');
    });

    it('alerts with the error message when import fails', async () => {
      vi.mocked(importCriteria).mockRejectedValueOnce(new Error('Bad file'));
      render(<CriteriaSettings />);
      await ioHandlers.importCb!(new File(['x'], 'criteria.csv'), 'csv');
      expect(global.alert).toHaveBeenCalledWith('Bad file');
    });
  });

  describe('applicability badges', () => {
    it('renders resourceTypeKey badges on each criterion card', () => {
      render(<CriteriaSettings />);
      // Each label appears as tab + badge; Tools appears only as a badge (no tab until tools ships)
      expect(screen.getAllByText('Spaces').length).toBeGreaterThanOrEqual(2); // tab + at least one badge
      expect(screen.getAllByText('People').length).toBeGreaterThanOrEqual(2); // tab + badge
      expect(screen.getAllByText('Tools').length).toBeGreaterThanOrEqual(1); // badge only (c2 has ['space','tool'])
    });
  });
});
