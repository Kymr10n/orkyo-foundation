import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { AppLayout } from './AppLayout';

vi.mock('@/store/app-store', () => ({
  useAppStore: vi.fn((selector: (s: Record<string, unknown>) => unknown) =>
    selector({ selectedSiteId: 'site-1', setSelectedSiteId: vi.fn() }),
  ),
}));

vi.mock('@/lib/api/site-api', () => ({
  getSites: vi.fn(() =>
    Promise.resolve([{ id: 'site-1', code: 'hq', name: 'HQ' }]),
  ),
}));

vi.mock('@/contexts/AuthContext', () => ({
  useAuth: () => ({ appUser: { isSuperAdmin: false, hasSeenTour: true } }),
}));

vi.mock('@/hooks/useCommandPalette', () => ({
  useCommandPalette: () => ({ isOpen: false, setIsOpen: vi.fn(), open: vi.fn() }),
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

vi.mock('@/components/tour/TourDialog', () => ({
  TourDialog: () => null,
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
});
