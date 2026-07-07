/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, act, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { BrowserRouter, MemoryRouter } from 'react-router-dom';
import { TooltipProvider } from '@foundation/src/components/ui/tooltip';
import { RequestsPage } from '@foundation/src/pages/RequestsPage';
import { getDescendantIds, getAncestorIds } from '@foundation/src/domain/request-tree';
import { importRequests, exportRequests } from '@foundation/src/lib/utils/export-handlers';
import { toast } from 'sonner';

// Capture the export/import callbacks the page registers so the tests can drive
// them directly (the real hooks wire them to a global toolbar event).
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

const mockNavigate = vi.fn();
vi.mock('react-router-dom', async (importOriginal) => {
  const actual = await importOriginal<Record<string, unknown>>();
  return { ...actual, useNavigate: () => mockNavigate };
});

// Mock the tenant-wide conflicts registry — the page's conflict source.
const mockConflictRegistry = vi.fn(() => ({ conflictsByRequest: new Map<string, unknown[]>() }));
vi.mock('@foundation/src/hooks/useConflictRegistry', () => ({
  useConflictRegistry: () => mockConflictRegistry(),
}));

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

// Mock the store
vi.mock('@foundation/src/store/app-store', () => ({
  useAppStore: vi.fn((selector) => {
    const mockState = {
      selectedSiteId: 'site-1',
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
  useSpaces: vi.fn(() => ({
    data: [],
    isLoading: false,
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
  RequestTreeView: ({ entries, onSelect, onEdit, onDelete, onAddChild, onAddSibling, onAddExisting, onMoveTo, onDrop, onOpenConflicts }: any) => (
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
          {onOpenConflicts && <button data-testid={`open-conflicts-${e.request.id}`} onClick={() => (onOpenConflicts as (id: string) => void)(e.request.id)}>Conflicts</button>}
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
        <button data-testid="form-save" onClick={() => { void Promise.resolve(onSave({ name: 'Test', planningMode: 'leaf' })).catch(() => {}); }}>Save</button>
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
      <button data-testid="confirm-move-root" onClick={() => onConfirm(null)}>Confirm Root</button>
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
  buildChildrenIdMap: vi.fn(() => new Map()),
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
      <BrowserRouter>
        <TooltipProvider>{children}</TooltipProvider>
      </BrowserRouter>
    </QueryClientProvider>
  );
};

describe('RequestsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGetRequests.mockResolvedValue([]);
    mockSelectedId = null;
    mockConflictRegistry.mockReturnValue({ conflictsByRequest: new Map() });
    vi.mocked(getDescendantIds).mockReturnValue([]);
    vi.mocked(getAncestorIds).mockReturnValue([]);
    vi.mocked(importRequests).mockResolvedValue([]);
    global.alert = vi.fn();
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
    expect(screen.getByText('Try again')).toBeInTheDocument();
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
      expect(screen.getByText('Try again')).toBeInTheDocument();
    });
    fireEvent.click(screen.getByText('Try again'));
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

  // ── handleConfirmMoveTo ────────────────────────────────────────────────────

  it('confirms move-to and calls moveRequest', async () => {
    const { moveRequest } = await import('@foundation/src/lib/api/request-api');
    vi.mocked(moveRequest).mockResolvedValue(undefined as any);
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Task A', planningMode: 'leaf', parentRequestId: null, sortOrder: 0 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => expect(screen.getByTestId('tree-view')).toBeInTheDocument());

    // Open move-to dialog then confirm
    fireEvent.click(screen.getByTestId('move-to-r1'));
    await waitFor(() => expect(screen.getByTestId('move-to-dialog')).toBeInTheDocument());
    fireEvent.click(screen.getByTestId('confirm-move'));

    await waitFor(() =>
      expect(moveRequest).toHaveBeenCalledWith('r1', expect.objectContaining({ newParentRequestId: 'r-target' })),
    );
  });

  it('shows error when handleConfirmMoveTo fails', async () => {
    const { moveRequest } = await import('@foundation/src/lib/api/request-api');
    vi.mocked(moveRequest).mockRejectedValueOnce(new Error('Move error'));
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Task A', planningMode: 'leaf', parentRequestId: null, sortOrder: 0 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => expect(screen.getByTestId('tree-view')).toBeInTheDocument());

    fireEvent.click(screen.getByTestId('move-to-r1'));
    await waitFor(() => expect(screen.getByTestId('move-to-dialog')).toBeInTheDocument());
    fireEvent.click(screen.getByTestId('confirm-move'));

    await waitFor(() => expect(screen.getByText('Move error')).toBeInTheDocument());
  });

  // ── handleConfirmAddExisting ────────────────────────────────────────────────

  it('confirms add-existing and calls moveRequest for each selected id', async () => {
    const { moveRequest } = await import('@foundation/src/lib/api/request-api');
    vi.mocked(moveRequest).mockResolvedValue(undefined as any);
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Group', planningMode: 'summary', parentRequestId: null, sortOrder: 0 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => expect(screen.getByTestId('tree-view')).toBeInTheDocument());

    fireEvent.click(screen.getByTestId('add-existing-r1'));
    await waitFor(() => expect(screen.getByTestId('add-existing-dialog')).toBeInTheDocument());
    fireEvent.click(screen.getByTestId('confirm-add-existing'));

    await waitFor(() =>
      expect(moveRequest).toHaveBeenCalledWith('r-orphan', expect.objectContaining({ newParentRequestId: 'r1' })),
    );
  });

  it('shows an error when add-existing move fails', async () => {
    const { moveRequest } = await import('@foundation/src/lib/api/request-api');
    vi.mocked(moveRequest).mockRejectedValueOnce(new Error('Add existing failed'));
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Group', planningMode: 'summary', parentRequestId: null, sortOrder: 0 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => expect(screen.getByTestId('tree-view')).toBeInTheDocument());
    fireEvent.click(screen.getByTestId('add-existing-r1'));
    await waitFor(() => expect(screen.getByTestId('add-existing-dialog')).toBeInTheDocument());
    fireEvent.click(screen.getByTestId('confirm-add-existing'));
    await waitFor(() => expect(screen.getByText('Add existing failed')).toBeInTheDocument());
  });

  // ── Import / export handlers ───────────────────────────────────────────────

  it('exports the loaded requests in the requested format', async () => {
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Task A', planningMode: 'leaf', parentRequestId: null, sortOrder: 0 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => expect(screen.getByTestId('tree-view')).toBeInTheDocument());
    await ioHandlers.exportCb!('csv');
    expect(exportRequests).toHaveBeenCalledWith(
      expect.arrayContaining([expect.objectContaining({ id: 'r1' })]),
      'csv',
    );
  });

  it('imports valid requests, creates each, and reloads', async () => {
    const { createRequest } = await import('@foundation/src/lib/api/request-api');
    vi.mocked(createRequest).mockResolvedValue(undefined as any);
    vi.mocked(importRequests).mockResolvedValueOnce([{ name: 'Imported' } as any]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await act(async () => {});
    await ioHandlers.importCb!(new File(['x'], 'requests.csv'), 'csv');
    expect(createRequest).toHaveBeenCalledWith(expect.objectContaining({ name: 'Imported' }));
    expect(toast.success).toHaveBeenCalledWith('Successfully imported 1 requests');
  });

  it('toasts when the imported file contains no valid requests', async () => {
    vi.mocked(importRequests).mockResolvedValueOnce([]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await act(async () => {});
    await ioHandlers.importCb!(new File(['x'], 'requests.csv'), 'csv');
    expect(toast.error).toHaveBeenCalledWith(
      'Failed to import requests',
      expect.objectContaining({ description: 'No valid requests found in file' }),
    );
  });

  it('toasts with a fallback message when import throws a non-Error', async () => {
    vi.mocked(importRequests).mockRejectedValueOnce('boom');
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await act(async () => {});
    await ioHandlers.importCb!(new File(['x'], 'requests.csv'), 'csv');
    expect(toast.error).toHaveBeenCalledWith(
      'Failed to import requests',
      expect.objectContaining({ description: undefined }),
    );
  });

  // ── ?edit query param ──────────────────────────────────────────────────────

  it('opens the edit dialog from an ?edit= query param', async () => {
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Task A', planningMode: 'leaf', parentRequestId: null, sortOrder: 0 },
    ]);
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });
    render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={['/requests?edit=r1']}>
          <TooltipProvider><RequestsPage /></TooltipProvider>
        </MemoryRouter>
      </QueryClientProvider>,
    );
    await waitFor(() => expect(screen.getByTestId('form-dialog')).toBeInTheDocument());
  });

  // ── List-view search by description ─────────────────────────────────────────

  it('filters list view by a description match', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Alpha', description: 'red apple', planningMode: 'leaf', parentRequestId: null, sortOrder: 0 },
      { id: 'r2', name: 'Beta', description: 'blue sky', planningMode: 'leaf', parentRequestId: null, sortOrder: 1 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => expect(screen.getByTestId('tree-view')).toBeInTheDocument());
    const toggleGroup = document.querySelector('.flex.border.rounded-md');
    const toggleButtons = toggleGroup?.querySelectorAll('button');
    fireEvent.click(toggleButtons![1]);
    await waitFor(() => expect(screen.getByTestId('list-view')).toBeInTheDocument());
    fireEvent.change(screen.getByPlaceholderText('Search requests...'), { target: { value: 'apple' } });
    await act(async () => { vi.advanceTimersByTime(300); });
    expect(screen.getByText('Alpha')).toBeInTheDocument();
    expect(screen.queryByText('Beta')).not.toBeInTheDocument();
    vi.useRealTimers();
  });

  // ── loadRequests non-Error fallback ─────────────────────────────────────────

  it('shows a fallback error message when loading rejects a non-Error', async () => {
    mockGetRequests.mockRejectedValue('socket hang up');
    const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => expect(screen.getByText('Failed to load requests')).toBeInTheDocument());
    consoleSpy.mockRestore();
  });

  // ── Add sibling resolves parent ─────────────────────────────────────────────

  it('opens the create dialog via Add Sibling for a child (resolving its parent)', async () => {
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Parent', planningMode: 'summary', parentRequestId: null, sortOrder: 0 },
      { id: 'r2', name: 'Child', planningMode: 'leaf', parentRequestId: 'r1', sortOrder: 0 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => expect(screen.getByTestId('tree-view')).toBeInTheDocument());
    fireEvent.click(screen.getByTestId('add-sibling-r2'));
    await waitFor(() => expect(screen.getByTestId('form-dialog')).toBeInTheDocument());
  });

  // ── Move-to to root (null parent) ───────────────────────────────────────────

  it('confirms move-to to root with a null parent and sortOrder 0', async () => {
    const { moveRequest } = await import('@foundation/src/lib/api/request-api');
    vi.mocked(moveRequest).mockResolvedValue(undefined as any);
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Task A', planningMode: 'leaf', parentRequestId: null, sortOrder: 0 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => expect(screen.getByTestId('tree-view')).toBeInTheDocument());
    fireEvent.click(screen.getByTestId('move-to-r1'));
    await waitFor(() => expect(screen.getByTestId('move-to-dialog')).toBeInTheDocument());
    fireEvent.click(screen.getByTestId('confirm-move-root'));
    await waitFor(() =>
      expect(moveRequest).toHaveBeenCalledWith('r1', expect.objectContaining({ newParentRequestId: null, sortOrder: 0 })),
    );
  });

  // ── Drop error path ─────────────────────────────────────────────────────────

  it('shows an error when drag-and-drop reparent fails', async () => {
    const { moveRequest } = await import('@foundation/src/lib/api/request-api');
    vi.mocked(moveRequest).mockRejectedValueOnce(new Error('Drop failed'));
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Task A', planningMode: 'leaf', parentRequestId: null, sortOrder: 0 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => expect(screen.getByTestId('tree-view')).toBeInTheDocument());
    fireEvent.click(screen.getByTestId('drop-r1'));
    await waitFor(() => expect(screen.getByText('Drop failed')).toBeInTheDocument());
  });

  // ── Subtree delete + selection clearing ─────────────────────────────────────

  it('deletes a subtree and clears selection when the selected node is removed', async () => {
    const { deleteRequestSubtree } = await import('@foundation/src/lib/api/request-api');
    vi.mocked(deleteRequestSubtree).mockResolvedValue(undefined as any);
    vi.mocked(getDescendantIds).mockReturnValue(['c1']);
    mockSelectedId = 'r1';
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Group', planningMode: 'summary', parentRequestId: null, sortOrder: 0 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => expect(screen.getByTestId('tree-view')).toBeInTheDocument());
    fireEvent.click(screen.getAllByText('Delete')[0]);
    await waitFor(() => expect(screen.getByText('Delete request')).toBeInTheDocument());
    expect(screen.getByText(/and 1 child request\./)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /^Delete$/i }));
    await waitFor(() => expect(deleteRequestSubtree).toHaveBeenCalledWith('r1'));
    expect(mockSetSelectedId).toHaveBeenCalledWith(null);
    mockSelectedId = null;
  });

  it('shows an error toast when deleting a request fails', async () => {
    const { deleteRequest } = await import('@foundation/src/lib/api/request-api');
    vi.mocked(deleteRequest).mockRejectedValueOnce(new Error('Delete boom'));
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Task A', planningMode: 'leaf', parentRequestId: null, sortOrder: 0 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => expect(screen.getByTestId('tree-view')).toBeInTheDocument());
    fireEvent.click(screen.getAllByText('Delete')[0]);
    await waitFor(() => expect(screen.getByText('Delete request')).toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: /^Delete$/i }));
    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith('Failed to delete request', expect.objectContaining({ description: 'Delete boom' })),
    );
  });

  // ── Save error toasts ───────────────────────────────────────────────────────

  it('shows a create error toast when creating a request fails', async () => {
    const { createRequest } = await import('@foundation/src/lib/api/request-api');
    vi.mocked(createRequest).mockRejectedValueOnce(new Error('Create boom'));
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await act(async () => {});
    fireEvent.click(screen.getByText('New Task'));
    await waitFor(() => expect(screen.getByTestId('form-dialog')).toBeInTheDocument());
    fireEvent.click(screen.getByTestId('form-save'));
    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith('Failed to create request', expect.objectContaining({ description: 'Create boom' })),
    );
  });

  it('shows an update error toast when editing a request fails', async () => {
    const { updateRequest } = await import('@foundation/src/lib/api/request-api');
    vi.mocked(updateRequest).mockRejectedValueOnce(new Error('Update boom'));
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Task A', planningMode: 'leaf', parentRequestId: null, sortOrder: 0 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => expect(screen.getByTestId('tree-view')).toBeInTheDocument());
    fireEvent.click(screen.getAllByText('Edit')[0]);
    await waitFor(() => expect(screen.getByTestId('form-dialog')).toBeInTheDocument());
    fireEvent.click(screen.getByTestId('form-save'));
    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith('Failed to update request', expect.objectContaining({ description: 'Update boom' })),
    );
  });

  // ── Selection toggle ────────────────────────────────────────────────────────

  it('deselects a request when its already-selected row is clicked again', async () => {
    mockSelectedId = 'r1';
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Task A', planningMode: 'leaf', parentRequestId: null, sortOrder: 0 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => expect(screen.getByTestId('tree-view')).toBeInTheDocument());
    fireEvent.click(screen.getAllByText('Select')[0]);
    expect(mockSetSelectedId).toHaveBeenCalledWith(null);
    mockSelectedId = null;
  });

  // ── Navigate to request ─────────────────────────────────────────────────────

  it('navigates to a request, expanding its ancestors', async () => {
    vi.mocked(getAncestorIds).mockReturnValue(['p1']);
    mockSelectedId = 'r1';
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Task A', planningMode: 'leaf', parentRequestId: null, sortOrder: 0 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => expect(screen.getByTestId('detail-panel')).toBeInTheDocument());
    fireEvent.click(screen.getByTestId('detail-navigate'));
    expect(mockExpandAncestors).toHaveBeenCalledWith(['p1']);
    expect(mockSetSelectedId).toHaveBeenCalledWith('r1');
    mockSelectedId = null;
  });

  // ── Open conflicts ──────────────────────────────────────────────────────────

  it('opens the conflicts view for a request that has conflicts', async () => {
    mockConflictRegistry.mockReturnValue({
      conflictsByRequest: new Map([['r1', [{ id: 'cf1' }]]]),
    });
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Task A', planningMode: 'leaf', parentRequestId: null, sortOrder: 0 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => expect(screen.getByTestId('tree-view')).toBeInTheDocument());
    fireEvent.click(screen.getByTestId('open-conflicts-r1'));
    expect(mockNavigate).toHaveBeenCalledWith(expect.stringContaining('requestId=r1'));
    expect(mockNavigate).toHaveBeenCalledWith(expect.stringContaining('conflictId=cf1'));
  });

  it('bubbles descendant conflicts up to the parent row and targets the descendant', async () => {
    vi.mocked(getDescendantIds).mockReturnValue(['c1']);
    mockConflictRegistry.mockReturnValue({
      conflictsByRequest: new Map([['c1', [{ id: 'cf-c1' }]]]),
    });
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Group', planningMode: 'summary', parentRequestId: null, sortOrder: 0 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => expect(screen.getByTestId('tree-view')).toBeInTheDocument());
    fireEvent.click(screen.getByTestId('open-conflicts-r1'));
    expect(mockNavigate).toHaveBeenCalledWith(expect.stringContaining('requestId=c1'));
  });

  // ── Initial auto-expand ─────────────────────────────────────────────────────

  it('auto-expands parent nodes on initial load', async () => {
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Group', planningMode: 'summary', parentRequestId: null, sortOrder: 0 },
      { id: 'r2', name: 'Child', planningMode: 'leaf', parentRequestId: 'r1', sortOrder: 0 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => expect(mockExpandAll).toHaveBeenCalledWith(['r1']));
  });

  // ── Dialog onOpenChange close paths ─────────────────────────────────────────

  it('closes the form dialog via onOpenChange', async () => {
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await act(async () => {});
    fireEvent.click(screen.getByText('New Task'));
    await waitFor(() => expect(screen.getByTestId('form-dialog')).toBeInTheDocument());
    fireEvent.click(screen.getByTestId('form-close'));
    await waitFor(() => expect(screen.queryByTestId('form-dialog')).not.toBeInTheDocument());
  });

  it('closes the add-existing dialog via cancel', async () => {
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Group', planningMode: 'summary', parentRequestId: null, sortOrder: 0 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => expect(screen.getByTestId('tree-view')).toBeInTheDocument());
    fireEvent.click(screen.getByTestId('add-existing-r1'));
    await waitFor(() => expect(screen.getByTestId('add-existing-dialog')).toBeInTheDocument());
    fireEvent.click(screen.getByTestId('cancel-add-existing'));
    await waitFor(() => expect(screen.queryByTestId('add-existing-dialog')).not.toBeInTheDocument());
  });

  it('closes the move-to dialog via cancel', async () => {
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Task A', planningMode: 'leaf', parentRequestId: null, sortOrder: 0 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => expect(screen.getByTestId('tree-view')).toBeInTheDocument());
    fireEvent.click(screen.getByTestId('move-to-r1'));
    await waitFor(() => expect(screen.getByTestId('move-to-dialog')).toBeInTheDocument());
    fireEvent.click(screen.getByTestId('cancel-move'));
    await waitFor(() => expect(screen.queryByTestId('move-to-dialog')).not.toBeInTheDocument());
  });

  it('closes the delete dialog via Cancel', async () => {
    mockGetRequests.mockResolvedValue([
      { id: 'r1', name: 'Task A', planningMode: 'leaf', parentRequestId: null, sortOrder: 0 },
    ]);
    const Wrapper = createWrapper();
    render(<Wrapper><RequestsPage /></Wrapper>);
    await waitFor(() => expect(screen.getByTestId('tree-view')).toBeInTheDocument());
    fireEvent.click(screen.getAllByText('Delete')[0]);
    await waitFor(() => expect(screen.getByText('Delete request')).toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: /^Cancel$/i }));
    await waitFor(() => expect(screen.queryByText('Delete request')).not.toBeInTheDocument());
  });
});
