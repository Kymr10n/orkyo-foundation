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
});
