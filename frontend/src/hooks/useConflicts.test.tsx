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

vi.mock('@foundation/src/domain/scheduling/schedule-preview', () => ({
  buildPreviewSchedule: vi.fn(() => []),
}));

vi.mock('@foundation/src/domain/scheduling/schedule-validator', () => ({
  evaluateSchedule: vi.fn(() => new Map()),
}));

// Capability conflicts now come from the backend validator (single source of
// truth). We mock the batch API but let the pure mapper run for real so the
// test exercises the real ValidationIssue → Conflict translation.
vi.mock('@foundation/src/lib/api/resource-assignments-api', () => ({
  validateAssignmentsBatch: vi.fn(() => Promise.resolve([])),
}));

import { useRequests, useSpaces } from '@foundation/src/hooks/useUtilization';
import { evaluateSchedule } from '@foundation/src/domain/scheduling/schedule-validator';
import { validateAssignmentsBatch } from '@foundation/src/lib/api/resource-assignments-api';

/** A request carrying a committed space assignment (the shape useConflicts reads). */
function scheduledRequest(id: string, resourceId = 'space-1') {
  return {
    id,
    assignments: [{
      id: `assign-${id}`,
      resourceId,
      resourceTypeKey: 'space',
      startUtc: '2026-06-10T08:00:00.000Z',
      endUtc: '2026-06-10T10:00:00.000Z',
      assignmentStatus: 'Planned',
    }],
  } as any;
}

function capabilityBlocker(requestId: string, resourceId = 'space-1') {
  return {
    requestId,
    resourceId,
    result: {
      severity: 'blocker',
      blockers: [{
        code: 'capability.missing',
        message: 'Resource does not satisfy requirement',
        criterionId: 'crit-1',
        details: 'Crane',
      }],
      warnings: [],
    },
  };
}

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
    vi.mocked(validateAssignmentsBatch).mockResolvedValue([]);
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
    const mockConflicts = new Map([['req-1', [{ kind: 'overlap', message: 'Overlaps' }]]]);
    vi.mocked(evaluateSchedule).mockReturnValue(mockConflicts as any);
    const { result } = renderHook(() => useConflicts(), { wrapper: makeWrapper() });
    expect(result.current.conflicts.has('req-1')).toBe(true);
    expect(result.current.conflictingRequestIds.has('req-1')).toBe(true);
  });

  it('does not call the backend validator when no request has a space assignment', () => {
    vi.mocked(useRequests).mockReturnValue({
      data: [{ id: 'r1', assignments: [] } as any],
    } as any);
    renderHook(() => useConflicts(), { wrapper: makeWrapper() });
    expect(validateAssignmentsBatch).not.toHaveBeenCalled();
  });

  it('validates each scheduled request against its assigned space (with self-exclusion)', async () => {
    vi.mocked(useRequests).mockReturnValue({ data: [scheduledRequest('r1')] } as any);
    renderHook(() => useConflicts(), { wrapper: makeWrapper() });
    await waitFor(() => expect(validateAssignmentsBatch).toHaveBeenCalledTimes(1));
    expect(validateAssignmentsBatch).toHaveBeenCalledWith([
      expect.objectContaining({
        requestId: 'r1',
        resourceId: 'space-1',
        excludeAssignmentId: 'assign-r1',
      }),
    ]);
  });

  // REGRESSION: a request assigned to a space that does not satisfy its
  // requirement MUST surface as a conflict. This is the exact bug the backend
  // convergence fixes — capability was previously never evaluated because the
  // client list omitted requirements.
  it('surfaces a capability conflict when the space fails a requirement', async () => {
    vi.mocked(useRequests).mockReturnValue({ data: [scheduledRequest('r1')] } as any);
    vi.mocked(validateAssignmentsBatch).mockResolvedValue([capabilityBlocker('r1')] as any);

    const { result } = renderHook(() => useConflicts(), { wrapper: makeWrapper() });
    await waitFor(() => expect(result.current.conflicts.has('r1')).toBe(true));

    const conflicts = result.current.conflicts.get('r1')!;
    expect(conflicts).toHaveLength(1);
    expect(conflicts[0].kind).toBe('connector_mismatch');
    expect(conflicts[0].severity).toBe('error');
    expect(conflicts[0].message).toContain('Crane');
    expect(result.current.conflictingRequestIds.has('r1')).toBe(true);
  });

  it('does not surface a conflict when the backend reports no blockers', async () => {
    vi.mocked(useRequests).mockReturnValue({ data: [scheduledRequest('r1')] } as any);
    vi.mocked(validateAssignmentsBatch).mockResolvedValue([
      { requestId: 'r1', resourceId: 'space-1', result: { severity: 'ok', blockers: [], warnings: [] } },
    ] as any);

    const { result } = renderHook(() => useConflicts(), { wrapper: makeWrapper() });
    await waitFor(() => expect(validateAssignmentsBatch).toHaveBeenCalled());
    expect(result.current.conflicts.has('r1')).toBe(false);
  });

  it('handles backend validation failure gracefully', async () => {
    vi.mocked(useRequests).mockReturnValue({ data: [scheduledRequest('r1')] } as any);
    vi.mocked(validateAssignmentsBatch).mockRejectedValue(new Error('Network error'));

    const { result } = renderHook(() => useConflicts(), { wrapper: makeWrapper() });
    await waitFor(() => expect(result.current).toBeDefined());
    // Failed validation → no capability conflicts, but the hook stays usable.
    expect(result.current.conflicts.has('r1')).toBe(false);
  });
});
