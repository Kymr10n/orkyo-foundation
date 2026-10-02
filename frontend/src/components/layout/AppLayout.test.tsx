import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, screen, waitFor, fireEvent } from '@testing-library/react';
import { AppLayout } from './AppLayout';
import { getSites } from '@foundation/src/lib/api/site-api';
import { setViewport, restoreViewport } from '@foundation/src/test-utils/viewport';
import { renderWithQuery } from '@foundation/src/test-utils';
import { useUiActionsStore } from '@foundation/src/store/ui-actions-store';
import { mockAuth } from '@foundation/src/test-utils/auth';
import { useSiteStore } from '@foundation/src/store/site-store';

const initialSiteState = useSiteStore.getState();
beforeEach(() => {
  useSiteStore.setState({ ...initialSiteState, selectedSiteId: 'site-1' }, true);
});

vi.mock('@foundation/src/lib/api/site-api', () => ({
  getSites: vi.fn(() =>
    Promise.resolve([{ id: 'site-1', code: 'hq', name: 'HQ' }]),
  ),
}));

const authState = { hasSeenTour: true };
vi.mock('@foundation/src/contexts/AuthContext', () => ({
  useAuth: () => mockAuth({ appUser: { hasSeenTour: authState.hasSeenTour } }),
}));

vi.mock('./CommandPalette', () => ({
  CommandPalette: ({ open }: { open: boolean }) => <div data-testid="command-palette" data-open={String(open)} />,
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

vi.mock('@foundation/src/components/scan/GlobalScanFlow', () => ({
  GlobalScanFlow: ({ open }: { open: boolean }) => <div data-testid="scan-flow" data-open={String(open)} />,
}));

vi.mock('@foundation/src/components/resources/ResourceStatusSheet', () => ({
  ResourceStatusSheet: () => null,
}));

function renderLayout() {
  // AppLayout loads sites via the useSites() React Query hook, so a client is required.
  const result = renderWithQuery(<AppLayout />, { router: true });
  // A fresh element each time: React skips a rerender of the very same element object.
  return { ...result, rerenderLayout: () => result.rerender(<AppLayout />) };
}

describe('AppLayout', () => {
  beforeEach(() => {
    useUiActionsStore.setState({
      commandPaletteOpen: false,
      tourOpen: false,
      assistantOpen: false,
      scannerOpen: false,
    });
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

  it('opens the scanner when Scan is pressed', async () => {
    renderLayout();
    expect(await screen.findByTestId('scan-flow')).toHaveAttribute('data-open', 'false');

    act(() => useUiActionsStore.getState().openScanner());

    expect(screen.getByTestId('scan-flow')).toHaveAttribute('data-open', 'true');
  });

  it('toggles the command palette on Ctrl+K and Cmd+K, not on a bare K', async () => {
    renderLayout();
    const palette = await screen.findByTestId('command-palette');
    expect(palette).toHaveAttribute('data-open', 'false');

    fireEvent.keyDown(document, { key: 'k' });
    expect(palette).toHaveAttribute('data-open', 'false');

    fireEvent.keyDown(document, { key: 'k', ctrlKey: true });
    expect(palette).toHaveAttribute('data-open', 'true');

    fireEvent.keyDown(document, { key: 'k', metaKey: true });
    expect(palette).toHaveAttribute('data-open', 'false');
  });

  it('opens the tour when the TopBar asks for it', async () => {
    renderLayout();
    await screen.findByTestId('topbar');
    expect(screen.queryByTestId('tour-dialog')).not.toBeInTheDocument();

    act(() => useUiActionsStore.getState().openTour());

    expect(screen.getByTestId('tour-dialog')).toBeInTheDocument();
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
    authState.hasSeenTour = false;
    renderLayout();
    await waitFor(() => {
      expect(screen.getByTestId('tour-dialog')).toBeInTheDocument();
    });
    authState.hasSeenTour = true;
  });

  it('does not auto-show tour when user has already seen it', async () => {
    renderLayout();
    await waitFor(() => expect(screen.getByTestId('topbar')).toBeInTheDocument());
    expect(screen.queryByTestId('tour-dialog')).not.toBeInTheDocument();
  });
});

describe('AppLayout — responsive shell', () => {
  afterEach(restoreViewport);

  it('the frame scrolls vertically only — a wide page clips instead of dragging it sideways', async () => {
    setViewport(500);
    renderLayout();
    await waitFor(() => expect(screen.getByRole('main')).toBeInTheDocument());
    expect(screen.getByRole('main')).toHaveClass('overflow-x-hidden', 'overflow-y-auto');
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
    await waitFor(() => expect(screen.getByTestId('hamburger')).toBeInTheDocument());
    // Drawer starts closed → the sidebar content is not mounted.
    expect(screen.queryByTestId('sidebar')).not.toBeInTheDocument();

    fireEvent.click(screen.getByTestId('hamburger'));

    // Opening the drawer mounts the sidebar in its expanded (forceCollapsed=false) form.
    await waitFor(() => expect(screen.getByTestId('sidebar')).toBeInTheDocument());
    expect(screen.getByTestId('sidebar')).toHaveAttribute('data-forced', 'false');
  });

  it('restores the inline sidebar (and drops the hamburger) when growing past phone width', async () => {
    setViewport(500);
    const { rerenderLayout } = renderLayout();
    await waitFor(() => expect(screen.getByTestId('hamburger')).toBeInTheDocument());
    fireEvent.click(screen.getByTestId('hamburger'));
    await waitFor(() => expect(screen.getByTestId('sidebar')).toBeInTheDocument());

    // Resize up to desktop and re-render: the drawer-close effect runs, the
    // inline store-driven sidebar returns and the hamburger disappears.
    setViewport(1280);
    rerenderLayout();
    await waitFor(() =>
      expect(screen.getByTestId('sidebar')).toHaveAttribute('data-forced', 'undefined'),
    );
    expect(screen.queryByTestId('hamburger')).not.toBeInTheDocument();
  });
});
