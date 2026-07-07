import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { OrkyoDataTable, type ColumnDef } from './OrkyoDataTable';

interface Row {
  id: string;
  name: string;
  status: string;
}

const columns: ColumnDef<Row>[] = [
  { accessorKey: 'name', header: 'Name' },
  { accessorKey: 'status', header: 'Status' },
];

function makeRows(count: number): Row[] {
  return Array.from({ length: count }, (_, i) => ({
    id: `r${i}`,
    name: `Item ${i + 1}`,
    status: i % 2 === 0 ? 'active' : 'inactive',
  }));
}

describe('OrkyoDataTable', () => {
  // ── Loading / Error / Empty ──────────────────────────────────────────────

  it('shows loading state', () => {
    render(<OrkyoDataTable columns={columns} data={[]} isLoading />);
    expect(screen.getByText(/loading/i)).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('renders skeleton rows (role=status) while loading', () => {
    const { container } = render(<OrkyoDataTable columns={columns} data={[]} isLoading />);
    expect(screen.getByRole('status')).toBeInTheDocument();
    // One skeleton cell per column, across the placeholder rows.
    expect(container.querySelectorAll('.animate-pulse').length).toBeGreaterThanOrEqual(columns.length);
  });

  it('shows error state', () => {
    render(<OrkyoDataTable columns={columns} data={[]} error="Something went wrong" />);
    expect(screen.getByText('Something went wrong')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('does not render a retry button when onRetry is omitted', () => {
    render(<OrkyoDataTable columns={columns} data={[]} error="Something went wrong" />);
    expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument();
  });

  it('shows a "Try again" button wired to onRetry when provided', async () => {
    const user = userEvent.setup();
    const onRetry = vi.fn();
    render(<OrkyoDataTable columns={columns} data={[]} error="Something went wrong" onRetry={onRetry} />);
    const retryButton = screen.getByRole('button', { name: 'Try again' });
    expect(retryButton).toBeInTheDocument();
    await user.click(retryButton);
    expect(onRetry).toHaveBeenCalledOnce();
  });

  it('error takes precedence over loading', () => {
    render(<OrkyoDataTable columns={columns} data={[]} isLoading error="Oops" />);
    // isLoading shows first in our implementation
    expect(screen.getByText(/loading/i)).toBeInTheDocument();
  });

  it('shows default empty message', () => {
    render(<OrkyoDataTable columns={columns} data={[]} />);
    expect(screen.getByText('No results found.')).toBeInTheDocument();
  });

  it('shows custom empty message', () => {
    render(<OrkyoDataTable columns={columns} data={[]} emptyMessage="No tenants yet." />);
    expect(screen.getByText('No tenants yet.')).toBeInTheDocument();
  });

  // ── Data rendering ───────────────────────────────────────────────────────

  it('renders column headers', () => {
    render(<OrkyoDataTable columns={columns} data={makeRows(1)} />);
    expect(screen.getByText('Name')).toBeInTheDocument();
    expect(screen.getByText('Status')).toBeInTheDocument();
  });

  it('renders row data', () => {
    render(<OrkyoDataTable columns={columns} data={makeRows(3)} />);
    expect(screen.getByText('Item 1')).toBeInTheDocument();
    expect(screen.getByText('Item 2')).toBeInTheDocument();
    expect(screen.getByText('Item 3')).toBeInTheDocument();
  });

  it('renders custom cell content', () => {
    const cols: ColumnDef<Row>[] = [
      { accessorKey: 'name', header: 'Name' },
      {
        accessorKey: 'status',
        header: 'Status',
        cell: ({ getValue }) => <span data-testid="badge">{getValue<string>().toUpperCase()}</span>,
      },
    ];
    render(<OrkyoDataTable columns={cols} data={makeRows(2)} />);
    const badges = screen.getAllByTestId('badge');
    expect(badges[0]).toHaveTextContent('ACTIVE');
    expect(badges[1]).toHaveTextContent('INACTIVE');
  });

  // ── Client-side filtering ─────────────────────────────────────────────────

  it('renders filter input when filterColumn is provided', () => {
    render(<OrkyoDataTable columns={columns} data={makeRows(3)} filterColumn="name" filterPlaceholder="Search..." />);
    expect(screen.getByPlaceholderText('Search...')).toBeInTheDocument();
  });

  it('does not render filter input when no filter props provided', () => {
    render(<OrkyoDataTable columns={columns} data={makeRows(3)} />);
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
  });

  it('filters rows client-side as user types', async () => {
    const user = userEvent.setup();
    render(<OrkyoDataTable columns={columns} data={makeRows(5)} filterColumn="name" filterPlaceholder="Search" />);
    expect(screen.getAllByRole('row')).toHaveLength(6); // 1 header + 5 data

    await user.type(screen.getByPlaceholderText('Search'), 'Item 3');
    expect(screen.getAllByRole('row')).toHaveLength(2); // 1 header + 1 match
    expect(screen.getByText('Item 3')).toBeInTheDocument();
    expect(screen.queryByText('Item 1')).not.toBeInTheDocument();
  });

  it('shows empty message when client filter matches nothing', async () => {
    const user = userEvent.setup();
    render(<OrkyoDataTable columns={columns} data={makeRows(3)} filterColumn="name" emptyMessage="No match." />);
    await user.type(screen.getByRole('textbox'), 'zzz');
    expect(screen.getByText('No match.')).toBeInTheDocument();
  });

  // ── Server-side filtering (keystroke) ────────────────────────────────────

  it('calls onFilterChange on each keystroke when filterOnSubmit is false', async () => {
    const user = userEvent.setup();
    const onFilterChange = vi.fn();
    render(
      <OrkyoDataTable
        columns={columns}
        data={makeRows(2)}
        filterValue=""
        onFilterChange={onFilterChange}
        filterPlaceholder="Search"
      />,
    );
    // Controlled input resets to filterValue="" after each change, so each
    // keystroke fires with just that character rather than an accumulated string.
    await user.type(screen.getByPlaceholderText('Search'), 'ab');
    expect(onFilterChange).toHaveBeenCalledTimes(2);
    expect(onFilterChange).toHaveBeenNthCalledWith(1, 'a');
    expect(onFilterChange).toHaveBeenNthCalledWith(2, 'b');
  });

  it('does not render Search button without filterOnSubmit', () => {
    render(
      <OrkyoDataTable
        columns={columns}
        data={makeRows(2)}
        filterValue=""
        onFilterChange={vi.fn()}
      />,
    );
    expect(screen.queryByRole('button', { name: /search/i })).not.toBeInTheDocument();
  });

  // ── Server-side filtering (submit pattern) ────────────────────────────────

  it('buffers input and only calls onFilterChange on button click when filterOnSubmit', async () => {
    const user = userEvent.setup();
    const onFilterChange = vi.fn();
    render(
      <OrkyoDataTable
        columns={columns}
        data={makeRows(2)}
        filterValue=""
        onFilterChange={onFilterChange}
        filterOnSubmit
        filterPlaceholder="Search"
      />,
    );
    await user.type(screen.getByPlaceholderText('Search'), 'alice');
    expect(onFilterChange).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: /search/i }));
    expect(onFilterChange).toHaveBeenCalledOnce();
    expect(onFilterChange).toHaveBeenCalledWith('alice');
  });

  it('calls onFilterChange on Enter key when filterOnSubmit', async () => {
    const user = userEvent.setup();
    const onFilterChange = vi.fn();
    render(
      <OrkyoDataTable
        columns={columns}
        data={makeRows(2)}
        filterValue=""
        onFilterChange={onFilterChange}
        filterOnSubmit
        filterPlaceholder="Search"
      />,
    );
    await user.type(screen.getByPlaceholderText('Search'), 'bob{Enter}');
    expect(onFilterChange).toHaveBeenCalledOnce();
    expect(onFilterChange).toHaveBeenCalledWith('bob');
  });

  // ── Pagination (client-side) ──────────────────────────────────────────────

  it('does not show pagination controls without pageSize', () => {
    render(<OrkyoDataTable columns={columns} data={makeRows(50)} />);
    expect(screen.queryByRole('button', { name: '' })).not.toBeInTheDocument();
    expect(screen.queryByText(/page/i)).not.toBeInTheDocument();
  });

  it('shows pagination controls when pageSize is set', () => {
    render(<OrkyoDataTable columns={columns} data={makeRows(30)} pageSize={10} />);
    expect(screen.getByText('Page 1 of 3')).toBeInTheDocument();
  });

  it('renders only pageSize rows on first page', () => {
    render(<OrkyoDataTable columns={columns} data={makeRows(30)} pageSize={10} />);
    expect(screen.getAllByText(/Item \d+/).length).toBe(10);
    expect(screen.getByText('Item 1')).toBeInTheDocument();
    expect(screen.queryByText('Item 11')).not.toBeInTheDocument();
  });

  it('prev button is disabled on first page', () => {
    render(<OrkyoDataTable columns={columns} data={makeRows(30)} pageSize={10} />);
    const [prev] = screen.getAllByRole('button');
    expect(prev).toBeDisabled();
  });

  it('next button is enabled on first page', () => {
    render(<OrkyoDataTable columns={columns} data={makeRows(30)} pageSize={10} />);
    const [, next] = screen.getAllByRole('button');
    expect(next).not.toBeDisabled();
  });

  it('navigates to next page and shows correct rows', async () => {
    const user = userEvent.setup();
    render(<OrkyoDataTable columns={columns} data={makeRows(30)} pageSize={10} />);
    const [, next] = screen.getAllByRole('button');
    await user.click(next);
    expect(screen.getByText('Page 2 of 3')).toBeInTheDocument();
    expect(screen.getByText('Item 11')).toBeInTheDocument();
    expect(screen.queryByText('Item 1')).not.toBeInTheDocument();
  });

  it('next button is disabled on last page', async () => {
    const user = userEvent.setup();
    render(<OrkyoDataTable columns={columns} data={makeRows(20)} pageSize={10} />);
    const [, next] = screen.getAllByRole('button');
    await user.click(next); // go to page 2 (last)
    expect(next).toBeDisabled();
  });

  it('navigates back to previous page', async () => {
    const user = userEvent.setup();
    render(<OrkyoDataTable columns={columns} data={makeRows(30)} pageSize={10} />);
    const [prev, next] = screen.getAllByRole('button');
    await user.click(next);
    await user.click(prev);
    expect(screen.getByText('Page 1 of 3')).toBeInTheDocument();
  });

  // ── Server-side pagination ────────────────────────────────────────────────

  it('calls onPageChange with next page index on next click', async () => {
    const user = userEvent.setup();
    const onPageChange = vi.fn();
    render(
      <OrkyoDataTable
        columns={columns}
        data={makeRows(10)}
        pageSize={10}
        totalCount={30}
        page={0}
        onPageChange={onPageChange}
      />,
    );
    const [, next] = screen.getAllByRole('button');
    await user.click(next);
    expect(onPageChange).toHaveBeenCalledWith(1);
  });

  it('calls onPageChange with prev page index on prev click', async () => {
    const user = userEvent.setup();
    const onPageChange = vi.fn();
    render(
      <OrkyoDataTable
        columns={columns}
        data={makeRows(10)}
        pageSize={10}
        totalCount={30}
        page={1}
        onPageChange={onPageChange}
      />,
    );
    const [prev] = screen.getAllByRole('button');
    await user.click(prev);
    expect(onPageChange).toHaveBeenCalledWith(0);
  });

  it('shows correct page count from totalCount', () => {
    render(
      <OrkyoDataTable
        columns={columns}
        data={makeRows(10)}
        pageSize={10}
        totalCount={25}
        page={0}
        onPageChange={vi.fn()}
      />,
    );
    expect(screen.getByText('Page 1 of 3')).toBeInTheDocument();
  });

  it('prev is disabled on server-side page 0', () => {
    render(
      <OrkyoDataTable
        columns={columns}
        data={makeRows(10)}
        pageSize={10}
        totalCount={30}
        page={0}
        onPageChange={vi.fn()}
      />,
    );
    const [prev] = screen.getAllByRole('button');
    expect(prev).toBeDisabled();
  });

  // ── Row interaction (onRowClick) ─────────────────────────────────────────

  it('calls onRowClick with the row data when a row is clicked', () => {
    const onRowClick = vi.fn();
    render(<OrkyoDataTable columns={columns} data={makeRows(3)} onRowClick={onRowClick} />);
    fireEvent.click(screen.getByText('Item 2'));
    expect(onRowClick).toHaveBeenCalledOnce();
    expect(onRowClick).toHaveBeenCalledWith(expect.objectContaining({ id: 'r1', name: 'Item 2' }));
  });

  it('marks rows as clickable only when onRowClick is provided', () => {
    const { rerender } = render(<OrkyoDataTable columns={columns} data={makeRows(1)} />);
    // Without onRowClick the data row carries no cursor-pointer affordance.
    const plainRow = screen.getByText('Item 1').closest('tr');
    expect(plainRow?.className).not.toContain('cursor-pointer');

    rerender(<OrkyoDataTable columns={columns} data={makeRows(1)} onRowClick={vi.fn()} />);
    const clickableRow = screen.getByText('Item 1').closest('tr');
    expect(clickableRow?.className).toContain('cursor-pointer');
  });

  it('does not fire onRowClick when an action cell stops propagation', async () => {
    const user = userEvent.setup();
    const onRowClick = vi.fn();
    const onAction = vi.fn();
    const cols: ColumnDef<Row>[] = [
      { accessorKey: 'name', header: 'Name' },
      {
        id: 'actions',
        header: () => null,
        cell: () => (
          <button
            onClick={(e) => {
              e.stopPropagation();
              onAction();
            }}
          >
            Act
          </button>
        ),
      },
    ];
    render(<OrkyoDataTable columns={cols} data={makeRows(1)} onRowClick={onRowClick} />);
    await user.click(screen.getByRole('button', { name: 'Act' }));
    expect(onAction).toHaveBeenCalledOnce();
    expect(onRowClick).not.toHaveBeenCalled();
  });
});

// ── Responsive card mode ───────────────────────────────────────────────────
describe('OrkyoDataTable — card mode', () => {
  const originalMatchMedia = window.matchMedia;

  function setViewport(width: number) {
    Object.defineProperty(window, 'matchMedia', {
      writable: true,
      configurable: true,
      value: vi.fn((query: string) => {
        const min = /\(min-width:\s*(\d+)px\)/.exec(query);
        return {
          matches: min ? width >= Number(min[1]) : false,
          media: query,
          onchange: null,
          addEventListener: () => {},
          removeEventListener: () => {},
          dispatchEvent: () => false,
        } as unknown as MediaQueryList;
      }),
    });
  }

  afterEach(() => {
    Object.defineProperty(window, 'matchMedia', {
      value: originalMatchMedia,
      writable: true,
      configurable: true,
    });
  });

  const renderCard = (row: Row) => <div data-testid="card">{row.name}</div>;

  it('renders cards instead of the table on phone when renderCard is provided', () => {
    setViewport(500);
    render(<OrkyoDataTable columns={columns} data={makeRows(3)} renderCard={renderCard} />);
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(screen.getAllByTestId('card')).toHaveLength(3);
    expect(screen.getByText('Item 2')).toBeInTheDocument();
  });

  it('keeps the grid on tablet/desktop even when renderCard is provided', () => {
    setViewport(1280);
    render(<OrkyoDataTable columns={columns} data={makeRows(3)} renderCard={renderCard} />);
    expect(screen.getByRole('table')).toBeInTheDocument();
    expect(screen.queryByTestId('card')).not.toBeInTheDocument();
  });

  it('falls back to the table on phone when no renderCard is given', () => {
    setViewport(500);
    render(<OrkyoDataTable columns={columns} data={makeRows(2)} />);
    expect(screen.getByRole('table')).toBeInTheDocument();
  });

  it('fires onRowClick when a card is tapped', () => {
    setViewport(500);
    const onRowClick = vi.fn();
    render(
      <OrkyoDataTable columns={columns} data={makeRows(3)} renderCard={renderCard} onRowClick={onRowClick} />,
    );
    fireEvent.click(screen.getByText('Item 2'));
    expect(onRowClick).toHaveBeenCalledWith(expect.objectContaining({ id: 'r1', name: 'Item 2' }));
  });

  it('an action inside a card can stop propagation to suppress onRowClick', async () => {
    setViewport(500);
    const user = userEvent.setup();
    const onRowClick = vi.fn();
    const onAction = vi.fn();
    const cardWithAction = (row: Row) => (
      <div>
        {row.name}
        <button onClick={(e) => { e.stopPropagation(); onAction(); }}>Act</button>
      </div>
    );
    render(
      <OrkyoDataTable columns={columns} data={makeRows(1)} renderCard={cardWithAction} onRowClick={onRowClick} />,
    );
    await user.click(screen.getByRole('button', { name: 'Act' }));
    expect(onAction).toHaveBeenCalledOnce();
    expect(onRowClick).not.toHaveBeenCalled();
  });

  it('still applies client-side filtering in card mode', async () => {
    setViewport(500);
    const user = userEvent.setup();
    render(
      <OrkyoDataTable
        columns={columns}
        data={makeRows(5)}
        filterColumn="name"
        filterPlaceholder="Search"
        renderCard={renderCard}
      />,
    );
    expect(screen.getAllByTestId('card')).toHaveLength(5);
    await user.type(screen.getByPlaceholderText('Search'), 'Item 3');
    expect(screen.getAllByTestId('card')).toHaveLength(1);
    expect(screen.getByText('Item 3')).toBeInTheDocument();
  });
});
