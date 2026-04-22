/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ConflictsPage } from '@/pages/ConflictsPage';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { Request } from '@/types/requests';

// Mock the store
vi.mock('@/store/app-store', () => ({
  useAppStore: vi.fn((selector) => {
    const mockState = {
      conflicts: new Map([
        [
          'req-1',
          [
            {
              id: 'conflict-1',
              kind: 'load_exceeded',
              severity: 'error',
              message: 'Capacity: Space has 30, but requires 50',
            },
          ],
        ],
        [
          'req-2',
          [
            {
              id: 'conflict-2',
              kind: 'connector_mismatch',
              severity: 'warning',
              message: 'Projector: Required but not available',
            },
            {
              id: 'conflict-3',
              kind: 'size_mismatch',
              severity: 'error',
              message: 'Seating: Space has "Classroom", but requires "Theater"',
            },
          ],
        ],
      ]),
    };
    return selector(mockState);
  }),
}));

// Mock the useUtilization hook
vi.mock('@/hooks/useUtilization', () => ({
  useRequests: vi.fn(() => ({
    data: [
      {
        id: 'req-1',
        name: 'Conference Room Request',
        startTs: '2026-01-28T10:00:00Z',
        endTs: '2026-01-28T12:00:00Z',
        durationMin: 120,
        createdAt: '2026-01-27T10:00:00Z',
        updatedAt: '2026-01-27T10:00:00Z',
      },
      {
        id: 'req-2',
        name: 'Workshop Space Request',
        startTs: '2026-01-29T14:00:00Z',
        endTs: '2026-01-29T17:00:00Z',
        durationMin: 180,
        createdAt: '2026-01-27T11:00:00Z',
        updatedAt: '2026-01-27T11:00:00Z',
      },
    ] as Request[],
    isLoading: false,
  })),
}));

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
    },
  });
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
};

describe('ConflictsPage', () => {
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
      // Workshop Space Request appears twice (has 2 conflicts)
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
      
      const errorBadges = screen.getAllByText('error');
      const warningBadges = screen.getAllByText('warning');
      
      expect(errorBadges).toHaveLength(2);
      expect(warningBadges).toHaveLength(1);
    });

    it('should display conflict kind badges', () => {
      render(<ConflictsPage />, { wrapper: createWrapper() });
      
      // Conflict kinds are displayed with formatted labels, not raw values
      expect(screen.getByText('Load Exceeded')).toBeInTheDocument();
      expect(screen.getByText('Capability Mismatch')).toBeInTheDocument();
      expect(screen.getByText('Size Mismatch')).toBeInTheDocument();
    });

    it('should display scheduled time information', () => {
      render(<ConflictsPage />, { wrapper: createWrapper() });
      
      // Check that scheduled times are displayed (without year in format)
      // Use getAllByText because there are multiple dates (Jan 29 appears twice)
      expect(screen.getAllByText(/Jan 28,/).length).toBeGreaterThan(0);
      expect(screen.getAllByText(/Jan 29,/).length).toBeGreaterThan(0);
    });
  });

  describe('without conflicts', () => {
    it('should display empty state message', async () => {
      // Mock empty conflicts
      const { useAppStore } = await import('@/store/app-store');
      vi.mocked(useAppStore).mockImplementation((selector: any) => {
        const mockState = {
          conflicts: new Map(),
        };
        return selector(mockState);
      });

      render(<ConflictsPage />, { wrapper: createWrapper() });
      
      expect(screen.getByText('No conflicts to display')).toBeInTheDocument();
    });

    it('should display success message in description', async () => {
      // Mock empty conflicts
      const { useAppStore } = await import('@/store/app-store');
      vi.mocked(useAppStore).mockImplementation((selector: any) => {
        const mockState = {
          conflicts: new Map(),
        };
        return selector(mockState);
      });

      render(<ConflictsPage />, { wrapper: createWrapper() });
      
      expect(
        screen.getByText(/No conflicts detected. All scheduled requests meet their requirements./i)
      ).toBeInTheDocument();
    });

    it('should not render any conflict items', async () => {
      // Mock empty conflicts
      const { useAppStore } = await import('@/store/app-store');
      vi.mocked(useAppStore).mockImplementation((selector: any) => {
        const mockState = {
          conflicts: new Map(),
        };
        return selector(mockState);
      });

      render(<ConflictsPage />, { wrapper: createWrapper() });
      
      expect(screen.queryByText('Conference Room Request')).not.toBeInTheDocument();
      expect(screen.queryByText('Workshop Space Request')).not.toBeInTheDocument();
    });
  });

  describe('partial conflicts', () => {
    it('should use singular form for single conflict', async () => {
      // Mock single conflict
      const { useAppStore } = await import('@/store/app-store');
      vi.mocked(useAppStore).mockImplementation((selector: any) => {
        const mockState = {
          conflicts: new Map([
            [
              'req-1',
              [
                {
                  id: 'conflict-1',
                  kind: 'load_exceeded',
                  severity: 'error',
                  message: 'Single conflict',
                },
              ],
            ],
          ]),
        };
        return selector(mockState);
      });

      render(<ConflictsPage />, { wrapper: createWrapper() });
      
      expect(screen.getByText(/1 conflict found/i)).toBeInTheDocument();
    });
  });

  describe('severity icons', () => {
    it('should display error icon for error severity', () => {
      const { container } = render(<ConflictsPage />, { wrapper: createWrapper() });
      
      // Check for AlertCircle icon (error) using text-destructive class
      const errorIcons = container.querySelectorAll('.text-destructive');
      expect(errorIcons.length).toBeGreaterThan(0);
    });

    it('should display warning icon for warning severity', async () => {
      // Need warnings in the data for this test
      const { useAppStore } = await import('@/store/app-store');
      vi.mocked(useAppStore).mockImplementation((selector: any) => {
        const mockState = {
          conflicts: new Map([
            [
              'req-2',
              [
                {
                  id: 'conflict-2',
                  kind: 'connector_mismatch',
                  severity: 'warning',
                  message: 'Projector: Required but not available',
                },
              ],
            ],
          ]),
        };
        return selector(mockState);
      });

      render(<ConflictsPage />, { wrapper: createWrapper() });
      
      // Just check that there is at least one warning badge displayed
      const warningBadges = screen.getAllByText('warning');
      expect(warningBadges.length).toBeGreaterThan(0);
    });
  });

  describe('styling', () => {
    it('should apply error badge styling', async () => {
      // Reset to error severity for this test
      const { useAppStore } = await import('@/store/app-store');
      vi.mocked(useAppStore).mockImplementation((selector: any) => {
        const mockState = {
          conflicts: new Map([
            [
              'req-1',
              [
                {
                  id: 'conflict-1',
                  kind: 'load_exceeded',
                  severity: 'error',
                  message: 'Error conflict',
                },
              ],
            ],
          ]),
        };
        return selector(mockState);
      });

      render(<ConflictsPage />, { wrapper: createWrapper() });
      
      const errorBadges = screen.getAllByText('error');
      errorBadges.forEach((badge) => {
        expect(badge.className).toMatch(/bg-red-100|dark:bg-red-900/);
      });
    });

    it('should apply warning badge styling', async () => {
      // Need to re-mock with warning severity for this test
      const { useAppStore } = await import('@/store/app-store');
      vi.mocked(useAppStore).mockImplementation((selector: any) => {
        const mockState = {
          conflicts: new Map([
            [
              'req-2',
              [
                {
                  id: 'conflict-2',
                  kind: 'connector_mismatch',
                  severity: 'warning',
                  message: 'Projector: Required but not available',
                },
              ],
            ],
          ]),
        };
        return selector(mockState);
      });

      render(<ConflictsPage />, { wrapper: createWrapper() });
      
      const warningBadges = screen.getAllByText('warning');
      warningBadges.forEach((badge) => {
        expect(badge.className).toMatch(/bg-yellow-100|dark:bg-yellow-900/);
      });
    });

    it('should apply hover effect to conflict items', () => {
      const { container } = render(<ConflictsPage />, { wrapper: createWrapper() });
      
      const conflictItems = container.querySelectorAll('[class*="hover:bg-accent"]');
      expect(conflictItems.length).toBeGreaterThan(0);
    });
  });
});
