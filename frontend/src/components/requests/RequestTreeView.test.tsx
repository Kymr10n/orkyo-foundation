import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { TooltipProvider } from '@foundation/src/components/ui/tooltip';
import { RequestTreeView } from './RequestTreeView';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';
import type { Request } from '@foundation/src/types/requests';
import type { FlatTreeEntry } from '@foundation/src/domain/request-tree';

// Mock request-tree-store (used by TreeRow for expandedIds)
const mockExpandedIds = new Set<string>();
const mockExpandAll = vi.fn();
const mockCollapseAll = vi.fn();
vi.mock('@foundation/src/store/request-tree-store', () => ({
  useRequestTreeStore: vi.fn((selector: (s: {
    expandedIds: Set<string>;
    expandAll: (ids: string[]) => void;
    collapseAll: () => void;
  }) => unknown) =>
    selector({
      expandedIds: mockExpandedIds,
      expandAll: mockExpandAll,
      collapseAll: mockCollapseAll,
    }),
  ),
}));

// Mock virtualizer so all items render in jsdom (no DOM measurements)
vi.mock('@tanstack/react-virtual', () => ({
  useVirtualizer: vi.fn(({ count }: { count: number }) => ({
    getVirtualItems: () =>
      Array.from({ length: count }, (_, i) => ({
        index: i,
        start: i * 40,
        size: 40,
        key: i,
      })),
    getTotalSize: () => count * 40,
  })),
}));

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

function makeRequest(overrides: Partial<Request> = {}): Request {
  return {
    id: 'r-1',
    name: 'Test Request',
    description: null,
    parentRequestId: null,
    planningMode: 'leaf',
    sortOrder: 0,
    assignments: [],
    startTs: null,
    endTs: null,
    earliestStartTs: null,
    latestEndTs: null,
    minimalDurationValue: 60,
    minimalDurationUnit: 'minutes',
    actualDurationValue: null,
    actualDurationUnit: null,
    durationMin: undefined,
    schedulingSettingsApply: true,
    status: 'new',
    requirements: [],
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

const parentRequest = makeRequest({
  id: 'parent-1',
  name: 'Parent Group',
  planningMode: 'summary',
});

const childRequest = makeRequest({
  id: 'child-1',
  name: 'Child Task',
  planningMode: 'leaf',
  parentRequestId: 'parent-1',
});

const childRequest2 = makeRequest({
  id: 'child-2',
  name: 'Child Task 2',
  planningMode: 'leaf',
  parentRequestId: 'parent-1',
});

const allRequests: Request[] = [parentRequest, childRequest, childRequest2];

function makeEntry(request: Request, depth: number, hasChildren: boolean, isLastChild = false): FlatTreeEntry {
  return { request, depth, hasChildren, isLastChild };
}

const defaultEntries: FlatTreeEntry[] = [
  makeEntry(parentRequest, 0, true),
  makeEntry(childRequest, 1, false),
  makeEntry(childRequest2, 1, false),
];

const defaultHandlers = {
  onToggle: vi.fn(),
  onSelect: vi.fn(),
  onEdit: vi.fn(),
  onDelete: vi.fn(),
  onDrop: vi.fn(),
};

function renderTreeView(
  props: Partial<React.ComponentProps<typeof RequestTreeView>> = {},
) {
  return render(
    <TooltipProvider>
      <RequestTreeView
        entries={defaultEntries}
        allRequests={allRequests}
        selectedId={null}
        {...defaultHandlers}
        {...props}
      />
    </TooltipProvider>,
  );
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('RequestTreeView', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockExpandedIds.clear();
    mockExpandedIds.add('parent-1');
    mockExpandAll.mockClear();
    mockCollapseAll.mockClear();
    // useCanEdit is globally mocked to true (src/test/setup.ts); reset each test.
    vi.mocked(useCanEdit).mockReturnValue(true);
  });

  it('renders Edit/Delete row actions per row for editors', () => {
    renderTreeView();
    // One Edit (and one Delete) button per visible row.
    expect(screen.getAllByRole('button', { name: /^Edit /i })).toHaveLength(defaultEntries.length);
    expect(screen.getAllByRole('button', { name: /^Delete /i })).toHaveLength(defaultEntries.length);
  });

  it('hides the row actions (all mutating) for a viewer', () => {
    vi.mocked(useCanEdit).mockReturnValue(false);
    renderTreeView();
    expect(screen.queryAllByRole('button', { name: /^Edit /i })).toHaveLength(0);
    expect(screen.queryAllByRole('button', { name: /^Delete /i })).toHaveLength(0);
  });

  it('renders all visible entries', () => {
    renderTreeView();
    expect(screen.getByText('Parent Group')).toBeInTheDocument();
    expect(screen.getByText('Child Task')).toBeInTheDocument();
    expect(screen.getByText('Child Task 2')).toBeInTheDocument();
  });

  it('has tree role', () => {
    renderTreeView();
    expect(screen.getByRole('tree')).toBeInTheDocument();
  });

  it('renders treeitem roles', () => {
    renderTreeView();
    const items = screen.getAllByRole('treeitem');
    expect(items).toHaveLength(3);
  });

  it('calls onSelect and onEdit when clicking a row (row click opens the dialog)', () => {
    renderTreeView();
    fireEvent.click(screen.getByText('Child Task'));
    expect(defaultHandlers.onSelect).toHaveBeenCalledWith('child-1');
    expect(defaultHandlers.onEdit).toHaveBeenCalledWith(childRequest);
  });

  it('calls onToggle when clicking expand chevron', () => {
    renderTreeView();
    // The parent row has a chevron button — find via the svg class
    const parentItem = screen.getAllByRole('treeitem')[0];
    const buttons = within(parentItem).getAllByRole('button');
    // The expand/collapse chevron is a <button> with tabIndex -1
    const chevron = buttons.find((b) => b.getAttribute('tabindex') === '-1');
    expect(chevron).toBeDefined();
    fireEvent.click(chevron!);
    expect(defaultHandlers.onToggle).toHaveBeenCalledWith('parent-1');
  });

  it('shows child count badge on parent rows', () => {
    renderTreeView();
    // Parent has 2 children
    expect(screen.getByText('2')).toBeInTheDocument();
  });

  it('applies selected styling', () => {
    renderTreeView({ selectedId: 'child-1' });
    const items = screen.getAllByRole('treeitem');
    const childItem = items[1];
    expect(childItem).toHaveAttribute('aria-selected', 'true');
  });

  it('renders a header row mirroring the house table', () => {
    renderTreeView();
    expect(screen.getByText('Name')).toBeInTheDocument();
    expect(screen.getByText('Schedule')).toBeInTheDocument();
    expect(screen.getByText('Duration')).toBeInTheDocument();
    expect(screen.getByText('Status')).toBeInTheDocument();
  });

  it('renders a status badge (not a dot) per row', () => {
    renderTreeView();
    // RequestStatusBadge renders the humanised label; all fixtures are "new".
    expect(screen.getAllByText('New')).toHaveLength(3);
  });

  it('renders the shared row actions (Edit / Delete, no Move to)', () => {
    renderTreeView();
    expect(screen.getByRole('button', { name: 'Edit Parent Group' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Delete Parent Group' })).toBeInTheDocument();
    expect(screen.queryByText(/Move to/i)).not.toBeInTheDocument();
  });

  it('clicking the inline Edit button on a row calls onEdit', async () => {
    const user = userEvent.setup();
    renderTreeView();
    await user.click(screen.getByRole('button', { name: 'Edit Child Task' }));
    expect(defaultHandlers.onEdit).toHaveBeenCalledWith(childRequest);
  });

  it('clicking the inline Delete button on a row calls onDelete', async () => {
    const user = userEvent.setup();
    renderTreeView();
    await user.click(screen.getByRole('button', { name: 'Delete Child Task' }));
    expect(defaultHandlers.onDelete).toHaveBeenCalledWith(childRequest);
  });

  it('renders without entries', () => {
    renderTreeView({ entries: [], allRequests: [] });
    expect(screen.getByRole('tree')).toBeInTheDocument();
    expect(screen.queryAllByRole('treeitem')).toHaveLength(0);
  });

  it('indents children deeper than parents', () => {
    renderTreeView();
    // Indent lives on the Name cell (the div wrapping the name text), so the
    // Schedule/Duration/Status columns stay aligned with the header.
    const parentPad = screen.getByText('Parent Group').closest('div')!.style.paddingLeft;
    const childPad = screen.getByText('Child Task').closest('div')!.style.paddingLeft;
    // depth 0 => 0*20 = 0px, depth 1 => 1*20 = 20px
    expect(parseInt(parentPad || '0')).toBeLessThan(parseInt(childPad || '0'));
  });

  it('expands all groups with * keyboard shortcut', () => {
    mockExpandedIds.clear();
    renderTreeView();
    fireEvent.keyDown(screen.getByRole('tree'), { key: '*' });
    expect(mockExpandAll).toHaveBeenCalledWith(['parent-1']);
  });

  it('collapses current expanded group with - keyboard shortcut', () => {
    renderTreeView();
    fireEvent.keyDown(screen.getByRole('tree'), { key: '-' });
    expect(defaultHandlers.onToggle).toHaveBeenCalledWith('parent-1');
  });

  it('expands current collapsed group with + keyboard shortcut', () => {
    mockExpandedIds.clear();
    renderTreeView();
    fireEvent.keyDown(screen.getByRole('tree'), { key: '+' });
    expect(defaultHandlers.onToggle).toHaveBeenCalledWith('parent-1');
  });

  it('Delete key calls onDelete when canEdit is true', () => {
    renderTreeView({ selectedId: 'child-1' });
    fireEvent.keyDown(screen.getByRole('tree'), { key: 'Delete' });
    expect(defaultHandlers.onDelete).toHaveBeenCalledWith(childRequest);
  });

  it('Delete key does nothing when canEdit is false (viewer)', () => {
    vi.mocked(useCanEdit).mockReturnValue(false);
    renderTreeView({ selectedId: 'child-1' });
    fireEvent.keyDown(screen.getByRole('tree'), { key: 'Delete' });
    expect(defaultHandlers.onDelete).not.toHaveBeenCalled();
  });

  it('aria-activedescendant points at the treeitem element itself', () => {
    renderTreeView({ selectedId: 'child-1' });
    const tree = screen.getByRole('tree');
    const activeId = tree.getAttribute('aria-activedescendant');
    expect(activeId).toBe('tree-item-child-1');
    const target = document.getElementById(activeId!);
    expect(target).toHaveAttribute('role', 'treeitem');
  });

  it('renders the curated icon on a row whose request.icon is set', () => {
    const withIcon = makeRequest({ id: 'icon-row', name: 'Iconned', icon: 'hammer' });
    const { container } = renderTreeView({
      entries: [makeEntry(withIcon, 0, false)],
      allRequests: [withIcon],
    });
    expect(container.querySelector('.lucide-hammer')).not.toBeNull();
  });
});

describe('RequestTreeView — touch affordances', () => {
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

  beforeEach(() => {
    mockExpandedIds.clear();
    mockExpandedIds.add('parent-1');
    vi.mocked(useCanEdit).mockReturnValue(true);
  });

  afterEach(() => {
    Object.defineProperty(window, 'matchMedia', {
      value: originalMatchMedia,
      writable: true,
      configurable: true,
    });
  });

  it('keeps row actions hover-gated on desktop', () => {
    setViewport(1280);
    renderTreeView();
    // The shared Edit/Delete actions are wrapped in a hover-reveal span on desktop.
    const wrapper = screen.getAllByRole('button', { name: /^Edit /i })[0].closest('span');
    expect(wrapper?.className).toContain('opacity-0');
  });

  it('shows row actions persistently and hides the drag handle on phone', () => {
    setViewport(500);
    const { container } = renderTreeView();

    const wrapper = screen.getAllByRole('button', { name: /^Edit /i })[0].closest('span');
    expect(wrapper).toBeTruthy();
    expect(wrapper?.className).not.toContain('opacity-0');

    // Drag-to-reparent is desktop-only; the handle is removed on touch.
    const handle = container.querySelector('.lucide-grip-vertical')?.closest('span');
    expect(handle?.className).toContain('hidden');
  });
});
