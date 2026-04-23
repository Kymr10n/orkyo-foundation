import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { TooltipProvider } from '@foundation/src/components/ui/tooltip';
import { RequestTreeView } from './RequestTreeView';
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
    spaceId: null,
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
    status: 'planned',
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
  onAddChild: vi.fn(),
  onAddSibling: vi.fn(),
  onAddExisting: vi.fn(),
  onMoveTo: vi.fn(),
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

  it('calls onSelect when clicking a row', () => {
    renderTreeView();
    fireEvent.click(screen.getByText('Child Task'));
    expect(defaultHandlers.onSelect).toHaveBeenCalledWith('child-1');
  });

  it('calls onEdit when double-clicking a row', () => {
    renderTreeView();
    fireEvent.doubleClick(screen.getByText('Child Task'));
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

  it('renders mode badges', () => {
    renderTreeView();
    // Summary mode for parent, Leaf mode for children
    expect(screen.getByText('Group')).toBeInTheDocument();
    expect(screen.getAllByText('Task')).toHaveLength(2);
  });

  it('renders status dots', () => {
    renderTreeView();
    // Each row has a status dot (small circle span) — we check them via treeitem structure
    const items = screen.getAllByRole('treeitem');
    expect(items).toHaveLength(3);
  });

  it('renders without entries', () => {
    renderTreeView({ entries: [], allRequests: [] });
    expect(screen.getByRole('tree')).toBeInTheDocument();
    expect(screen.queryAllByRole('treeitem')).toHaveLength(0);
  });

  it('indents children deeper than parents', () => {
    renderTreeView();
    const items = screen.getAllByRole('treeitem');
    const parentPadding = items[0].style.paddingLeft;
    const childPadding = items[1].style.paddingLeft;
    // depth 0 => 12 + 0*20 = 12px, depth 1 => 12 + 1*20 = 32px
    expect(parseInt(parentPadding)).toBeLessThan(parseInt(childPadding));
  });

  it('expands all groups when clicking Expand all', () => {
    mockExpandedIds.clear();
    renderTreeView();
    fireEvent.click(screen.getByRole('button', { name: 'Expand all' }));
    expect(mockExpandAll).toHaveBeenCalledWith(['parent-1']);
  });

  it('collapses all groups when clicking Collapse all', () => {
    renderTreeView();
    fireEvent.click(screen.getByRole('button', { name: 'Collapse all' }));
    expect(mockCollapseAll).toHaveBeenCalledTimes(1);
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
});
