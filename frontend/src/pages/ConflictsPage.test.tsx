import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { ConflictsPage } from '@foundation/src/pages/ConflictsPage';
import { useConflicts } from '@foundation/src/hooks/useConflicts';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { Request, Conflict } from '@foundation/src/types/requests';

const mockRequests: Request[] = [
  {
    id: 'req-1',
    name: 'Conference Room Request',
    startTs: '2026-01-28T10:00:00Z',
    endTs: '2026-01-28T12:00:00Z',
    durationMin: 120,
    createdAt: '2026-01-27T10:00:00Z',
    updatedAt: '2026-01-27T10:00:00Z',
  } as Request,
  {
    id: 'req-2',
    name: 'Workshop Space Request',
    startTs: '2026-01-29T14:00:00Z',
    endTs: '2026-01-29T17:00:00Z',
    durationMin: 180,
    createdAt: '2026-01-27T11:00:00Z',
    updatedAt: '2026-01-27T11:00:00Z',
  } as Request,
];

const mockConflicts = new Map<string, Conflict[]>([
  ['req-1', [{ id: 'conflict-1', kind: 'load_exceeded', severity: 'error', message: 'Capacity: Space has 30, but requires 50' }]],
  ['req-2', [
    { id: 'conflict-2', kind: 'connector_mismatch', severity: 'warning', message: 'Projector: Required but not available' },
    { id: 'conflict-3', kind: 'size_mismatch', severity: 'error', message: 'Seating: Space has "Classroom", but requires "Theater"' },
  ]],
]);

// Mock useConflicts — the new source of conflict data
vi.mock('@foundation/src/hooks/useConflicts', () => ({
  useConflicts: vi.fn(() => ({
    conflicts: mockConflicts,
    conflictingRequestIds: new Set(['req-1', 'req-2']),
    schedulingValidation: new Map(),
    spaceCapacities: new Map(),
  })),
}));

// Mock useRequests — still called directly in ConflictsPage for request details
vi.mock('@foundation/src/hooks/useUtilization', () => ({
  useRequests: vi.fn(() => ({ data: mockRequests, isLoading: false })),
  useSpaces: vi.fn(() => ({ data: [], isLoading: false })),
}));

const mockOpen = vi.fn();
vi.mock('@foundation/src/components/requests/useRequestEditor', () => ({
  useRequestEditor: () => ({ open: mockOpen, dialogs: null }),
}));

const createWrapper = () => {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
};

describe('ConflictsPage', () => {
  beforeEach(() => {
    mockOpen.mockClear();
  });

  describe('with conflicts', () => {
    it('should render conflicts page with title', () => {
      render(<ConflictsPage />, { wrapper: createWrapper() });
      expect(screen.getByText('Conflicts')).toBeInTheDocument();
    });

    it('should display conflict count in description', () => {
      render(<ConflictsPage />, { wrapper: createWrapper() });
      expect(screen.getByText(/3 conflicts found/i)).toBeInTheDocument();
    });

    it('should render all conflict items', () => {
      render(<ConflictsPage />, { wrapper: createWrapper() });
      expect(screen.getByText('Conference Room Request')).toBeInTheDocument();
      expect(screen.getAllByText('Workshop Space Request')).toHaveLength(2);
    });

    it('should display conflict messages', () => {
      render(<ConflictsPage />, { wrapper: createWrapper() });
      expect(screen.getByText('Capacity: Space has 30, but requires 50')).toBeInTheDocument();
      expect(screen.getByText('Projector: Required but not available')).toBeInTheDocument();
      expect(screen.getByText('Seating: Space has "Classroom", but requires "Theater"')).toBeInTheDocument();
    });

    it('should display severity badges', () => {
      render(<ConflictsPage />, { wrapper: createWrapper() });
      expect(screen.getAllByText('error')).toHaveLength(2);
      expect(screen.getAllByText('warning')).toHaveLength(1);
    });

    it('should display conflict kind badges', () => {
      render(<ConflictsPage />, { wrapper: createWrapper() });
      expect(screen.getByText('Load Exceeded')).toBeInTheDocument();
      expect(screen.getByText('Capability Mismatch')).toBeInTheDocument();
      expect(screen.getByText('Size Mismatch')).toBeInTheDocument();
    });

    it('should display scheduled time information', () => {
      render(<ConflictsPage />, { wrapper: createWrapper() });
      expect(screen.getAllByText(/Jan 28,/).length).toBeGreaterThan(0);
      expect(screen.getAllByText(/Jan 29,/).length).toBeGreaterThan(0);
    });
  });

  describe('without conflicts', () => {
    const setupEmpty = async () => {
      const { useConflicts } = await import('@foundation/src/hooks/useConflicts');
      vi.mocked(useConflicts).mockReturnValue({
        conflicts: new Map(),
        conflictingRequestIds: new Set(),
        schedulingValidation: new Map(),
        spaceCapacities: new Map(),
      });
    };

    it('should display empty state message', async () => {
      await setupEmpty();
      render(<ConflictsPage />, { wrapper: createWrapper() });
      expect(screen.getByText('No conflicts to display')).toBeInTheDocument();
    });

    it('should display success message in description', async () => {
      await setupEmpty();
      render(<ConflictsPage />, { wrapper: createWrapper() });
      expect(screen.getByText(/No conflicts detected. All scheduled requests meet their requirements./i)).toBeInTheDocument();
    });

    it('should not render any conflict items', async () => {
      await setupEmpty();
      render(<ConflictsPage />, { wrapper: createWrapper() });
      expect(screen.queryByText('Conference Room Request')).not.toBeInTheDocument();
    });
  });

  describe('partial conflicts', () => {
    it('should use singular form for single conflict', async () => {
      const { useConflicts } = await import('@foundation/src/hooks/useConflicts');
      vi.mocked(useConflicts).mockReturnValue({
        conflicts: new Map([['req-1', [{ id: 'c1', kind: 'load_exceeded', severity: 'error', message: 'Single conflict' }]]]),
        conflictingRequestIds: new Set(['req-1']),
        schedulingValidation: new Map(),
        spaceCapacities: new Map(),
      });
      render(<ConflictsPage />, { wrapper: createWrapper() });
      expect(screen.getByText(/1 conflict found/i)).toBeInTheDocument();
    });
  });

  describe('severity icons', () => {
    it('should display error icon for error severity', () => {
      const { container } = render(<ConflictsPage />, { wrapper: createWrapper() });
      const errorIcons = container.querySelectorAll('.text-destructive');
      expect(errorIcons.length).toBeGreaterThan(0);
    });
  });

  describe('styling', () => {
    it('should apply hover effect to conflict items', () => {
      const { container } = render(<ConflictsPage />, { wrapper: createWrapper() });
      const conflictItems = container.querySelectorAll('[class*="hover:bg-accent"]');
      expect(conflictItems.length).toBeGreaterThan(0);
    });

    it('should apply error badge styling', () => {
      render(<ConflictsPage />, { wrapper: createWrapper() });
      const errorBadges = screen.getAllByText('error');
      errorBadges.forEach((badge) => {
        expect(badge.className).toMatch(/bg-red-100|dark:bg-red-900/);
      });
    });
  });

  describe('interaction', () => {
    beforeEach(async () => {
      const { useConflicts } = await import('@foundation/src/hooks/useConflicts');
      vi.mocked(useConflicts).mockReturnValue({
        conflicts: mockConflicts,
        conflictingRequestIds: new Set(['req-1', 'req-2']),
        schedulingValidation: new Map(),
        spaceCapacities: new Map(),
      });
    });

    it('conflict rows have role="button"', () => {
      render(<ConflictsPage />, { wrapper: createWrapper() });
      const buttons = screen.getAllByRole('button');
      // 3 conflicts from mockConflicts, all rendered as buttons
      expect(buttons.length).toBeGreaterThanOrEqual(3);
    });

    it('clicking a conflict row calls open with the associated request', () => {
      render(<ConflictsPage />, { wrapper: createWrapper() });
      const row = screen
        .getByText('Capacity: Space has 30, but requires 50')
        .closest('[role="button"]')!;
      fireEvent.click(row);
      expect(mockOpen).toHaveBeenCalledTimes(1);
      expect(mockOpen).toHaveBeenCalledWith(mockRequests[0]);
    });

    it('pressing Enter on a conflict row calls open', () => {
      render(<ConflictsPage />, { wrapper: createWrapper() });
      const row = screen
        .getByText('Capacity: Space has 30, but requires 50')
        .closest('[role="button"]')!;
      fireEvent.keyDown(row, { key: 'Enter' });
      expect(mockOpen).toHaveBeenCalledWith(mockRequests[0]);
    });

    it('pressing Space on a conflict row calls open', () => {
      render(<ConflictsPage />, { wrapper: createWrapper() });
      const row = screen
        .getByText('Capacity: Space has 30, but requires 50')
        .closest('[role="button"]')!;
      fireEvent.keyDown(row, { key: ' ' });
      expect(mockOpen).toHaveBeenCalledWith(mockRequests[0]);
    });

    it('other keys do not trigger open', () => {
      render(<ConflictsPage />, { wrapper: createWrapper() });
      const row = screen
        .getByText('Capacity: Space has 30, but requires 50')
        .closest('[role="button"]')!;
      fireEvent.keyDown(row, { key: 'Tab' });
      expect(mockOpen).not.toHaveBeenCalled();
    });

    describe('overlap peer link', () => {
      const overlapConflicts = new Map<string, Conflict[]>([
        ['req-1', [{
          id: 'overlap-1',
          kind: 'overlap' as const,
          severity: 'error' as const,
          peerRequestId: 'req-2',
          message: 'Overlaps with "Workshop Space Request" in the same space',
        }]],
      ]);

      beforeEach(async () => {
        const { useConflicts } = await import('@foundation/src/hooks/useConflicts');
        vi.mocked(useConflicts).mockReturnValue({
          conflicts: overlapConflicts,
          conflictingRequestIds: new Set(['req-1']),
          schedulingValidation: new Map(),
          spaceCapacities: new Map(),
        });
      });

      it('renders "View other request" link when peerRequestId is set', () => {
        render(<ConflictsPage />, { wrapper: createWrapper() });
        expect(
          screen.getByText(/View other request: Workshop Space Request/)
        ).toBeInTheDocument();
      });

      it('does not render a peer link for non-overlap conflicts', async () => {
        const { useConflicts } = await import('@foundation/src/hooks/useConflicts');
        vi.mocked(useConflicts).mockReturnValue({
          conflicts: new Map([['req-1', [{ id: 'c1', kind: 'load_exceeded' as const, severity: 'error' as const, message: 'Capacity exceeded' }]]]),
          conflictingRequestIds: new Set(['req-1']),
          schedulingValidation: new Map(),
          spaceCapacities: new Map(),
        });
        render(<ConflictsPage />, { wrapper: createWrapper() });
        expect(screen.queryByText(/View other request/)).not.toBeInTheDocument();
      });

      it('clicking "View other request" calls open with the peer request', () => {
        render(<ConflictsPage />, { wrapper: createWrapper() });
        fireEvent.click(screen.getByText(/View other request:/));
        expect(mockOpen).toHaveBeenCalledWith(mockRequests[1]);
      });

      it('clicking "View other request" calls open exactly once (stopPropagation prevents row click)', () => {
        render(<ConflictsPage />, { wrapper: createWrapper() });
        fireEvent.click(screen.getByText(/View other request:/));
        expect(mockOpen).toHaveBeenCalledTimes(1);
        expect(mockOpen).toHaveBeenCalledWith(mockRequests[1]);
      });
    });
  });

  describe('getConflictKindLabel — less-common kind labels', () => {
    const kindCases: [string, string][] = [
      ['below_min_duration', 'Below Minimum Duration'],
      ['before_earliest_start', 'Before Earliest Start'],
      ['after_latest_end', 'After Latest End'],
      ['capacity_exceeded', 'Capacity Exceeded'],
      ['unknown_xyz', 'unknown_xyz'],
    ];

    it.each(kindCases)('kind "%s" renders label "%s"', (kind, label) => {
      vi.mocked(useConflicts).mockReturnValue({
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        conflicts: new Map([['req-1', [{ id: 'c1', kind: kind as any, severity: 'error' as const, message: 'msg' }]]]) as any,
        conflictingRequestIds: new Set(['req-1']),
        schedulingValidation: new Map(),
        spaceCapacities: new Map(),
      });
      render(<ConflictsPage />, { wrapper: createWrapper() });
      expect(screen.getByText(label)).toBeInTheDocument();
    });
  });

  describe('getSeverityIcon — default (info) case', () => {
    it('renders content for unknown severity without crashing', () => {
      vi.mocked(useConflicts).mockReturnValue({
        conflicts: new Map([['req-1', [{ id: 'c1', kind: 'load_exceeded' as const, severity: 'info' as unknown as 'error', message: 'info msg' }]]]),
        conflictingRequestIds: new Set(['req-1']),
        schedulingValidation: new Map(),
        spaceCapacities: new Map(),
      });
      render(<ConflictsPage />, { wrapper: createWrapper() });
      expect(screen.getByText('info msg')).toBeInTheDocument();
    });
  });
});
