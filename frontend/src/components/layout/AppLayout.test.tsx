import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { AppLayout } from './AppLayout';
import { getSites } from '@foundation/src/lib/api/site-api';

vi.mock('@foundation/src/store/app-store', () => ({
  useAppStore: vi.fn((selector: (s: Record<string, unknown>) => unknown) =>
    selector({ selectedSiteId: 'site-1', setSelectedSiteId: vi.fn() }),
  ),
}));

vi.mock('@foundation/src/lib/api/site-api', () => ({
  getSites: vi.fn(() =>
    Promise.resolve([{ id: 'site-1', code: 'hq', name: 'HQ' }]),
  ),
}));

const mockAppUser = { isSuperAdmin: false, hasSeenTour: true };
vi.mock('@foundation/src/contexts/AuthContext', () => ({
  useAuth: () => ({ appUser: mockAppUser }),
}));

const mockOpenCommandPalette = vi.fn();
vi.mock('@foundation/src/hooks/useCommandPalette', () => ({
  useCommandPalette: () => ({ isOpen: false, setIsOpen: vi.fn(), open: mockOpenCommandPalette }),
}));

// ui-actions-store: default to tick = 0; individual tests override via mockReturnValue
const mockUiActionsStore = vi.fn((sel: (s: Record<string, unknown>) => unknown) =>
  sel({ commandPaletteTick: 0, tourTick: 0 }),
);
vi.mock('@foundation/src/store/ui-actions-store', () => ({
  useUiActionsStore: (sel: (s: Record<string, unknown>) => unknown) => mockUiActionsStore(sel),
}));

vi.mock('./CommandPalette', () => ({
  CommandPalette: () => null,
}));

vi.mock('./FeedbackButton', () => ({
  FeedbackButton: () => <div data-testid="feedback" />,
}));

vi.mock('./SidebarNav', () => ({
  SidebarNav: ({ forceCollapsed }: { forceCollapsed?: boolean }) => (
    <nav data-testid="sidebar" data-forced={String(forceCollapsed)} />
  ),
}));

vi.mock('./TopBar', () => ({
  TopBar: ({ onOpenMobileNav }: { onOpenMobileNav?: () => void }) => (
    <header data-testid="topbar">
      {onOpenMobileNav && (
        <button type="button" data-testid="hamburger" onClick={onOpenMobileNav}>
          menu
        </button>
      )}
    </header>
  ),
}));

vi.mock('@foundation/src/components/tour/TourDialog', () => ({
  TourDialog: ({ open }: { open: boolean }) => (open ? <div data-testid="tour-dialog" /> : null),
}));

function renderLayout() {
  return render(
      <MemoryRouter>
      <AppLayout />
    </MemoryRouter>,
  );
}

describe('AppLayout', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('shows loading initially then renders layout', async () => {
    renderLayout();
    // After sites load, sidebar and topbar appear
    await waitFor(() => {
      expect(screen.getByTestId('topbar')).toBeInTheDocument();
      expect(screen.getByTestId('sidebar')).toBeInTheDocument();
    });
  });

  it('renders feedback button', async () => {
    renderLayout();
    await waitFor(() => {
      expect(screen.getByTestId('feedback')).toBeInTheDocument();
    });
  });

  it('still validates when getSites returns an empty array', async () => {
    vi.mocked(getSites).mockResolvedValueOnce([]);
    renderLayout();
    await waitFor(() => {
      expect(screen.getByTestId('topbar')).toBeInTheDocument();
      expect(screen.getByTestId('sidebar')).toBeInTheDocument();
    });
  });

  it('still validates when getSites throws', async () => {
    vi.mocked(getSites).mockRejectedValueOnce(new Error('Network error'));
    renderLayout();
    await waitFor(() => {
      expect(screen.getByTestId('topbar')).toBeInTheDocument();
      expect(screen.getByTestId('sidebar')).toBeInTheDocument();
    });
  });

  it('auto-shows tour for users who have not seen it', async () => {
    // Override hasSeenTour to false for this test
    (mockAppUser as Record<string, unknown>).hasSeenTour = false;
    renderLayout();
    await waitFor(() => {
      expect(screen.getByTestId('tour-dialog')).toBeInTheDocument();
    });
    // Restore
    (mockAppUser as Record<string, unknown>).hasSeenTour = true;
  });

  it('does not auto-show tour when user has already seen it', async () => {
    renderLayout();
    await waitFor(() => expect(screen.getByTestId('topbar')).toBeInTheDocument());
    expect(screen.queryByTestId('tour-dialog')).not.toBeInTheDocument();
  });
});

describe('AppLayout — responsive shell', () => {
  const originalMatchMedia = window.matchMedia;

  function setViewport(width: number) {
    Object.defineProperty(window, 'matchMedia', {
      writable: true,
      configurable: true,
      value: vi.fn((query: string) => {
        const min = /\(min-width:\s*(\d+)px\)/.exec(query);
        return {
          matches: min ? width >= Number(min[1]) : false,
          media: query,
          onchange: null,
          addEventListener: () => {},
          removeEventListener: () => {},
          dispatchEvent: () => false,
        } as unknown as MediaQueryList;
      }),
    });
  }

  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    Object.defineProperty(window, 'matchMedia', {
      value: originalMatchMedia,
      writable: true,
      configurable: true,
    });
  });

  it('desktop: inline sidebar (store-driven), no hamburger', async () => {
    setViewport(1280);
    renderLayout();
    await waitFor(() => expect(screen.getByTestId('sidebar')).toBeInTheDocument());
    expect(screen.getByTestId('sidebar')).toHaveAttribute('data-forced', 'undefined');
    expect(screen.queryByTestId('hamburger')).not.toBeInTheDocument();
  });

  it('tablet: collapsed icon rail, no hamburger', async () => {
    setViewport(900);
    renderLayout();
    await waitFor(() => expect(screen.getByTestId('sidebar')).toBeInTheDocument());
    expect(screen.getByTestId('sidebar')).toHaveAttribute('data-forced', 'true');
    expect(screen.queryByTestId('hamburger')).not.toBeInTheDocument();
  });

  it('phone: no inline sidebar; hamburger opens the nav drawer', async () => {
    setViewport(500);
    renderLayout();
    await waitFor(() => expect(screen.getByTestId('topbar')).toBeInTheDocument());
    // Drawer starts closed → the sidebar content is not mounted.
    expect(screen.queryByTestId('sidebar')).not.toBeInTheDocument();

    fireEvent.click(screen.getByTestId('hamburger'));

    // Opening the drawer mounts the sidebar in its expanded (forceCollapsed=false) form.
    await waitFor(() => expect(screen.getByTestId('sidebar')).toBeInTheDocument());
    expect(screen.getByTestId('sidebar')).toHaveAttribute('data-forced', 'false');
  });

  it('restores the inline sidebar (and drops the hamburger) when growing past phone width', async () => {
    setViewport(500);
    const { rerender } = renderLayout();
    await waitFor(() => expect(screen.getByTestId('topbar')).toBeInTheDocument());
    fireEvent.click(screen.getByTestId('hamburger'));
    await waitFor(() => expect(screen.getByTestId('sidebar')).toBeInTheDocument());

    // Resize up to desktop and re-render: the drawer-close effect runs, the
    // inline store-driven sidebar returns and the hamburger disappears.
    setViewport(1280);
    rerender(
      <MemoryRouter>
        <AppLayout />
      </MemoryRouter>,
    );
    await waitFor(() =>
      expect(screen.getByTestId('sidebar')).toHaveAttribute('data-forced', 'undefined'),
    );
    expect(screen.queryByTestId('hamburger')).not.toBeInTheDocument();
  });
});
