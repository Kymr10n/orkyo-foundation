/**
 * TopBar tests
 *
 * Covers the "Switch Organization" affordance:
 * - Shown for multi-tenant users who are not in a break-glass session
 * - Hidden for single-tenant users
 * - Hidden during break-glass sessions
 * - Calls switchTenant() in local dev; navigates to apex in production
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import type * as ReactQuery from '@tanstack/react-query';
import { TopBar } from './TopBar';
import { restoreViewport, setViewport } from '@foundation/src/test-utils/viewport';
import { useUiActionsStore } from '@foundation/src/store/ui-actions-store';
import { useSiteStore } from '@foundation/src/store/site-store';
import { useLayoutStore } from '@foundation/src/store/layout-store';
import { useAuth } from '@foundation/src/contexts/AuthContext';
import { mockAuth, type MockAuthOptions } from '@foundation/src/test-utils/auth';

// ── Module mocks ──────────────────────────────────────────────────────────────

const { mockNavigateToApex } = vi.hoisted(() => ({
  mockNavigateToApex: vi.fn(),
}));

const mockSwitchTenant = vi.fn();
const mockLogout = vi.fn();

vi.mock('@foundation/src/contexts/AuthContext', async (importOriginal) => ({
  ...(await importOriginal<object>()),
  useAuth: vi.fn(),
}));

vi.mock('@foundation/src/lib/utils/tenant-navigation', () => ({
  navigateToApex: mockNavigateToApex,
  getCurrentSubdomain: vi.fn(() => null),
}));

const mockSitesData = { current: undefined as unknown };

vi.mock('@tanstack/react-query', async (importOriginal) => ({
  ...(await importOriginal<typeof ReactQuery>()),
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

const initialSiteState = useSiteStore.getState();
const initialLayoutState = useLayoutStore.getState();
beforeEach(() => {
  useSiteStore.setState({ ...initialSiteState, selectedSiteId: null }, true);
  useLayoutStore.setState({ ...initialLayoutState, resolvedTheme: 'dark' }, true);
});

const baseMembership = {
  tenantId: 't1',
  slug: 'demo',
  displayName: 'Demo Corp',
  role: 'admin',
  state: 'active',
};

function authState(overrides: MockAuthOptions = {}) {
  return mockAuth({
    membership: baseMembership,
    sessionData: { tenants: [baseMembership] },
    appUser: { displayName: 'Alice', email: 'alice@example.com' },
    logout: mockLogout,
    switchTenant: mockSwitchTenant,
    ...overrides,
  });
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
  it('is hidden when user has only one tenant', () => {
    vi.mocked(useAuth).mockReturnValue(authState({
      sessionData: { tenants: [baseMembership] },
    }));
    renderTopBar();
    openUserMenu();
    expect(screen.queryByTestId('switch-organization-btn')).not.toBeInTheDocument();
  });

  it('is shown when user has multiple tenants', () => {
    const tenant2 = { ...baseMembership, tenantId: 't2', slug: 'other', displayName: 'Other Corp' };
    vi.mocked(useAuth).mockReturnValue(authState({
      sessionData: { tenants: [baseMembership, tenant2] },
    }));
    renderTopBar();
    openUserMenu();
    expect(screen.getByTestId('switch-organization-btn')).toBeInTheDocument();
  });

  it('is hidden during a break-glass session even when multi-tenant', () => {
    const tenant2 = { ...baseMembership, tenantId: 't2', slug: 'other', displayName: 'Other Corp' };
    vi.mocked(useAuth).mockReturnValue(authState({
      membership: { ...baseMembership, isBreakGlass: true },
      sessionData: { tenants: [baseMembership, tenant2] },
    }));
    renderTopBar();
    openUserMenu();
    expect(screen.queryByTestId('switch-organization-btn')).not.toBeInTheDocument();
  });

  it('is hidden when sessionData is null', () => {
    vi.mocked(useAuth).mockReturnValue(authState({ sessionData: null }));
    renderTopBar();
    openUserMenu();
    expect(screen.queryByTestId('switch-organization-btn')).not.toBeInTheDocument();
  });

  describe('click behaviour', () => {
    const tenant2 = { ...baseMembership, tenantId: 't2', slug: 'other', displayName: 'Other Corp' };

    it('calls navigateToApex("/") in production (returns true) and does not call switchTenant', () => {
      mockNavigateToApex.mockReturnValue(true);
      vi.mocked(useAuth).mockReturnValue(authState({
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
      vi.mocked(useAuth).mockReturnValue(authState({
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
  it('is shown for site admins in a normal session', () => {
    vi.mocked(useAuth).mockReturnValue(authState({ canAccessAdminPage: true }));
    renderTopBar();
    openUserMenu();
    expect(screen.getByTestId('admin-panel-btn')).toBeInTheDocument();
  });

  it('is hidden during a break-glass session', () => {
    vi.mocked(useAuth).mockReturnValue(authState({
      canAccessAdminPage: true,
      membership: { ...baseMembership, isBreakGlass: true },
    }));
    renderTopBar();
    openUserMenu();
    expect(screen.queryByTestId('admin-panel-btn')).not.toBeInTheDocument();
  });

  it('is hidden for non-admin users', () => {
    vi.mocked(useAuth).mockReturnValue(authState({ canAccessAdminPage: false }));
    renderTopBar();
    openUserMenu();
    expect(screen.queryByTestId('admin-panel-btn')).not.toBeInTheDocument();
  });
});

describe('TopBar — Site Selector', () => {
  beforeEach(() => {
    mockSitesData.current = undefined;
  });

  it('is hidden when there is only one site', () => {
    mockSitesData.current = [{ id: 's1', name: 'Default Site' }];
    vi.mocked(useAuth).mockReturnValue(authState());
    renderTopBar();
    expect(screen.queryByText('Select site...')).not.toBeInTheDocument();
  });

  it('is shown when there are multiple sites', () => {
    mockSitesData.current = [
      { id: 's1', name: 'Site Alpha' },
      { id: 's2', name: 'Site Beta' },
    ];
    vi.mocked(useAuth).mockReturnValue(authState());
    renderTopBar();
    // The Building2 icon and select trigger should be present
    expect(screen.getByRole('combobox')).toBeInTheDocument();
  });

  it('is hidden when sites data is empty', () => {
    mockSitesData.current = [];
    vi.mocked(useAuth).mockReturnValue(authState());
    renderTopBar();
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
  });
});

describe('TopBar — mobile navigation hamburger', () => {
  beforeEach(() => {
    vi.mocked(useAuth).mockReturnValue(authState());
  });

  it('is absent when onOpenMobileNav is not provided (tablet/desktop)', () => {
    renderTopBar();
    expect(screen.queryByLabelText('Open navigation menu')).not.toBeInTheDocument();
  });

  it('renders a hamburger and calls onOpenMobileNav when provided (phone)', () => {
    const onOpenMobileNav = vi.fn();
    render(
      <MemoryRouter>
        <TopBar onOpenMobileNav={onOpenMobileNav} />
      </MemoryRouter>,
    );
    const hamburger = screen.getByLabelText('Open navigation menu');
    expect(hamburger).toBeInTheDocument();
    fireEvent.click(hamburger);
    expect(onOpenMobileNav).toHaveBeenCalledTimes(1);
  });
});

describe('TopBar — phone overflow menu', () => {
  beforeEach(() => {
    mockSitesData.current = undefined;
    vi.mocked(useAuth).mockReturnValue(authState());
  });

  it('renders a "More options" trigger', () => {
    renderTopBar();
    expect(screen.getByLabelText('More options')).toBeInTheDocument();
  });

  it('renders a site radio group in the overflow menu when there are multiple sites', async () => {
    const user = userEvent.setup();
    mockSitesData.current = [
      { id: 's1', name: 'Site Alpha' },
      { id: 's2', name: 'Site Beta' },
    ];
    vi.mocked(useAuth).mockReturnValue(authState());
    renderTopBar();

    await user.click(screen.getByLabelText('More options'));

    const radios = screen.getAllByRole('menuitemradio');
    expect(radios).toHaveLength(2);
    expect(screen.getByRole('menuitemradio', { name: 'Site Alpha' })).toBeInTheDocument();
    expect(screen.getByRole('menuitemradio', { name: 'Site Beta' })).toBeInTheDocument();
  });

  it('omits the site radio group when there is a single site', async () => {
    const user = userEvent.setup();
    mockSitesData.current = [{ id: 's1', name: 'Default Site' }];
    vi.mocked(useAuth).mockReturnValue(authState());
    renderTopBar();

    await user.click(screen.getByLabelText('More options'));

    expect(screen.queryByRole('menuitemradio')).not.toBeInTheDocument();
  });

  it('disables the Import item when import is not available for the context', async () => {
    // Default route "/" resolves to the utilization context, which has no import support.
    const user = userEvent.setup();
    renderTopBar();

    await user.click(screen.getByLabelText('More options'));

    const importItem = screen.getByText('Import').closest('[role="menuitem"]');
    expect(importItem).toHaveAttribute('data-disabled');
  });
});

// ── Import/export availability ───────────────────────────────────────────────
// The buttons follow the registry — what a mounted page offers — never the URL.
describe('TopBar — import/export availability', () => {
  beforeEach(() => {
    useUiActionsStore.setState({ exportRegistry: new Map(), importRegistry: new Map() });
  });

  it('disables both buttons when nothing on screen registered a handler', () => {
    renderTopBar();
    expect(screen.getByRole('button', { name: 'Export not available' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Import not available' })).toBeDisabled();
  });

  it('enables export and names it from the registration', () => {
    useUiActionsStore.setState({
      exportRegistry: new Map([
        ['resources:tool', { label: 'Tools', description: 'Tools.', formats: ['csv', 'json'] }],
      ]),
    });
    renderTopBar();

    expect(screen.getByRole('button', { name: 'Export Tools' })).toBeEnabled();
    // Export-only page: import stays off.
    expect(screen.getByRole('button', { name: 'Import not available' })).toBeDisabled();
  });

  it('enables import only when the page registered an import handler', () => {
    useUiActionsStore.setState({
      exportRegistry: new Map([
        ['people', { label: 'People', description: 'People.', formats: ['csv'] }],
      ]),
      importRegistry: new Map([['people', { formats: ['csv'] }]]),
    });
    renderTopBar();

    expect(screen.getByRole('button', { name: 'Import People' })).toBeEnabled();
  });

  it('acts on the most recently registered page when two are mounted', () => {
    // A tab registering inside a page that also registers: the inner one wins.
    useUiActionsStore.setState({
      exportRegistry: new Map([
        ['spaces', { label: 'Spaces', description: 'Spaces.', formats: ['csv'] }],
        ['resources:forklift', { label: 'Forklifts', description: 'Forklifts.', formats: ['csv'] }],
      ]),
    });
    renderTopBar();

    expect(screen.getByRole('button', { name: 'Export Forklifts' })).toBeEnabled();
  });
});

describe('TopBar — calendar subscription availability', () => {
  beforeEach(() => {
    useUiActionsStore.setState({
      exportRegistry: new Map(),
      importRegistry: new Map(),
      calendarFeedRegistry: new Map(),
    });
  });

  it('is disabled when no page offers a schedule to subscribe to', () => {
    renderTopBar();
    expect(
      screen.getByRole('button', { name: 'Calendar subscription not available' }),
    ).toBeDisabled();
  });

  it('is enabled and named from the registration', () => {
    useUiActionsStore.setState({
      calendarFeedRegistry: new Map([
        ['utilization', { label: 'Utilization schedule', description: 'Subscribe once.' }],
      ]),
    });
    renderTopBar();

    expect(
      screen.getByRole('button', { name: 'Subscribe to Utilization schedule' }),
    ).toBeEnabled();
  });

  it('is independent of the export registry', () => {
    // A page can export without offering a feed; the two buttons gate separately.
    useUiActionsStore.setState({
      exportRegistry: new Map([
        ['people', { label: 'People', description: 'People.', formats: ['csv'] }],
      ]),
      calendarFeedRegistry: new Map(),
    });
    renderTopBar();

    expect(screen.getByRole('button', { name: 'Export People' })).toBeEnabled();
    expect(
      screen.getByRole('button', { name: 'Calendar subscription not available' }),
    ).toBeDisabled();
  });
});

describe('TopBar — Scan QR code', () => {
  beforeEach(() => {
    vi.mocked(useAuth).mockReturnValue(authState());
  });

  afterEach(restoreViewport);

  it('is offered on a phone and asks the layout to open the scanner', () => {
    setViewport(375);
    useUiActionsStore.setState({ scannerOpen: false });
    renderTopBar();

    fireEvent.click(screen.getByRole('button', { name: 'Scan QR code' }));

    expect(useUiActionsStore.getState().scannerOpen).toBe(true);
  });
});
