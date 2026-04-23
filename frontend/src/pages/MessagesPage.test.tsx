/** @jsxImportSource react */
import { describe, it, expect, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MessagesPage } from './MessagesPage';

// Mock navigate
const mockNavigate = vi.fn();
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual('react-router-dom');
  return {
    ...actual,
    useNavigate: () => mockNavigate,
  };
});

// Mock the announcements API
vi.mock('@foundation/src/lib/api/user-announcements-api', () => ({
  getActiveAnnouncements: vi.fn().mockResolvedValue({ announcements: [] }),
  markAnnouncementRead: vi.fn().mockResolvedValue(undefined),
}));

function renderWithProviders(ui: React.ReactElement) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
    </QueryClientProvider>
  );
}

describe('MessagesPage', () => {
  it('renders page heading and description', async () => {
    renderWithProviders(<MessagesPage />);

    await waitFor(() => {
      expect(screen.getByText('Messages')).toBeInTheDocument();
    });
    expect(screen.getByText('Stay up to date with platform news')).toBeInTheDocument();
  });

  it('renders Back button', async () => {
    renderWithProviders(<MessagesPage />);

    await waitFor(() => {
      expect(screen.getByText('Back')).toBeInTheDocument();
    });
  });

  it('renders the MessagesTab component', async () => {
    renderWithProviders(<MessagesPage />);

    // MessagesTab shows "No messages at this time." when empty
    await waitFor(() => {
      expect(screen.getByText('No messages at this time.')).toBeInTheDocument();
    });
  });
});
