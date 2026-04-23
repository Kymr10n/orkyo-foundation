/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MessagesTab } from './MessagesTab';

// Mock the user announcements API
const mockGetActiveAnnouncements = vi.fn();
const mockMarkAnnouncementRead = vi.fn();

vi.mock('@foundation/src/lib/api/user-announcements-api', () => ({
  getActiveAnnouncements: (...args: unknown[]) => mockGetActiveAnnouncements(...args),
  markAnnouncementRead: (...args: unknown[]) => mockMarkAnnouncementRead(...args),
}));

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
}

const unreadAnnouncement = {
  id: '11111111-1111-1111-1111-111111111111',
  title: 'Scheduled Maintenance',
  body: 'The platform will be down for maintenance on Friday.',
  isImportant: false,
  createdAt: '2026-03-01T10:00:00Z',
  updatedAt: '2026-03-01T10:00:00Z',
  isRead: false,
};

const readAnnouncement = {
  id: '22222222-2222-2222-2222-222222222222',
  title: 'Welcome to Orkyo',
  body: 'Thanks for joining!',
  isImportant: false,
  createdAt: '2026-02-20T10:00:00Z',
  updatedAt: '2026-02-20T10:00:00Z',
  isRead: true,
};

const importantAnnouncement = {
  id: '33333333-3333-3333-3333-333333333333',
  title: 'Critical Security Update',
  body: 'Please update your passwords immediately.',
  isImportant: true,
  createdAt: '2026-03-02T08:00:00Z',
  updatedAt: '2026-03-02T08:00:00Z',
  isRead: false,
};

describe('MessagesTab', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGetActiveAnnouncements.mockResolvedValue({ announcements: [] });
    mockMarkAnnouncementRead.mockResolvedValue(undefined);
  });

  // ========================================================================
  // Loading & Empty state
  // ========================================================================

  it('should show loading state', () => {
    mockGetActiveAnnouncements.mockReturnValue(new Promise(() => {}));
    render(<MessagesTab />, { wrapper: createWrapper() });
    expect(screen.getByText('Loading messages...')).toBeInTheDocument();
  });

  it('should show empty state when no messages', async () => {
    render(<MessagesTab />, { wrapper: createWrapper() });
    await waitFor(() => {
      expect(screen.getByText('No messages at this time.')).toBeInTheDocument();
    });
  });

  it('should show "Messages" heading', async () => {
    render(<MessagesTab />, { wrapper: createWrapper() });
    await waitFor(() => {
      expect(screen.getByText('Messages')).toBeInTheDocument();
    });
  });

  it('should show description text', async () => {
    render(<MessagesTab />, { wrapper: createWrapper() });
    await waitFor(() => {
      expect(screen.getByText('Platform announcements from the Orkyo team')).toBeInTheDocument();
    });
  });

  // ========================================================================
  // Rendering announcements
  // ========================================================================

  it('should render announcement titles', async () => {
    mockGetActiveAnnouncements.mockResolvedValue({
      announcements: [unreadAnnouncement, readAnnouncement],
    });

    render(<MessagesTab />, { wrapper: createWrapper() });
    await waitFor(() => {
      expect(screen.getByText('Scheduled Maintenance')).toBeInTheDocument();
      expect(screen.getByText('Welcome to Orkyo')).toBeInTheDocument();
    });
  });

  it('should show "unread" badge when there are unread messages', async () => {
    mockGetActiveAnnouncements.mockResolvedValue({
      announcements: [unreadAnnouncement, readAnnouncement],
    });

    render(<MessagesTab />, { wrapper: createWrapper() });
    await waitFor(() => {
      expect(screen.getByText('1 unread')).toBeInTheDocument();
    });
  });

  it('should not show "unread" badge when all messages are read', async () => {
    mockGetActiveAnnouncements.mockResolvedValue({
      announcements: [readAnnouncement],
    });

    render(<MessagesTab />, { wrapper: createWrapper() });
    await waitFor(() => {
      expect(screen.queryByText(/unread/)).not.toBeInTheDocument();
    });
  });

  // ========================================================================
  // Expanding / collapsing & mark-as-read
  // ========================================================================

  it('should expand announcement body when clicked', async () => {
    mockGetActiveAnnouncements.mockResolvedValue({
      announcements: [readAnnouncement],
    });

    const user = userEvent.setup();
    render(<MessagesTab />, { wrapper: createWrapper() });

    await waitFor(() => screen.getByText('Welcome to Orkyo'));

    // Body should not be visible initially
    expect(screen.queryByText('Thanks for joining!')).not.toBeInTheDocument();

    // Click to expand
    await user.click(screen.getByText('Welcome to Orkyo'));
    expect(screen.getByText('Thanks for joining!')).toBeInTheDocument();
  });

  it('should collapse announcement when clicked again', async () => {
    mockGetActiveAnnouncements.mockResolvedValue({
      announcements: [readAnnouncement],
    });

    const user = userEvent.setup();
    render(<MessagesTab />, { wrapper: createWrapper() });

    await waitFor(() => screen.getByText('Welcome to Orkyo'));

    // Expand
    await user.click(screen.getByText('Welcome to Orkyo'));
    expect(screen.getByText('Thanks for joining!')).toBeInTheDocument();

    // Collapse
    await user.click(screen.getByText('Welcome to Orkyo'));
    expect(screen.queryByText('Thanks for joining!')).not.toBeInTheDocument();
  });

  it('should call markAnnouncementRead when expanding an unread announcement', async () => {
    mockGetActiveAnnouncements.mockResolvedValue({
      announcements: [unreadAnnouncement],
    });

    const user = userEvent.setup();
    render(<MessagesTab />, { wrapper: createWrapper() });

    await waitFor(() => screen.getByText('Scheduled Maintenance'));
    await user.click(screen.getByText('Scheduled Maintenance'));

    await waitFor(() => {
      expect(mockMarkAnnouncementRead).toHaveBeenCalledWith(unreadAnnouncement.id, expect.anything());
    });
  });

  it('should NOT call markAnnouncementRead when expanding a read announcement', async () => {
    mockGetActiveAnnouncements.mockResolvedValue({
      announcements: [readAnnouncement],
    });

    const user = userEvent.setup();
    render(<MessagesTab />, { wrapper: createWrapper() });

    await waitFor(() => screen.getByText('Welcome to Orkyo'));
    await user.click(screen.getByText('Welcome to Orkyo'));

    // Small wait to ensure no async calls happen
    await new Promise((r) => setTimeout(r, 50));
    expect(mockMarkAnnouncementRead).not.toHaveBeenCalled();
  });

  it('should update unread count after marking announcement as read', async () => {
    mockGetActiveAnnouncements.mockResolvedValue({
      announcements: [unreadAnnouncement],
    });

    const user = userEvent.setup();
    render(<MessagesTab />, { wrapper: createWrapper() });

    await waitFor(() => {
      expect(screen.getByText('1 unread')).toBeInTheDocument();
    });

    // Expand the unread announcement
    await user.click(screen.getByText('Scheduled Maintenance'));

    await waitFor(() => {
      expect(screen.queryByText(/unread/)).not.toBeInTheDocument();
    });
  });

  // ========================================================================
  // Important announcements
  // ========================================================================

  it('should render important announcements', async () => {
    mockGetActiveAnnouncements.mockResolvedValue({
      announcements: [importantAnnouncement],
    });

    render(<MessagesTab />, { wrapper: createWrapper() });
    await waitFor(() => {
      expect(screen.getByText('Critical Security Update')).toBeInTheDocument();
    });
  });

  // ========================================================================
  // Error handling
  // ========================================================================

  it('should show error when API call fails', async () => {
    mockGetActiveAnnouncements.mockRejectedValue(new Error('Network error'));

    render(<MessagesTab />, { wrapper: createWrapper() });
    await waitFor(() => {
      expect(screen.getByText('Network error')).toBeInTheDocument();
    });
  });
});
