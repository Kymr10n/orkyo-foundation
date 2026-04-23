/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, act, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { BrowserRouter } from 'react-router-dom';
import { TooltipProvider } from '@foundation/src/components/ui/tooltip';
import { RequestsPage } from '@foundation/src/pages/RequestsPage';

// Mock the useExportHandler and useImportHandler hooks
vi.mock('@foundation/src/hooks/useImportExport', () => ({
  useExportHandler: vi.fn(() => vi.fn()),
  useImportHandler: vi.fn(() => vi.fn()),
}));

// Mock the store
vi.mock('@foundation/src/store/app-store', () => ({
  useAppStore: vi.fn((selector) => {
    const mockState = {
      selectedSiteId: 'site-1',
      conflicts: new Map(),
    };
    return selector ? selector(mockState) : mockState;
  }),
}));

const mockToggle = vi.fn();
const mockExpandAncestors = vi.fn();
const mockExpandAll = vi.fn();
const mockSetSelectedId = vi.fn();
let mockSelectedId: string | null = null;

vi.mock('@foundation/src/store/request-tree-store', () => ({
  useRequestTreeStore: vi.fn(() => ({
    expandedIds: new Set<string>(),
    toggle: mockToggle,
    expandAncestors: mockExpandAncestors,
    expandAll: mockExpandAll,
    selectedId: mockSelectedId,
    setSelectedId: mockSetSelectedId,
  })),
}));

const mockGetRequests = vi.fn((_?: any): Promise<any[]> => Promise.resolve([]));

// Mock request-api — RequestsPage calls getRequests() directly in a useEffect
vi.mock('@foundation/src/lib/api/request-api', () => ({
  getRequests: (siteId: unknown) => mockGetRequests(siteId),
  createRequest: vi.fn(),
  updateRequest: vi.fn(),
  deleteRequest: vi.fn(),
  deleteRequestSubtree: vi.fn(),
  moveRequest: vi.fn(),
}));

// Mock the useRequests hook
vi.mock('@foundation/src/hooks/useUtilization', () => ({
  useRequests: vi.fn(() => ({
    data: [],
    isLoading: false,
    error: null,
  })),
  useCreateRequest: vi.fn(() => ({
    mutate: vi.fn(),
  })),
  useUpdateRequest: vi.fn(() => ({
    mutate: vi.fn(),
  })),
  useDeleteRequest: vi.fn(() => ({
    mutate: vi.fn(),
  })),
}));

// Mock the spaces hook
vi.mock('@foundation/src/hooks/useSpaces', () => ({
  useSpaces: vi.fn(() => ({
    data: [],
    isLoading: false,
  })),
}));

// Mock child components to isolate page logic
vi.mock('@foundation/src/components/requests/RequestTreeView', () => ({
  RequestTreeView: ({ entries, onSelect, onEdit, onDelete, onAddChild, onAddSibling, onAddExisting, onMoveTo, onDrop }: any) => (
    <div data-testid="tree-view">
      {(entries as { request: { id: string; name: string; parentRequestId?: string | null } }[]).map((e) => (
        <div key={e.request.id} data-testid={`tree-item-${e.request.id}`}>
          <span>{e.request.name}</span>
          <button onClick={() => (onSelect as (id: string) => void)(e.request.id)}>Select</button>
          <button onClick={() => (onEdit as (r: unknown) => void)(e.request)}>Edit</button>
          <button onClick={() => (onDelete as (r: unknown) => void)(e.request)}>Delete</button>
          <button data-testid={`add-child-${e.request.id}`} onClick={() => (onAddChild as (r: unknown) => void)(e.request)}>Add Child</button>
          <button data-testid={`add-sibling-${e.request.id}`} onClick={() => (onAddSibling as (r: unknown) => void)(e.request)}>Add Sibling</button>
          <button data-testid={`add-existing-${e.request.id}`} onClick={() => (onAddExisting as (r: unknown) => void)(e.request)}>Add Existing</button>
          <button data-testid={`move-to-${e.request.id}`} onClick={() => (onMoveTo as (r: unknown) => void)(e.request)}>Move To</button>
          {onDrop && <button data-testid={`drop-${e.request.id}`} onClick={() => (onDrop as (a: string, b: string) => void)('r-drag', e.request.id)}>Drop</button>}
        </div>
      ))}
    </div>
  ),
}));

vi.mock('@foundation/src/components/requests/RequestListView', () => ({
  RequestListView: ({ requests, onSelect, onEdit, onDelete }: any) => (
    <div data-testid="list-view">
      {(requests as { id: string; name: string }[]).map((r) => (
        <div key={r.id} data-testid={`list-item-${r.id}`}>
          <span>{r.name}</span>
          <button data-testid={`list-select-${r.id}`} onClick={() => (onSelect as (id: string) => void)(r.id)}>Select</button>
          <button data-testid={`list-edit-${r.id}`} onClick={() => (onEdit as (r: unknown) => void)(r)}>Edit</button>
          <button data-testid={`list-delete-${r.id}`} onClick={() => (onDelete as (r: unknown) => void)(r)}>Delete</button>
        </div>
      ))}
    </div>
  ),
}));

vi.mock('@foundation/src/components/requests/RequestDetailPanel', () => ({
  RequestDetailPanel: ({ request, onEdit, onClose, onNavigate }: any) => (
    <div data-testid="detail-panel">
      <span>{(request as { name: string }).name}</span>
      <button data-testid="detail-edit" onClick={() => (onEdit as (r: unknown) => void)(request)}>Edit</button>
      <button data-testid="detail-close" onClick={onClose as () => void}>Close</button>
      {onNavigate && <button data-testid="detail-navigate" onClick={() => (onNavigate as (id: string) => void)((request as { id: string }).id)}>Navigate</button>}
    </div>
  ),
}));

vi.mock('@foundation/src/components/requests/RequestFormDialog', () => ({
  RequestFormDialog: ({ open, onSave, onOpenChange }: any) =>
    open ? (
      <div data-testid="form-dialog">
        Form Dialog
        <button data-testid="form-save" onClick={() => onSave({ name: 'Test', planningMode: 'leaf' })}>Save</button>
        <button data-testid="form-close" onClick={() => onOpenChange(false)}>Close</button>
      </div>
    ) : null,
}));

vi.mock('@foundation/src/components/requests/AddExistingRequestsDialog', () => ({
  AddExistingRequestsDialog: ({ onConfirm, onOpenChange }: any) => (
    <div data-testid="add-existing-dialog">
      <button data-testid="confirm-add-existing" onClick={() => onConfirm(['r-orphan'])}>Confirm</button>
      <button data-testid="cancel-add-existing" onClick={() => onOpenChange(false)}>Cancel</button>
    </div>
  ),
}));

vi.mock('@foundation/src/components/requests/MoveToDialog', () => ({
  MoveToDialog: ({ onConfirm, onOpenChange }: any) => (
    <div data-testid="move-to-dialog">
      <button data-testid="confirm-move" onClick={() => onConfirm('r-target')}>Confirm</button>
      <button data-testid="cancel-move" onClick={() => onOpenChange(false)}>Cancel</button>
    </div>
  ),
}));

vi.mock('@foundation/src/lib/utils/export-handlers', () => ({
  exportRequests: vi.fn(),
  importRequests: vi.fn(() => Promise.resolve([])),
}));

vi.mock('@foundation/src/domain/request-tree', () => ({
  buildRequestTree: vi.fn((requests: any[]) => {
    return requests
      .filter((r: any) => !r.parentRequestId)
      .map((r: any) => ({
        request: r,
        depth: 0,
        expanded: false,
        children: requests
          .filter((c: any) => c.parentRequestId === r.id)
          .map((c: any) => ({ request: c, depth: 1, expanded: false, children: [] })),
      }));
  }),
  flattenTree: vi.fn((entries: any[]) => {
    const result: any[] = [];
    const flatten = (list: any[]) => {
      for (const e of list) {
        result.push(e);
        if (e.children) flatten(e.children);
      }
    };
    flatten(entries);
    return result;
  }),
  flattenVisibleTree: vi.fn((entries: any[], _expandedIds: any) => {
    const result: any[] = [];
    const flatten = (list: any[]) => {
      for (const e of list) {
        result.push(e);
        if (e.children) flatten(e.children);
      }
    };
    flatten(entries);
    return result;
  }),
  getDescendantIds: vi.fn(() => []),
  getAncestorIds: vi.fn(() => []),
  getNextSortOrder: vi.fn(() => 0),
  wouldCreateCycle: vi.fn(() => false),
  canHaveChildren: vi.fn(() => true),
}));

vi.mock('@foundation/src/lib/utils/utils', async (importOriginal) => {
  const actual = await importOriginal<Record<string, unknown>>();
  return {
    ...actual,
    buildUpdatePayload: vi.fn((d: any) => d),
    buildCreatePayload: vi.fn((d: any) => d),
  };
});

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>
      <TooltipProvider>{children}</TooltipProvider>
    </QueryClientProvider>
  );
};

describe('RequestsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGetRequests.mockResolvedValue([]);
    mockSelectedId = null;
  });

  it('renders heading and search', async () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    expect(screen.getByText('Requests')).toBeInTheDocument();
    expect(screen.getByPlaceholderText('Search requests...')).toBeInTheDocument();
    await act(async () => {});
  });

  it('renders New Task and New Group buttons', async () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    expect(screen.getByText('New Task')).toBeInTheDocument();
    expect(screen.getByText('New Group')).toBeInTheDocument();
    await act(async () => {});
  });

  it('shows empty state when no requests exist', async () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => {
      expect(screen.getByText('No requests found')).toBeInTheDocument();
    });
    expect(screen.getByText(/get started by creating/i)).toBeInTheDocument();
  });

  it('shows error state when loading fails', async () => {
    mockGetRequests.mockRejectedValue(new Error('Network failure'));
    const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => {
      expect(screen.getByText('Error loading requests')).toBeInTheDocument();
    });
    expect(screen.getByText('Network failure')).toBeInTheDocument();
    expect(screen.getByText('Try Again')).toBeInTheDocument();
    consoleSpy.mockRestore();
  });

  it('retries loading on Try Again click', async () => {
    mockGetRequests
      .mockRejectedValueOnce(new Error('fail'))
      .mockResolvedValueOnce([]);
    const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => {
      expect(screen.getByText('Try Again')).toBeInTheDocument();
    });
    fireEvent.click(screen.getByText('Try Again'));
    await waitFor(() => {
      expect(mockGetRequests).toHaveBeenCalledTimes(2);
    });
    consoleSpy.mockRestore();
  });

  it('renders tree view with loaded requests', async () => {
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Task A', planningMode: 'leaf', parentRequestId: null, sortOrder: 0 },
      { id: 'r2', name: 'Task B', planningMode: 'leaf', parentRequestId: null, sortOrder: 1 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => {
      expect(screen.getByTestId('tree-view')).toBeInTheDocument();
    });
    expect(screen.getByText('Task A')).toBeInTheDocument();
    expect(screen.getByText('Task B')).toBeInTheDocument();
  });

  it('opens form dialog when New Task is clicked', async () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await act(async () => {});
    fireEvent.click(screen.getByText('New Task'));
    await waitFor(() => {
      expect(screen.getByTestId('form-dialog')).toBeInTheDocument();
    });
  });

  it('opens form dialog when New Group is clicked', async () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await act(async () => {});
    fireEvent.click(screen.getByText('New Group'));
    await waitFor(() => {
      expect(screen.getByTestId('form-dialog')).toBeInTheDocument();
    });
  });

  it('shows Create Request button in empty state', async () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => {
      expect(screen.getByText('Create Request')).toBeInTheDocument();
    });
  });

  it('filters search results', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Alpha', planningMode: 'leaf', parentRequestId: null, sortOrder: 0 },
      { id: 'r2', name: 'Beta', planningMode: 'leaf', parentRequestId: null, sortOrder: 1 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => {
      expect(screen.getByText('Alpha')).toBeInTheDocument();
    });
    fireEvent.change(screen.getByPlaceholderText('Search requests...'), { target: { value: 'Alpha' } });
    await act(async () => { vi.advanceTimersByTime(300); });
    expect(screen.getByText('Alpha')).toBeInTheDocument();
    expect(screen.queryByText('Beta')).not.toBeInTheDocument();
    vi.useRealTimers();
  });

  it('shows adjust search message when search yields no results', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Alpha', planningMode: 'leaf', parentRequestId: null, sortOrder: 0 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => {
      expect(screen.getByText('Alpha')).toBeInTheDocument();
    });
    fireEvent.change(screen.getByPlaceholderText('Search requests...'), { target: { value: 'zzz' } });
    await act(async () => { vi.advanceTimersByTime(300); });
    expect(screen.getByText(/try adjusting your search/i)).toBeInTheDocument();
    vi.useRealTimers();
  });

  it('renders Filters button', async () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    expect(screen.getByText('Filters')).toBeInTheDocument();
    await act(async () => {});
  });

  // --- View mode toggle ---

  it('toggles to list view and back', async () => {
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Task A', planningMode: 'leaf', parentRequestId: null, sortOrder: 0 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => {
      expect(screen.getByTestId('tree-view')).toBeInTheDocument();
    });
    // Click list view toggle — icon-only buttons, find via the toggle group container
    const toggleGroup = document.querySelector('.flex.border.rounded-md');
    const toggleButtons = toggleGroup?.querySelectorAll('button');
    // Second button is list view
    fireEvent.click(toggleButtons![1]);
    await waitFor(() => {
      expect(screen.getByTestId('list-view')).toBeInTheDocument();
    });
    // Switch back — first button is tree view
    fireEvent.click(toggleButtons![0]);
    await waitFor(() => {
      expect(screen.getByTestId('tree-view')).toBeInTheDocument();
    });
  });

  // --- Select request shows detail panel ---

  it('shows detail panel on request select', async () => {
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Task A', planningMode: 'leaf', parentRequestId: null, sortOrder: 0 },
    ]);
    mockSelectedId = 'r1';
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => {
      expect(screen.getByTestId('detail-panel')).toBeInTheDocument();
    });
    // The detail panel should contain the request name
    expect(screen.getByTestId('detail-panel')).toHaveTextContent('Task A');
    mockSelectedId = null;
  });

  // --- Edit from tree view opens form dialog ---

  it('opens edit dialog from tree view', async () => {
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Task A', planningMode: 'leaf', parentRequestId: null, sortOrder: 0 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => {
      expect(screen.getByTestId('tree-view')).toBeInTheDocument();
    });
    const editBtns = screen.getAllByText('Edit');
    fireEvent.click(editBtns[0]);
    await waitFor(() => {
      expect(screen.getByTestId('form-dialog')).toBeInTheDocument();
    });
  });

  // --- Delete from tree view opens delete confirmation ---

  it('opens delete confirmation from tree view', async () => {
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Task A', planningMode: 'leaf', parentRequestId: null, sortOrder: 0 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => {
      expect(screen.getByTestId('tree-view')).toBeInTheDocument();
    });
    const deleteBtns = screen.getAllByText('Delete');
    fireEvent.click(deleteBtns[0]);
    await waitFor(() => {
      expect(screen.getByText('Delete request')).toBeInTheDocument();
    });
  });

  // --- Confirm delete calls deleteRequest ---

  it('confirms delete and reloads requests', async () => {
    const { deleteRequest } = await import('@foundation/src/lib/api/request-api');
    vi.mocked(deleteRequest).mockResolvedValue(undefined as any);
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Task A', planningMode: 'leaf', parentRequestId: null, sortOrder: 0 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => {
      expect(screen.getByTestId('tree-view')).toBeInTheDocument();
    });
    // Open delete dialog
    const deleteBtns = screen.getAllByText('Delete');
    fireEvent.click(deleteBtns[0]);
    await waitFor(() => {
      expect(screen.getByText('Delete request')).toBeInTheDocument();
    });
    // After clicking Delete in the dialog, resolve with empty list
    mockGetRequests.mockResolvedValue([]);
    const confirmBtn = screen.getByRole('button', { name: /^Delete$/i });
    fireEvent.click(confirmBtn);
    await waitFor(() => {
      expect(deleteRequest).toHaveBeenCalledWith('r1');
    });
  });

  // --- Add child from tree view opens create dialog ---

  it('opens create dialog via Add Child', async () => {
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Task A', planningMode: 'leaf', parentRequestId: null, sortOrder: 0 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => {
      expect(screen.getByTestId('tree-view')).toBeInTheDocument();
    });
    fireEvent.click(screen.getByTestId('add-child-r1'));
    await waitFor(() => {
      expect(screen.getByTestId('form-dialog')).toBeInTheDocument();
    });
  });

  // --- Add sibling opens create dialog ---

  it('opens create dialog via Add Sibling', async () => {
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Task A', planningMode: 'leaf', parentRequestId: null, sortOrder: 0 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => {
      expect(screen.getByTestId('tree-view')).toBeInTheDocument();
    });
    fireEvent.click(screen.getByTestId('add-sibling-r1'));
    await waitFor(() => {
      expect(screen.getByTestId('form-dialog')).toBeInTheDocument();
    });
  });

  // --- Add existing opens dialog ---

  it('opens add-existing dialog', async () => {
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Task A', planningMode: 'leaf', parentRequestId: null, sortOrder: 0 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => {
      expect(screen.getByTestId('tree-view')).toBeInTheDocument();
    });
    fireEvent.click(screen.getByTestId('add-existing-r1'));
    await waitFor(() => {
      expect(screen.getByTestId('add-existing-dialog')).toBeInTheDocument();
    });
  });

  // --- Move to opens dialog ---

  it('opens move-to dialog', async () => {
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Task A', planningMode: 'leaf', parentRequestId: null, sortOrder: 0 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => {
      expect(screen.getByTestId('tree-view')).toBeInTheDocument();
    });
    fireEvent.click(screen.getByTestId('move-to-r1'));
    await waitFor(() => {
      expect(screen.getByTestId('move-to-dialog')).toBeInTheDocument();
    });
  });

  // --- Save new request via form dialog ---

  it('saves a new request from form dialog', async () => {
    const { createRequest } = await import('@foundation/src/lib/api/request-api');
    vi.mocked(createRequest).mockResolvedValue(undefined as any);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await act(async () => {});
    fireEvent.click(screen.getByText('New Task'));
    await waitFor(() => {
      expect(screen.getByTestId('form-dialog')).toBeInTheDocument();
    });
    fireEvent.click(screen.getByTestId('form-save'));
    await waitFor(() => {
      expect(createRequest).toHaveBeenCalled();
    });
  });

  // --- Save edited request ---

  it('saves an edited request from form dialog', async () => {
    const { updateRequest } = await import('@foundation/src/lib/api/request-api');
    vi.mocked(updateRequest).mockResolvedValue(undefined as any);
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Task A', planningMode: 'leaf', parentRequestId: null, sortOrder: 0 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => {
      expect(screen.getByTestId('tree-view')).toBeInTheDocument();
    });
    const editBtns = screen.getAllByText('Edit');
    fireEvent.click(editBtns[0]);
    await waitFor(() => {
      expect(screen.getByTestId('form-dialog')).toBeInTheDocument();
    });
    fireEvent.click(screen.getByTestId('form-save'));
    await waitFor(() => {
      expect(updateRequest).toHaveBeenCalled();
    });
  });

  // --- Close detail panel ---

  it('closes detail panel', async () => {
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Task A', planningMode: 'leaf', parentRequestId: null, sortOrder: 0 },
    ]);
    mockSelectedId = 'r1';
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => {
      expect(screen.getByTestId('detail-panel')).toBeInTheDocument();
    });
    fireEvent.click(screen.getByTestId('detail-close'));
    expect(mockSetSelectedId).toHaveBeenCalledWith(null);
    mockSelectedId = null;
  });

  // --- List view rendering ---

  it('renders requests in list view', async () => {
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Task A', planningMode: 'leaf', parentRequestId: null, sortOrder: 0 },
      { id: 'r2', name: 'Task B', planningMode: 'leaf', parentRequestId: null, sortOrder: 1 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => {
      expect(screen.getByTestId('tree-view')).toBeInTheDocument();
    });
    const toggleGroup = document.querySelector('.flex.border.rounded-md');
    const toggleButtons = toggleGroup?.querySelectorAll('button');
    fireEvent.click(toggleButtons![1]);
    await waitFor(() => {
      expect(screen.getByTestId('list-view')).toBeInTheDocument();
    });
    expect(screen.getByText('Task A')).toBeInTheDocument();
    expect(screen.getByText('Task B')).toBeInTheDocument();
  });

  // --- Drop handler ---

  it('handles drag-and-drop reparent', async () => {
    const { moveRequest } = await import('@foundation/src/lib/api/request-api');
    vi.mocked(moveRequest).mockResolvedValue(undefined as any);
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Task A', planningMode: 'leaf', parentRequestId: null, sortOrder: 0 },
      { id: 'r-drag', name: 'Dragged', planningMode: 'leaf', parentRequestId: null, sortOrder: 1 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => {
      expect(screen.getByTestId('tree-view')).toBeInTheDocument();
    });
    fireEvent.click(screen.getByTestId('drop-r1'));
    await waitFor(() => {
      expect(moveRequest).toHaveBeenCalledWith('r-drag', expect.objectContaining({ newParentRequestId: 'r1' }));
    });
  });
});
