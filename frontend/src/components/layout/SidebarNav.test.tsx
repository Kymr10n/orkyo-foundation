import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { SidebarNav } from './SidebarNav';

vi.mock('@foundation/src/store/app-store', () => ({
  useAppStore: vi.fn((selector: (s: Record<string, unknown>) => unknown) =>
    selector({
      isSidebarCollapsed: false,
      setIsSidebarCollapsed: vi.fn(),
    }),
  ),
}));

const authState: { membership: { isTenantAdmin?: boolean } | null } = { membership: null };
vi.mock('@foundation/src/contexts/AuthContext', () => ({
  useAuth: () => ({ membership: authState.membership }),
}));

// User-defined resource types become nav entries; mocked so the nav stays renderable
// without a QueryClient, matching how the store and auth context are handled above.
const resourceTypesState: {
  data: { key: string; displayName: string; displayNamePlural: string; isSystem: boolean }[];
} = {
  data: [],
};
vi.mock('@foundation/src/hooks/useResourceTypes', () => ({
  useResourceTypes: () => ({ data: resourceTypesState.data }),
}));

function renderSidebar(
  initialPath = '/',
  props: { forceCollapsed?: boolean; onNavigate?: () => void } = {},
) {
  return render(
    <MemoryRouter
      initialEntries={[initialPath]}
    >
      <SidebarNav {...props} />
    </MemoryRouter>,
  );
}

describe('SidebarNav', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    authState.membership = null;
    resourceTypesState.data = [];
  });

  it('renders all navigation links', () => {
    renderSidebar();
    expect(screen.getByText('Utilization')).toBeInTheDocument();
    // The nav names the surface — one floorplan holding every placeable type.
    expect(screen.getByText('Floorplan')).toBeInTheDocument();
    expect(screen.getByText('Requests')).toBeInTheDocument();
    expect(screen.getByText('Insights')).toBeInTheDocument();
    expect(screen.getByText('Settings')).toBeInTheDocument();
  });

  it('renders Collapse button', () => {
    renderSidebar();
    expect(screen.getByText('Collapse')).toBeInTheDocument();
  });

  it('links to correct paths', () => {
    renderSidebar();
    const links = screen.getAllByRole('link');
    const hrefs = links.map(l => l.getAttribute('href'));
    expect(hrefs).toContain('/');
    expect(hrefs).toContain('/spaces');
    expect(hrefs).toContain('/requests');
    expect(hrefs).toContain('/insights');
    expect(hrefs).toContain('/settings');
  });

  it('lists every type without a purpose-built page, built-in or not', () => {
    // The test used to be `!isSystem`, which withheld an entry from `tool` — a built-in type
    // that has never had a page of its own, leaving it reachable only by typing the URL.
    resourceTypesState.data = [
      { key: 'space', displayName: 'Space', displayNamePlural: 'Spaces', isSystem: true },
      { key: 'person', displayName: 'Person', displayNamePlural: 'People', isSystem: true },
      { key: 'tool', displayName: 'Tool', displayNamePlural: 'Tools', isSystem: true },
      { key: 'car', displayName: 'Car', displayNamePlural: 'Cars', isSystem: false },
    ];
    renderSidebar();

    // A nav entry names a collection, so it uses the plural the type carries — deriving it
    // would read "Persons", and tenants do not all name things in English.
    expect(screen.getByText('Tools')).toBeInTheDocument();
    expect(screen.getByText('Cars')).toBeInTheDocument();

    const hrefs = screen.getAllByRole('link').map((l) => l.getAttribute('href'));
    expect(hrefs).toContain('/resources/tool');
    expect(hrefs).toContain('/resources/car');
    // Space is an ordinary placeable type now: it gets a generic type entry like any other,
    // while the fixed Floorplan entry stays a site surface, not a type page. Person keeps its
    // purpose-built page rather than gaining a second entry.
    expect(hrefs).toContain('/resources/space');
    expect(hrefs).toContain('/spaces');
    expect(hrefs).not.toContain('/resources/person');
  });

  it('places the derived type entries with the other resources, beneath People', () => {
    resourceTypesState.data = [
      { key: 'space', displayName: 'Space', displayNamePlural: 'Spaces', isSystem: true },
      { key: 'tool', displayName: 'Tool', displayNamePlural: 'Tools', isSystem: true },
    ];
    renderSidebar();

    const hrefs = screen.getAllByRole('link').map((l) => l.getAttribute('href'));
    // Resources group together; Requests and Insights stay after them.
    expect(hrefs.indexOf('/resources/tool')).toBeGreaterThan(hrefs.indexOf('/people'));
    expect(hrefs.indexOf('/resources/tool')).toBeLessThan(hrefs.indexOf('/requests'));
  });

  it('hides the Administration item for non-admins', () => {
    authState.membership = { isTenantAdmin: false };
    renderSidebar();
    expect(screen.queryByText('Administration')).not.toBeInTheDocument();
  });

  it('shows the Administration item for tenant admins, below Settings', () => {
    authState.membership = { isTenantAdmin: true };
    renderSidebar();
    expect(screen.getByText('Administration')).toBeInTheDocument();
    const hrefs = screen.getAllByRole('link').map((l) => l.getAttribute('href'));
    expect(hrefs).toContain('/tenant-admin');
    // Administration is positioned directly after Settings.
    const labels = screen.getAllByRole('link').map((l) => l.textContent);
    expect(labels.indexOf('Administration')).toBe(labels.indexOf('Settings') + 1);
  });

  it('shows the Resources item for tenant admins only', () => {
    authState.membership = { isTenantAdmin: true };
    renderSidebar();

    expect(screen.getByText('Resources')).toBeInTheDocument();
    expect(screen.getAllByRole('link').map((l) => l.getAttribute('href'))).toContain(
      '/configuration',
    );
  });

  it('hides Resources from a member who is not a tenant admin', () => {
    authState.membership = { isTenantAdmin: false };
    renderSidebar();

    // Same gate as Administration: it shapes tenant-wide data, so it is not an editor surface.
    expect(screen.queryByText('Resources')).not.toBeInTheDocument();
  });

  describe('active state', () => {
    const activeLink = () =>
      screen
        .getAllByRole('link')
        .find((l) => l.className.includes('bg-accent') && l.className.includes('font-medium'));

    it('highlights a section when on one of its index-redirect sub-tabs', () => {
      renderSidebar('/spaces/floorplan');
      expect(activeLink()?.getAttribute('href')).toBe('/spaces');
    });

    it('highlights a section on its exact path', () => {
      renderSidebar('/insights');
      expect(activeLink()?.getAttribute('href')).toBe('/insights');
    });

    it('keeps the root item active only on exact "/" (not on sub-routes)', () => {
      renderSidebar('/spaces/floorplan');
      // Root item must NOT be the active one when we are under /spaces.
      expect(activeLink()?.getAttribute('href')).not.toBe('/');
    });

    it('highlights the root item on "/"', () => {
      renderSidebar('/');
      expect(activeLink()?.getAttribute('href')).toBe('/');
    });
  });

  describe('forced presentations (tablet rail / phone drawer)', () => {
    it('hides labels and the collapse toggle when forceCollapsed is true (rail)', () => {
      renderSidebar('/', { forceCollapsed: true });
      // Icon rail: link labels are not rendered, and the toggle is gone.
      expect(screen.queryByText('Utilization')).not.toBeInTheDocument();
      expect(screen.queryByText('Collapse')).not.toBeInTheDocument();
      // Links themselves still present (icons), pointing at the same routes.
      const hrefs = screen.getAllByRole('link').map((l) => l.getAttribute('href'));
      expect(hrefs).toContain('/');
    });

    it('shows labels but no toggle when forceCollapsed is false (drawer)', () => {
      renderSidebar('/', { forceCollapsed: false });
      expect(screen.getByText('Utilization')).toBeInTheDocument();
      expect(screen.queryByText('Collapse')).not.toBeInTheDocument();
    });

    it('calls onNavigate when a link is activated', async () => {
      const onNavigate = vi.fn();
      renderSidebar('/', { forceCollapsed: false, onNavigate });
      await userEvent.click(screen.getByText('Floorplan'));
      expect(onNavigate).toHaveBeenCalledTimes(1);
    });
  });
});
