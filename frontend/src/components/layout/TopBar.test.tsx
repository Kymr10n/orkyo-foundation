/**
 * TopBar tests
 *
 * Covers the "Switch Organization" affordance:
 * - Shown for multi-tenant users who are not in a break-glass session
 * - Hidden for single-tenant users
 * - Hidden during break-glass sessions
 * - Calls switchTenant() in local dev; navigates to apex in production
 */

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { TopBar } from './TopBar';

// ── Module mocks ──────────────────────────────────────────────────────────────

const { mockNavigateToApex, mockUseAuth } = vi.hoisted(() => ({
  mockNavigateToApex: vi.fn(),
  mockUseAuth: vi.fn(),
}));

const mockSwitchTenant = vi.fn();
const mockLogout = vi.fn();

vi.mock('@foundation/src/contexts/AuthContext', () => ({
  useAuth: () => mockUseAuth(),
}));

vi.mock('@foundation/src/lib/utils/tenant-navigation', () => ({
  navigateToApex: mockNavigateToApex,
  getCurrentSubdomain: vi.fn(() => null),
}));

vi.mock('@foundation/src/store/app-store', () => ({
  useAppStore: vi.fn((selector: (s: unknown) => unknown) =>
    selector({ scale: 1, anchorTs: new Date('2026-01-01'), user: null, selectedSiteId: null, setUser: vi.fn(), setSelectedSiteId: vi.fn() })
  ),
}));

const mockSitesData = { current: undefined as unknown };

vi.mock('@tanstack/react-query', () => ({
  useQuery: vi.fn((opts: { queryKey: string[] }) => {
    if (opts.queryKey[0] === 'sites') {
      return { data: mockSitesData.current, isLoading: false };
    }
    return { data: undefined, isLoading: false };
  }),
}));

vi.mock('@foundation/src/lib/api/site-api', () => ({ getSites: vi.fn() }));
vi.mock('@foundation/src/lib/api/user-announcements-api', () => ({ getUnreadAnnouncementCount: vi.fn() }));

// ── Helpers ───────────────────────────────────────────────────────────────────

const baseMembership = {
  tenantId: 't1',
  slug: 'demo',
  displayName: 'Demo Corp',
  role: 'admin',
  state: 'active',
};

function authState(overrides: Record<string, unknown> = {}) {
  return {
    membership: baseMembership,
    sessionData: { tenants: [baseMembership] },
    appUser: { displayName: 'Alice', email: 'alice@example.com' },
    logout: mockLogout,
    clearMembership: vi.fn(),
    switchTenant: mockSwitchTenant,
    ...overrides,
  };
}

function renderTopBar() {
  return render(
      <MemoryRouter>
      <TopBar />
    </MemoryRouter>,
  );
}

/** Open the user menu popover so its content is rendered. */
function openUserMenu() {
  fireEvent.click(screen.getByTestId('user-menu-trigger'));
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe('TopBar — Switch Organization', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('is hidden when user has only one tenant', () => {
    mockUseAuth.mockReturnValue(authState({
      sessionData: { tenants: [baseMembership] },
    }));
    renderTopBar();
    openUserMenu();
    expect(screen.queryByTestId('switch-organization-btn')).not.toBeInTheDocument();
  });

  it('is shown when user has multiple tenants', () => {
    const tenant2 = { ...baseMembership, tenantId: 't2', slug: 'other', displayName: 'Other Corp' };
    mockUseAuth.mockReturnValue(authState({
      sessionData: { tenants: [baseMembership, tenant2] },
    }));
    renderTopBar();
    openUserMenu();
    expect(screen.getByTestId('switch-organization-btn')).toBeInTheDocument();
  });

  it('is hidden during a break-glass session even when multi-tenant', () => {
    const tenant2 = { ...baseMembership, tenantId: 't2', slug: 'other', displayName: 'Other Corp' };
    mockUseAuth.mockReturnValue(authState({
      membership: { ...baseMembership, isBreakGlass: true },
      sessionData: { tenants: [baseMembership, tenant2] },
    }));
    renderTopBar();
    openUserMenu();
    expect(screen.queryByTestId('switch-organization-btn')).not.toBeInTheDocument();
  });

  it('is hidden when sessionData is null', () => {
    mockUseAuth.mockReturnValue(authState({ sessionData: null }));
    renderTopBar();
    openUserMenu();
    expect(screen.queryByTestId('switch-organization-btn')).not.toBeInTheDocument();
  });

  describe('click behaviour', () => {
    const tenant2 = { ...baseMembership, tenantId: 't2', slug: 'other', displayName: 'Other Corp' };

    it('calls navigateToApex("/") in production (returns true) and does not call switchTenant', () => {
      mockNavigateToApex.mockReturnValue(true);
      mockUseAuth.mockReturnValue(authState({
        sessionData: { tenants: [baseMembership, tenant2] },
      }));
      renderTopBar();
      openUserMenu();
      fireEvent.click(screen.getByTestId('switch-organization-btn'));
      expect(mockNavigateToApex).toHaveBeenCalledWith('/');
      expect(mockSwitchTenant).not.toHaveBeenCalled();
    });

    it('calls switchTenant() in local dev when navigateToApex returns false', () => {
      mockNavigateToApex.mockReturnValue(false);
      mockUseAuth.mockReturnValue(authState({
        sessionData: { tenants: [baseMembership, tenant2] },
      }));
      renderTopBar();
      openUserMenu();
      fireEvent.click(screen.getByTestId('switch-organization-btn'));
      expect(mockNavigateToApex).toHaveBeenCalledWith('/');
      expect(mockSwitchTenant).toHaveBeenCalledOnce();
    });
  });
});

describe('TopBar — Admin Panel', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('is shown for site admins in a normal session', () => {
    mockUseAuth.mockReturnValue(authState({ canAccessAdminPage: true }));
    renderTopBar();
    openUserMenu();
    expect(screen.getByTestId('admin-panel-btn')).toBeInTheDocument();
  });

  it('is hidden during a break-glass session', () => {
    mockUseAuth.mockReturnValue(authState({
      canAccessAdminPage: true,
      membership: { ...baseMembership, isBreakGlass: true },
    }));
    renderTopBar();
    openUserMenu();
    expect(screen.queryByTestId('admin-panel-btn')).not.toBeInTheDocument();
  });

  it('is hidden for non-admin users', () => {
    mockUseAuth.mockReturnValue(authState({ canAccessAdminPage: false }));
    renderTopBar();
    openUserMenu();
    expect(screen.queryByTestId('admin-panel-btn')).not.toBeInTheDocument();
  });
});

describe('TopBar — Site Selector', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockSitesData.current = undefined;
  });

  it('is hidden when there is only one site', () => {
    mockSitesData.current = [{ id: 's1', name: 'Default Site' }];
    mockUseAuth.mockReturnValue(authState());
    renderTopBar();
    expect(screen.queryByText('Select site...')).not.toBeInTheDocument();
  });

  it('is shown when there are multiple sites', () => {
    mockSitesData.current = [
      { id: 's1', name: 'Site Alpha' },
      { id: 's2', name: 'Site Beta' },
    ];
    mockUseAuth.mockReturnValue(authState());
    renderTopBar();
    // The Building2 icon and select trigger should be present
    expect(screen.getByRole('combobox')).toBeInTheDocument();
  });

  it('is hidden when sites data is empty', () => {
    mockSitesData.current = [];
    mockUseAuth.mockReturnValue(authState());
    renderTopBar();
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
  });
});
