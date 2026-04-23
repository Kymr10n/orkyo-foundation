import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { BreakGlassBanner } from './BreakGlassBanner';

// ── Mocks ────────────────────────────────────────────────────────────────────

const { mockClearMembership, mockRenew, mockGetStatus, mockExit, mockNavigateToApex } = vi.hoisted(() => ({
  mockClearMembership: vi.fn(),
  mockRenew: vi.fn(),
  mockGetStatus: vi.fn(),
  mockExit: vi.fn(),
  mockNavigateToApex: vi.fn(() => true),
}));

let mockMembership: Record<string, unknown> | null = null;

vi.mock('@foundation/src/contexts/AuthContext', () => ({
  useAuth: () => ({
    membership: mockMembership,
    clearMembership: mockClearMembership,
  }),
}));

vi.mock('@foundation/src/lib/api/admin-api', () => ({
  renewBreakGlassSession: mockRenew,
  getBreakGlassSessionStatus: mockGetStatus,
  auditBreakGlassExit: mockExit,
}));

vi.mock('@foundation/src/lib/utils/tenant-navigation', () => ({
  navigateToApex: mockNavigateToApex,
}));

// ── Helpers ──────────────────────────────────────────────────────────────────

const BASE_TIME = new Date('2026-04-18T12:00:00Z').getTime();

function breakGlassMembership(overrides: Record<string, unknown> = {}) {
  return {
    tenantId: 'tid',
    slug: 'acme',
    displayName: 'Acme',
    role: 'admin',
    state: 'active',
    isBreakGlass: true,
    breakGlassSessionId: 'session-123',
    ...overrides,
  };
}

function sessionStatus(overrides: Record<string, unknown> = {}) {
  return {
    sessionId: 'session-123',
    tenantSlug: 'acme',
    createdAt: '2026-04-18T12:00:00Z',
    expiresAt: '2026-04-18T13:00:00Z',
    absoluteExpiresAt: '2026-04-18T20:00:00Z',
    ...overrides,
  };
}

// ── Tests ────────────────────────────────────────────────────────────────────

describe('BreakGlassBanner', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockMembership = null;
    mockGetStatus.mockResolvedValue(sessionStatus());
    mockRenew.mockResolvedValue(sessionStatus({ expiresAt: '2026-04-18T14:00:00Z' }));
    mockExit.mockResolvedValue(undefined);
  });

  it('renders nothing when membership is not break-glass', () => {
    mockMembership = { slug: 'acme', isBreakGlass: false };
    const { container } = render(<BreakGlassBanner now={() => BASE_TIME} />);
    expect(container.innerHTML).toBe('');
  });

  it('renders the banner when membership is break-glass', async () => {
    mockMembership = breakGlassMembership();
    render(<BreakGlassBanner now={() => BASE_TIME} />);

    await waitFor(() => {
      expect(screen.getByTestId('break-glass-banner')).toBeInTheDocument();
    });
    expect(screen.getByText('Break-glass session active')).toBeInTheDocument();
  });

  it('fetches session status on mount', async () => {
    mockMembership = breakGlassMembership();
    render(<BreakGlassBanner now={() => BASE_TIME} />);

    await waitFor(() => {
      expect(mockGetStatus).toHaveBeenCalledWith('acme');
    });
  });

  it('shows countdown after status loads', async () => {
    mockMembership = breakGlassMembership();
    // 30 minutes remaining
    const now = new Date('2026-04-18T12:30:00Z').getTime();
    render(<BreakGlassBanner now={() => now} />);

    await waitFor(() => {
      expect(screen.getByTestId('break-glass-remaining')).toHaveTextContent('30:00 remaining');
    });
  });

  it('calls renewBreakGlassSession when Extend is clicked', async () => {
    mockMembership = breakGlassMembership();
    render(<BreakGlassBanner now={() => BASE_TIME} />);

    await waitFor(() => {
      expect(screen.getByTestId('break-glass-remaining')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('break-glass-extend'));

    await waitFor(() => {
      expect(mockRenew).toHaveBeenCalledWith('session-123');
    });
  });

  it('calls clearMembership and navigates on Exit click', async () => {
    mockMembership = breakGlassMembership();
    render(<BreakGlassBanner now={() => BASE_TIME} />);

    await waitFor(() => {
      expect(screen.getByTestId('break-glass-remaining')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('break-glass-exit'));

    expect(mockClearMembership).toHaveBeenCalled();
    expect(mockNavigateToApex).toHaveBeenCalledWith('/admin');
  });

  it('fires audit exit on Exit click', async () => {
    mockMembership = breakGlassMembership();
    render(<BreakGlassBanner now={() => BASE_TIME} />);

    await waitFor(() => {
      expect(screen.getByTestId('break-glass-remaining')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('break-glass-exit'));

    await waitFor(() => {
      expect(mockExit).toHaveBeenCalledWith('session-123');
    });
  });

  it('disables Extend button when hard cap is reached', async () => {
    mockMembership = breakGlassMembership();
    // Time is past the absolute cap
    const pastCap = new Date('2026-04-18T20:01:00Z').getTime();
    mockGetStatus.mockResolvedValue(
      sessionStatus({ expiresAt: '2026-04-18T20:00:00Z' }),
    );
    render(<BreakGlassBanner now={() => pastCap} />);

    await waitFor(() => {
      expect(screen.getByTestId('break-glass-extend')).toBeDisabled();
    });
    expect(screen.getByText('Cap reached')).toBeInTheDocument();
  });

  it('shows urgent styling when < 5 minutes remain', async () => {
    mockMembership = breakGlassMembership();
    // 2 minutes before expiry
    const nearExpiry = new Date('2026-04-18T12:58:00Z').getTime();
    render(<BreakGlassBanner now={() => nearExpiry} />);

    await waitFor(() => {
      const banner = screen.getByTestId('break-glass-banner');
      expect(banner.className).toContain('bg-destructive');
    });
  });

  it('auto-exits when remaining time reaches zero', async () => {
    mockMembership = breakGlassMembership();
    // Already expired
    const expired = new Date('2026-04-18T13:01:00Z').getTime();
    render(<BreakGlassBanner now={() => expired} />);

    await waitFor(() => {
      expect(mockClearMembership).toHaveBeenCalled();
      expect(mockNavigateToApex).toHaveBeenCalledWith('/admin');
    });
  });

  it('auto-exits when server returns no active session', async () => {
    mockMembership = breakGlassMembership();
    mockGetStatus.mockResolvedValue(null);
    render(<BreakGlassBanner now={() => BASE_TIME} />);

    await waitFor(() => {
      expect(mockClearMembership).toHaveBeenCalled();
      expect(mockNavigateToApex).toHaveBeenCalledWith('/admin');
    });
  });
});
