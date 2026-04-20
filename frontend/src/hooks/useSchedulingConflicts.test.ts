import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook } from '@testing-library/react';
import { useSchedulingConflicts } from './useSchedulingConflicts';
import type { Request } from '@/types/requests';
import type { Space } from '@/types/space';

const { mockSetConflicts } = vi.hoisted(() => ({
  mockSetConflicts: vi.fn(),
}));

vi.mock('@/store/app-store', () => {
  const mockStore = {
    setConflicts: mockSetConflicts,
    conflicts: new Map(),
  };
  return {
    useAppStore: Object.assign(
      (selector: (s: typeof mockStore) => unknown) => selector(mockStore),
      { getState: () => mockStore },
    ),
  };
});

vi.mock('@/domain/scheduling/schedule-preview', () => ({
  buildPreviewSchedule: vi.fn(() => []),
}));

vi.mock('@/domain/scheduling/schedule-validator', () => ({
  evaluateSchedule: vi.fn(() => new Map()),
}));

vi.mock('@/domain/scheduling/capability-matcher', () => ({
  validateSpaceRequirements: vi.fn(() => []),
}));

vi.mock('@/lib/api/space-capability-api', () => ({
  getSpaceCapabilities: vi.fn(() => Promise.resolve([])),
}));

describe('useSchedulingConflicts', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('returns empty conflicts for empty inputs', () => {
    const { result } = renderHook(() =>
      useSchedulingConflicts([], [], null),
    );
    expect(result.current.conflictingRequestIds.size).toBe(0);
    expect(result.current.schedulingValidation.size).toBe(0);
  });

  it('returns spaceCapacities map from spaces', () => {
    const spaces = [
      { id: 's1', capacity: 10 } as Space,
      { id: 's2', capacity: 5 } as Space,
    ];
    const { result } = renderHook(() =>
      useSchedulingConflicts([], spaces, 'site-1'),
    );
    expect(result.current.spaceCapacities.get('s1')).toBe(10);
    expect(result.current.spaceCapacities.get('s2')).toBe(5);
  });

  it('defaults capacity to 1 when space has no capacity', () => {
    const spaces = [{ id: 's1' } as Space];
    const { result } = renderHook(() =>
      useSchedulingConflicts([], spaces, 'site-1'),
    );
    expect(result.current.spaceCapacities.get('s1')).toBe(1);
  });

  it('syncs scheduling validation to store', async () => {
    const { evaluateSchedule } = await import('@/domain/scheduling/schedule-validator');
    const mockEval = vi.mocked(evaluateSchedule);
    const conflicts = new Map([['r1', [{ type: 'overlap' }]]]);
    mockEval.mockReturnValue(conflicts as never);

    const requests = [{ id: 'r1' } as Request];
    renderHook(() => useSchedulingConflicts(requests, [], 'site-1'));

    expect(mockSetConflicts).toHaveBeenCalled();
  });
});
