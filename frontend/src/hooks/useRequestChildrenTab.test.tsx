import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { toast } from 'sonner';
import { useRequestChildrenTab } from './useRequestChildrenTab';
import { createChildRequest, moveRequest } from '@foundation/src/lib/api/request-api';
import { REQUEST_DERIVED_QUERY_KEYS } from '@foundation/src/lib/core/invalidate-request-data';
import { createTestQueryClient } from '@foundation/src/test-utils';
import type { Request } from '@foundation/src/types/requests';

vi.mock('@foundation/src/lib/api/request-api', () => ({
  createChildRequest: vi.fn(),
  moveRequest: vi.fn(),
}));

const GROUP = { id: 'grp-1', name: 'Group', parentRequestId: null, sortOrder: 0 } as unknown as Request;
const CHILD = { id: 'c-1', name: 'Child', parentRequestId: 'grp-1', sortOrder: 0 } as unknown as Request;
const LOOSE = { id: 'loose-1', name: 'Loose task', parentRequestId: null, sortOrder: 3 } as unknown as Request;
const TREE = [GROUP, CHILD, LOOSE];
const BY_ID = new Map(TREE.map((r) => [r.id, r]));

function renderTab(request: Request | null) {
  const setError = vi.fn();
  const { spy, wrapper } = createTestQueryClient({ feedback: true });
  const hook = renderHook(
    () => useRequestChildrenTab({ open: true, request, allRequests: TREE, requestsById: BY_ID, setError }),
    { wrapper },
  );
  return { ...hook, setError, invalidateSpy: spy };
}

function expectRequestDataInvalidated(spy: ReturnType<typeof renderTab>['invalidateSpy']) {
  for (const queryKey of REQUEST_DERIVED_QUERY_KEYS) {
    expect(spy).toHaveBeenCalledWith({ queryKey, exact: false });
  }
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(createChildRequest).mockResolvedValue({} as never);
  vi.mocked(moveRequest).mockResolvedValue({} as never);
});

describe('useRequestChildrenTab — edit mode', () => {
  it('adds a child at the end of the group and re-reads the request data', async () => {
    const { result, invalidateSpy } = renderTab(GROUP);
    act(() => result.current.setNewChildName('  Fresh  '));

    await act(() => result.current.handleAddChild());

    expect(createChildRequest).toHaveBeenCalledWith('grp-1', 'Fresh', 1);
    expect(result.current.newChildName).toBe('');
    expectRequestDataInvalidated(invalidateSpy);
    expect(toast.success).not.toHaveBeenCalled();
  });

  it('shows a failed add inline and never toasts', async () => {
    vi.mocked(createChildRequest).mockRejectedValueOnce(new Error('Name taken'));
    const { result, setError } = renderTab(GROUP);
    act(() => result.current.setNewChildName('Fresh'));

    await act(() => result.current.handleAddChild());

    expect(setError).toHaveBeenLastCalledWith('Name taken');
    expect(result.current.newChildName).toBe('Fresh');
    expect(toast.error).not.toHaveBeenCalled();
  });

  it('pulls the selected existing requests in with consecutive sort orders', async () => {
    const { result } = renderTab(GROUP);
    act(() => result.current.setAddExistingOpen(() => true));
    expect(result.current.addExistingCandidates.map((r) => r.id)).toEqual(['loose-1']);
    act(() => result.current.toggleAddExistingSelected('loose-1'));

    await act(() => result.current.handleAddExisting());

    expect(moveRequest).toHaveBeenCalledWith('loose-1', { newParentRequestId: 'grp-1', sortOrder: 1 });
    expect(result.current.addExistingOpen).toBe(false);
  });

  it('removes a child to the end of the root list, and reports a failure inline', async () => {
    const { result, setError } = renderTab(GROUP);

    await act(() => result.current.handleRemoveChild(CHILD));
    expect(moveRequest).toHaveBeenCalledWith('c-1', { newParentRequestId: null, sortOrder: 4 });

    vi.mocked(moveRequest).mockRejectedValueOnce(new Error('Locked'));
    await act(() => result.current.handleRemoveChild(CHILD));
    expect(setError).toHaveBeenLastCalledWith('Locked');
    expect(toast.error).not.toHaveBeenCalled();
  });
});

describe('useRequestChildrenTab — create mode', () => {
  it('queues instead of writing, and counts the queue as unsaved', async () => {
    const { result } = renderTab(null);
    expect(result.current.hasPending).toBe(false);

    act(() => result.current.setNewChildName('Queued'));
    await act(() => result.current.handleAddChild());

    expect(createChildRequest).not.toHaveBeenCalled();
    expect(result.current.pendingChildren).toEqual(['Queued']);
    expect(result.current.hasPending).toBe(true);

    act(() => result.current.clearPendingChildren());
    expect(result.current.hasPending).toBe(false);
  });

  it('commits the queue under the saved group and toasts each failure by name', async () => {
    const { result, invalidateSpy } = renderTab(null);
    act(() => result.current.setNewChildName('A'));
    await act(() => result.current.handleAddChild());
    act(() => result.current.setNewChildName('B'));
    await act(() => result.current.handleAddChild());
    act(() => result.current.setAddExistingOpen(() => true));
    act(() => result.current.toggleAddExistingSelected('loose-1'));
    await act(() => result.current.handleAddExisting());
    expect(moveRequest).not.toHaveBeenCalled();
    expect(result.current.pendingExistingRequests.map((r) => r.id)).toEqual(['loose-1']);

    vi.mocked(createChildRequest).mockRejectedValueOnce(new Error('Boom'));
    vi.mocked(moveRequest).mockRejectedValueOnce(new Error('Gone'));

    await act(() => result.current.commitPending('new-grp'));

    expect(createChildRequest).toHaveBeenCalledWith('new-grp', 'A', 0);
    expect(createChildRequest).toHaveBeenCalledWith('new-grp', 'B', 1);
    expect(moveRequest).toHaveBeenCalledWith('loose-1', { newParentRequestId: 'new-grp', sortOrder: 2 });
    expect(toast.error).toHaveBeenCalledWith('Failed to create child "A"', { description: 'Boom' });
    expect(toast.error).toHaveBeenCalledWith('Failed to add "Loose task"', { description: 'Gone' });
    // "B" went through, so the request data is re-read.
    expectRequestDataInvalidated(invalidateSpy);
  });
});
