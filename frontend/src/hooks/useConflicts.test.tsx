/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { useConflicts } from './useConflicts';

// ── Module mocks ──────────────────────────────────────────────────────────────

vi.mock('@foundation/src/hooks/useUtilization', () => ({
  useRequests: vi.fn(() => ({ data: [] })),
  useSpaces: vi.fn(() => ({ data: [] })),
}));

vi.mock('@foundation/src/store/app-store', () => ({
  useAppStore: vi.fn((sel: (s: Record<string, unknown>) => unknown) =>
    sel({ selectedSiteId: 'site-1' }),
  ),
}));

vi.mock('@foundation/src/lib/api/space-capability-api', () => ({
  getSpaceCapabilities: vi.fn(() => Promise.resolve([])),
}));

vi.mock('@foundation/src/domain/scheduling/schedule-preview', () => ({
  buildPreviewSchedule: vi.fn(() => []),
}));

vi.mock('@foundation/src/domain/scheduling/schedule-validator', () => ({
  evaluateSchedule: vi.fn(() => new Map()),
}));

vi.mock('@foundation/src/domain/scheduling/capability-matcher', () => ({
  validateSpaceRequirements: vi.fn(() => []),
}));

vi.mock('@foundation/src/domain/scheduling/request-assignments', () => ({
  getSpaceResourceId: vi.fn(() => null),
}));

import { useRequests, useSpaces } from '@foundation/src/hooks/useUtilization';
import { getSpaceCapabilities } from '@foundation/src/lib/api/space-capability-api';
import { evaluateSchedule } from '@foundation/src/domain/scheduling/schedule-validator';
import { validateSpaceRequirements } from '@foundation/src/domain/scheduling/capability-matcher';
import { getSpaceResourceId } from '@foundation/src/domain/scheduling/request-assignments';

function makeWrapper() {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  );
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe('useConflicts', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(useRequests).mockReturnValue({ data: [] } as any);
    vi.mocked(useSpaces).mockReturnValue({ data: [] } as any);
    vi.mocked(evaluateSchedule).mockReturnValue(new Map());
    vi.mocked(getSpaceResourceId).mockReturnValue(null);
  });

  it('returns empty conflicts when there are no requests or spaces', () => {
    const { result } = renderHook(() => useConflicts(), { wrapper: makeWrapper() });
    expect(result.current.conflicts.size).toBe(0);
    expect(result.current.conflictingRequestIds.size).toBe(0);
    expect(result.current.schedulingValidation.size).toBe(0);
  });

  it('populates spaceCapacities from spaces data', () => {
    vi.mocked(useSpaces).mockReturnValue({
      data: [{ id: 's1', capacity: 10 } as any],
    } as any);
    const { result } = renderHook(() => useConflicts(), { wrapper: makeWrapper() });
    expect(result.current.spaceCapacities.get('s1')).toBe(10);
  });

  it('defaults capacity to 1 when space has no capacity', () => {
    vi.mocked(useSpaces).mockReturnValue({
      data: [{ id: 's1', capacity: undefined } as any],
    } as any);
    const { result } = renderHook(() => useConflicts(), { wrapper: makeWrapper() });
    expect(result.current.spaceCapacities.get('s1')).toBe(1);
  });

  it('merges scheduling conflicts from evaluateSchedule', () => {
    const mockConflicts = new Map([['req-1', [{ type: 'overlap', message: 'Overlaps' }]]]);
    vi.mocked(evaluateSchedule).mockReturnValue(mockConflicts as any);
    const { result } = renderHook(() => useConflicts(), { wrapper: makeWrapper() });
    expect(result.current.conflicts.has('req-1')).toBe(true);
    expect(result.current.conflictingRequestIds.has('req-1')).toBe(true);
  });

  it('does not fetch capabilities when no scheduled requests with requirements', () => {
    vi.mocked(useRequests).mockReturnValue({
      data: [{ id: 'r1', requirements: [] } as any],
    } as any);
    // getSpaceResourceId returns null → no space assigned → not in scheduledResourceIds
    renderHook(() => useConflicts(), { wrapper: makeWrapper() });
    expect(getSpaceCapabilities).not.toHaveBeenCalled();
  });

  it('fetches capabilities for requests that have a space and requirements', async () => {
    vi.mocked(useRequests).mockReturnValue({
      data: [{ id: 'r1', requirements: [{ criterionId: 'c1' }] } as any],
    } as any);
    vi.mocked(getSpaceResourceId).mockReturnValue('space-1');
    vi.mocked(getSpaceCapabilities).mockResolvedValue([]);

    renderHook(() => useConflicts(), { wrapper: makeWrapper() });
    await waitFor(() => expect(getSpaceCapabilities).toHaveBeenCalledWith('site-1', 'space-1'));
  });

  it('merges capability conflicts when capabilitiesMap is available', async () => {
    const capConflict = [{ type: 'capability', message: 'Missing capability' }];
    vi.mocked(validateSpaceRequirements).mockReturnValue(capConflict as any);
    vi.mocked(useRequests).mockReturnValue({
      data: [{ id: 'r1', requirements: [{ criterionId: 'c1' }] } as any],
    } as any);
    vi.mocked(getSpaceResourceId).mockReturnValue('space-1');
    vi.mocked(getSpaceCapabilities).mockResolvedValue([{ criterionId: 'c1', value: false }] as any);

    const { result } = renderHook(() => useConflicts(), { wrapper: makeWrapper() });
    await waitFor(() => {
      expect(result.current.conflicts.has('r1')).toBe(true);
    });
  });

  it('handles capability fetch failure gracefully (Promise.allSettled)', async () => {
    vi.mocked(useRequests).mockReturnValue({
      data: [{ id: 'r1', requirements: [{ criterionId: 'c1' }] } as any],
    } as any);
    vi.mocked(getSpaceResourceId).mockReturnValue('space-1');
    vi.mocked(getSpaceCapabilities).mockRejectedValue(new Error('Network error'));

    const { result } = renderHook(() => useConflicts(), { wrapper: makeWrapper() });
    // Should not throw — allSettled handles the failure
    await waitFor(() => expect(result.current).toBeDefined());
    // Failed fetch → capabilitiesMap doesn't include space-1 → no capability conflicts
    expect(result.current.conflicts.size).toBe(0);
  });
});
