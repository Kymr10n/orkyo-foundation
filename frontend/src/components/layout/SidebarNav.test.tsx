import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
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
  });

  it('renders all navigation links', () => {
    renderSidebar();
    expect(screen.getByText('Utilization')).toBeInTheDocument();
    expect(screen.getByText('Spaces')).toBeInTheDocument();
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
      await userEvent.click(screen.getByText('Spaces'));
      expect(onNavigate).toHaveBeenCalledTimes(1);
    });
  });
});
