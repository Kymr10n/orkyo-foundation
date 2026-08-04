import { describe, expect, it } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { OrkyoDataTable, type ColumnDef } from './OrkyoDataTable';

/**
 * Exercises the header menu through OrkyoDataTable rather than in isolation: the header only
 * ever renders from the table's header loop, and the loop's can-sort/has-filter gating is part
 * of the behaviour under test.
 */

interface Row {
  name: string;
  status: string;
  createdAt: string;
  size: number;
  tags: string[];
}

const rows: Row[] = [
  { name: 'Alpha', status: 'active', createdAt: '2026-01-10T08:00:00Z', size: 5, tags: ['a'] },
  { name: 'Beta', status: 'expired', createdAt: '2026-03-05T08:00:00Z', size: 12, tags: ['a', 'b'] },
  { name: 'Gamma', status: 'active', createdAt: '2026-06-20T08:00:00Z', size: 2, tags: ['b'] },
];

const STATUS_LABEL: Record<string, string> = { active: 'Active', expired: 'Expired' };

const columns: ColumnDef<Row>[] = [
  { accessorKey: 'name', header: 'Name', meta: { filter: { type: 'text' } } },
  {
    accessorKey: 'status',
    header: 'Status',
    meta: { filter: { type: 'enum', getLabel: (v) => STATUS_LABEL[v] ?? v } },
  },
  { accessorKey: 'createdAt', header: 'Created', meta: { filter: { type: 'date' } } },
  { accessorKey: 'size', header: 'Size', meta: { filter: { type: 'number' } } },
  {
    accessorKey: 'tags',
    header: 'Tags',
    meta: { filter: { type: 'enum', isArray: true } },
  },
  // Display-only column: no accessor, no meta — must render as a plain, non-interactive header.
  { id: 'actions', header: 'Actions', cell: () => null },
];

const renderTable = () => render(<OrkyoDataTable columns={columns} data={rows} />);

const openHeader = async (label: string) => {
  await userEvent.click(screen.getByRole('button', { name: `${label} — sort and filter` }));
};

const visibleNames = () =>
  screen
    .getAllByRole('row')
    .slice(1) // header row
    .map((r) => within(r).getAllByRole('cell')[0].textContent);

describe('DataTableColumnHeader', () => {
  it('leaves display-only columns as plain headers', () => {
    renderTable();
    expect(screen.getByText('Actions')).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Actions — sort and filter' }),
    ).not.toBeInTheDocument();
  });

  it('sorts, reports aria-sort on the th, and clears on the second click', async () => {
    renderTable();
    await openHeader('Name');
    await userEvent.click(screen.getByRole('button', { name: 'Sort Z → A' }));

    expect(visibleNames()).toEqual(['Gamma', 'Beta', 'Alpha']);
    expect(screen.getByRole('columnheader', { name: /Name/ })).toHaveAttribute(
      'aria-sort',
      'descending',
    );

    // The menu is also the off switch: clicking the active direction clears the sort.
    await openHeader('Name');
    await userEvent.click(screen.getByRole('button', { name: 'Sort Z → A' }));
    expect(visibleNames()).toEqual(['Alpha', 'Beta', 'Gamma']);
  });

  it('uses type-appropriate sort wording for dates and numbers', async () => {
    renderTable();
    await openHeader('Created');
    expect(screen.getByRole('button', { name: 'Oldest first' })).toBeInTheDocument();
    await userEvent.keyboard('{Escape}');

    await openHeader('Size');
    expect(screen.getByRole('button', { name: 'Low → High' })).toBeInTheDocument();
  });

  it('filters text by contains', async () => {
    renderTable();
    await openHeader('Name');
    await userEvent.type(screen.getByLabelText('Filter Name'), 'am');

    expect(visibleNames()).toEqual(['Gamma']);
  });

  it('facets enums with display labels and narrowing counts, and shows the count pill', async () => {
    renderTable();
    await openHeader('Status');

    // Labels come from the same lookup the badge cell uses; counts from the faceted model.
    const facetGroup = screen.getByRole('group', { name: 'Filter Status' });
    const active = within(facetGroup).getByLabelText('Active');
    expect(within(facetGroup).getByText('2')).toBeInTheDocument(); // active count

    await userEvent.click(active);
    expect(visibleNames()).toEqual(['Alpha', 'Gamma']);

    // The trigger shows how many facet values are selected.
    const trigger = screen.getByRole('button', { name: 'Status — sort and filter' });
    expect(within(trigger).getByText('1')).toBeInTheDocument();

    // Clear restores every row.
    await userEvent.click(screen.getByRole('button', { name: 'Clear filter' }));
    expect(visibleNames()).toEqual(['Alpha', 'Beta', 'Gamma']);
  });

  it('facets array columns per element', async () => {
    renderTable();
    await openHeader('Tags');

    // 'a' appears in two rows, 'b' in two — flattened, not offered as "a,b".
    await userEvent.click(screen.getByLabelText('a'));
    expect(visibleNames()).toEqual(['Alpha', 'Beta']);
  });

  it('filters date ranges with an inclusive end day', async () => {
    renderTable();
    await openHeader('Created');
    await userEvent.type(screen.getByLabelText('Filter Created to'), '2026-03-05');

    // Beta's 08:00 on the end day must be inside the range.
    expect(visibleNames()).toEqual(['Alpha', 'Beta']);
  });

  it('filters number ranges with open ends', async () => {
    renderTable();
    await openHeader('Size');
    await userEvent.type(screen.getByLabelText('Filter Size from'), '5');

    expect(visibleNames()).toEqual(['Alpha', 'Beta']);
  });
});
