import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
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
  SidebarNav: () => <nav data-testid="sidebar" />,
}));

vi.mock('./TopBar', () => ({
  TopBar: () => <header data-testid="topbar" />,
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
