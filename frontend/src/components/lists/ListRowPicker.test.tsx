/** @jsxImportSource react */
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ListRowPicker } from './ListRowPicker';
import type { ListColumn, ListDefinition, ListRow } from '@foundation/src/lib/api/lists-api';

const getListRows = vi.fn();
const getListDefinition = vi.fn();

vi.mock('@foundation/src/lib/api/lists-api', async (importOriginal) => {
  const actual = await importOriginal<Record<string, unknown>>();
  return {
    ...actual,
    getListRows: (...args: unknown[]) => getListRows(...args),
    getListDefinition: (...args: unknown[]) => getListDefinition(...args),
  };
});

function column(overrides: Partial<ListColumn> = {}): ListColumn {
  return {
    id: 'c1',
    listDefinitionId: 'd1',
    key: 'name',
    label: 'Name',
    dataType: 'text',
    isRequired: false,
    sortOrder: 0,
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

const definition: ListDefinition = {
  id: 'd1',
  name: 'Components',
  isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  columns: [column(), column({ id: 'c2', key: 'price', label: 'Price', dataType: 'number' })],
};

const rows: ListRow[] = [
  {
    id: 'row-bolt',
    listInstanceId: 'i1',
    values: { name: 'Bolt', price: 2 },
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  },
  {
    id: 'row-nut',
    listInstanceId: 'i1',
    values: { name: 'Nut', price: 1 },
    createdAt: '2026-01-02T00:00:00Z',
    updatedAt: '2026-01-02T00:00:00Z',
  },
];

function renderPicker(value: string[], onChange = vi.fn()) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  render(
    <QueryClientProvider client={client}>
      <ListRowPicker instanceId="i1" definitionId="d1" value={value} onChange={onChange} />
    </QueryClientProvider>,
  );
  return onChange;
}

describe('ListRowPicker', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getListRows.mockResolvedValue(rows);
    getListDefinition.mockResolvedValue(definition);
  });

  it('describes a row by its first column, with the rest as context', async () => {
    renderPicker([]);

    // The same formatter the table uses, so a row reads identically wherever it appears.
    expect(await screen.findByText('Bolt — 2')).toBeInTheDocument();
  });

  it('reports the ids of what is picked', async () => {
    const onChange = renderPicker([]);

    await userEvent.click(await screen.findByLabelText('Bolt — 2'));

    expect(onChange).toHaveBeenCalledWith(['row-bolt']);
  });

  it('unpicks a row that was already selected', async () => {
    const onChange = renderPicker(['row-bolt']);

    await userEvent.click(await screen.findByLabelText('Bolt — 2'));

    expect(onChange).toHaveBeenCalledWith([]);
  });

  it('stores picks in the list order, not the order they were clicked', async () => {
    // Two resources picking the same rows then store the same array, so the value reads the same
    // way whichever order the user got there.
    const onChange = renderPicker(['row-nut']);

    await userEvent.click(await screen.findByLabelText('Bolt — 2'));

    expect(onChange).toHaveBeenCalledWith(['row-bolt', 'row-nut']);
  });

  it('says where the rows come from when the shared list is empty', async () => {
    getListRows.mockResolvedValue([]);
    renderPicker([]);

    expect(await screen.findByText(/administrator adds them under Resources/i)).toBeInTheDocument();
  });
});
