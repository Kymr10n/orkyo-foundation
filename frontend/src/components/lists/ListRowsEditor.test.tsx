/** @jsxImportSource react */
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ListRowsEditor } from './ListRowsEditor';
import type { ListColumn, ListRow } from '@foundation/src/lib/api/lists-api';

const getListRows = vi.fn();
const createListRow = vi.fn();
const updateListRow = vi.fn();
const deleteListRow = vi.fn();

vi.mock('@foundation/src/lib/api/lists-api', async (importOriginal) => {
  const actual = await importOriginal<Record<string, unknown>>();
  return {
    ...actual,
    getListRows: (...args: unknown[]) => getListRows(...args),
    createListRow: (...args: unknown[]) => createListRow(...args),
    updateListRow: (...args: unknown[]) => updateListRow(...args),
    deleteListRow: (...args: unknown[]) => deleteListRow(...args),
  };
});

const columns: ListColumn[] = [
  {
    id: 'c1',
    listDefinitionId: 'd1',
    key: 'note',
    label: 'Note',
    dataType: 'text',
    isRequired: false,
    sortOrder: 0,
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  },
];

const parentColumn: ListColumn = {
  id: 'c2',
  listDefinitionId: 'd1',
  key: 'parent',
  label: 'Parent',
  dataType: 'row_ref',
  isRequired: false,
  sortOrder: 1,
  isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
};

const existingRow: ListRow = {
  id: 'r1',
  listInstanceId: 'i1',
  values: { note: 'oil change' },
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
};

function renderEditor(props: Partial<React.ComponentProps<typeof ListRowsEditor>> = {}) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={client}>
      <ListRowsEditor columns={columns} instanceId="i1" {...props} />
    </QueryClientProvider>,
  );
}

describe('ListRowsEditor', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getListRows.mockResolvedValue([existingRow]);
    createListRow.mockResolvedValue({ ...existingRow, id: 'r2' });
    updateListRow.mockResolvedValue(existingRow);
    deleteListRow.mockResolvedValue(undefined);
  });

  it('renders the rows of the instance', async () => {
    renderEditor();

    expect(await screen.findByText('oil change')).toBeInTheDocument();
  });

  it('creates the instance before posting the first row, and posts to that id', async () => {
    // The regression this pins: the mutations bind their instance id at render, so a dialog
    // opened while the id is still null would post the row to a URL with no instance in it.
    const ensureInstanceId = vi.fn().mockResolvedValue('created-1');
    getListRows.mockResolvedValue([]);

    const user = userEvent.setup();
    renderEditor({ instanceId: null, ensureInstanceId });

    await user.click(screen.getByRole('button', { name: /add row/i }));

    await waitFor(() => expect(ensureInstanceId).toHaveBeenCalledTimes(1));

    await user.type(await screen.findByLabelText('Note'), 'first');
    await user.click(screen.getByRole('button', { name: 'Add' }));

    await waitFor(() => expect(createListRow).toHaveBeenCalled());
    expect(createListRow).toHaveBeenCalledWith('created-1', { values: { note: 'first' } });
  });

  it('does not create an instance when one already exists', async () => {
    const ensureInstanceId = vi.fn();
    const user = userEvent.setup();
    renderEditor({ ensureInstanceId });

    await user.click(screen.getByRole('button', { name: /add row/i }));

    expect(ensureInstanceId).not.toHaveBeenCalled();
  });

  it('edits an existing row through the same dialog', async () => {
    const user = userEvent.setup();
    renderEditor();

    await user.click(await screen.findByRole('button', { name: 'Edit row' }));
    const input = await screen.findByLabelText('Note');
    await user.clear(input);
    await user.type(input, 'new brakes');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(updateListRow).toHaveBeenCalled());
    expect(updateListRow).toHaveBeenCalledWith('i1', 'r1', { values: { note: 'new brakes' } });
  });

  it('confirms before deleting a row', async () => {
    const user = userEvent.setup();
    renderEditor();

    await user.click(await screen.findByRole('button', { name: 'Delete row' }));
    // The confirmation is the point: a destructive action does not fire on the first click.
    expect(deleteListRow).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'Delete' }));

    await waitFor(() => expect(deleteListRow).toHaveBeenCalledWith('i1', 'r1'));
  });

  it('offers no write affordances when read-only', async () => {
    renderEditor({ readOnly: true });

    expect(await screen.findByText('oil change')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /add row/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Edit row' })).not.toBeInTheDocument();
  });

  it('names the Add button after what one row is, when told', () => {
    renderEditor({ entityLabel: 'Department' });

    expect(screen.getByRole('button', { name: /add department/i })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /add row/i })).not.toBeInTheDocument();
  });

  it('falls back to "row" when the host has no better word', () => {
    renderEditor();

    expect(screen.getByRole('button', { name: /add row/i })).toBeInTheDocument();
  });

  it('renders a host toolbar in the action row, beside the button', () => {
    renderEditor({
      entityLabel: 'Department',
      toolbar: <button type="button">picker</button>,
    });

    // One toolbar rather than two stacked rows of chrome over the table.
    const add = screen.getByRole('button', { name: /add department/i });
    expect(add.parentElement).toContainElement(screen.getByRole('button', { name: 'picker' }));
  });

  it('keeps a host toolbar for a viewer, who has no Add button', () => {
    renderEditor({ readOnly: true, toolbar: <button type="button">picker</button> });

    expect(screen.getByRole('button', { name: 'picker' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /add/i })).not.toBeInTheDocument();
  });

  describe('a column that points at another row', () => {
    const parent: ListRow = { ...existingRow, id: 'r2', values: { note: 'Quality' } };
    const child: ListRow = { ...existingRow, id: 'r3', values: { note: 'Quality North', parent: 'r2' } };

    const renderTree = () =>
      renderEditor({ columns: [columns[0], parentColumn], displayColumnId: 'c1' });

    beforeEach(() => {
      getListRows.mockResolvedValue([parent, child]);
    });

    it('shows the parent by name in the table, never by id', async () => {
      renderTree();

      expect(await screen.findByText('Quality North')).toBeInTheDocument();
      // Two cells read "Quality": the parent's own name and the child's Parent cell.
      await waitFor(() => expect(screen.getAllByText('Quality')).toHaveLength(2));
      expect(screen.queryByText('r2')).not.toBeInTheDocument();
    });

    it('offers the other rows to pick from, and not the row being edited', async () => {
      const user = userEvent.setup();
      renderTree();

      const editButtons = await screen.findAllByRole('button', { name: 'Edit row' });
      await user.click(editButtons[0]);
      await user.click(screen.getByLabelText('Parent'));

      // The row being edited is "Quality" itself, so only the other one is offerable.
      expect(await screen.findByRole('option', { name: 'Quality North' })).toBeInTheDocument();
      expect(screen.queryByRole('option', { name: 'Quality' })).not.toBeInTheDocument();
      expect(screen.getByRole('option', { name: 'None' })).toBeInTheDocument();
    });

    it('saves the picked row id', async () => {
      const user = userEvent.setup();
      renderTree();

      const editButtons = await screen.findAllByRole('button', { name: 'Edit row' });
      await user.click(editButtons[0]);
      await user.click(screen.getByLabelText('Parent'));
      await user.click(await screen.findByRole('option', { name: 'Quality North' }));
      await user.click(screen.getByRole('button', { name: 'Save' }));

      await waitFor(() => expect(updateListRow).toHaveBeenCalled());
      expect(updateListRow).toHaveBeenCalledWith('i1', 'r2', {
        values: { note: 'Quality', parent: 'r3' },
      });
    });

    it('clears the reference through the None entry', async () => {
      const user = userEvent.setup();
      renderTree();

      const editButtons = await screen.findAllByRole('button', { name: 'Edit row' });
      await user.click(editButtons[1]);
      await user.click(screen.getByLabelText('Parent'));
      await user.click(await screen.findByRole('option', { name: 'None' }));
      await user.click(screen.getByRole('button', { name: 'Save' }));

      await waitFor(() => expect(updateListRow).toHaveBeenCalled());
      expect(updateListRow).toHaveBeenCalledWith('i1', 'r3', {
        values: { note: 'Quality North', parent: null },
      });
    });
  });
});
