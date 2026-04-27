/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AnnouncementsTab } from './AnnouncementsTab';

// Mock the announcement API
const mockGetAnnouncements = vi.fn();
const mockCreateAnnouncement = vi.fn();
const mockUpdateAnnouncement = vi.fn();
const mockDeleteAnnouncement = vi.fn();

vi.mock('@foundation/src/lib/api/announcement-api', () => ({
  getAnnouncements: (...args: unknown[]) => mockGetAnnouncements(...args),
  createAnnouncement: (...args: unknown[]) => mockCreateAnnouncement(...args),
  updateAnnouncement: (...args: unknown[]) => mockUpdateAnnouncement(...args),
  deleteAnnouncement: (...args: unknown[]) => mockDeleteAnnouncement(...args),
}));

const sampleAnnouncement = {
  id: '11111111-1111-1111-1111-111111111111',
  title: 'Scheduled Maintenance',
  body: 'The platform will be down for maintenance on Friday.',
  isImportant: false,
  revision: 1,
  createdAt: '2025-01-15T10:00:00Z',
  createdByEmail: 'admin@orkyo.io',
  updatedAt: '2025-01-15T10:00:00Z',
  updatedByEmail: 'admin@orkyo.io',
  expiresAt: '2099-04-15T10:00:00Z',
  isExpired: false,
};

const importantAnnouncement = {
  id: '22222222-2222-2222-2222-222222222222',
  title: 'Critical Security Update',
  body: 'Please update your passwords immediately.',
  isImportant: true,
  revision: 2,
  createdAt: '2025-01-10T08:00:00Z',
  createdByEmail: 'security@orkyo.io',
  updatedAt: '2025-01-12T14:30:00Z',
  updatedByEmail: 'security@orkyo.io',
  expiresAt: '2099-03-10T08:00:00Z',
  isExpired: false,
};

const expiredAnnouncement = {
  id: '33333333-3333-3333-3333-333333333333',
  title: 'Old News',
  body: 'This has expired.',
  isImportant: false,
  revision: 1,
  createdAt: '2024-06-01T10:00:00Z',
  createdByEmail: 'admin@orkyo.io',
  updatedAt: '2024-06-01T10:00:00Z',
  updatedByEmail: 'admin@orkyo.io',
  expiresAt: '2024-09-01T10:00:00Z',
  isExpired: true,
};

describe('AnnouncementsTab', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGetAnnouncements.mockResolvedValue({ announcements: [] });
  });

  // ========================================================================
  // Loading & Empty state
  // ========================================================================

  it('should show loading state initially', () => {
    // Never resolve the promise to keep loading
    mockGetAnnouncements.mockReturnValue(new Promise(() => {}));
    render(<AnnouncementsTab />);
    expect(screen.getByText('Loading announcements...')).toBeInTheDocument();
  });

  it('should show empty state when no announcements', async () => {
    render(<AnnouncementsTab />);
    await waitFor(() => {
      expect(screen.getByText(/no announcements yet/i)).toBeInTheDocument();
    });
  });

  it('should show the "New Announcement" button', async () => {
    render(<AnnouncementsTab />);
    await waitFor(() => {
      expect(screen.getByText('New Announcement')).toBeInTheDocument();
    });
  });

  it('should show card header with title and description', async () => {
    render(<AnnouncementsTab />);
    await waitFor(() => {
      expect(screen.getByText('Platform Announcements')).toBeInTheDocument();
      expect(screen.getByText(/announcements visible to all users/i)).toBeInTheDocument();
    });
  });

  // ========================================================================
  // List rendering
  // ========================================================================

  it('should render announcements in a table', async () => {
    mockGetAnnouncements.mockResolvedValue({
      announcements: [sampleAnnouncement, importantAnnouncement],
    });

    render(<AnnouncementsTab />);

    await waitFor(() => {
      expect(screen.getByText('Scheduled Maintenance')).toBeInTheDocument();
      expect(screen.getByText('Critical Security Update')).toBeInTheDocument();
    });
  });

  it('should show "Active" badge for non-important active announcements', async () => {
    mockGetAnnouncements.mockResolvedValue({
      announcements: [sampleAnnouncement],
    });

    render(<AnnouncementsTab />);

    await waitFor(() => {
      expect(screen.getByText('Active')).toBeInTheDocument();
    });
  });

  it('should show "Important" badge for important announcements', async () => {
    mockGetAnnouncements.mockResolvedValue({
      announcements: [importantAnnouncement],
    });

    render(<AnnouncementsTab />);

    await waitFor(() => {
      expect(screen.getByText('Important')).toBeInTheDocument();
    });
  });

  it('should show "Expired" badge for expired announcements', async () => {
    mockGetAnnouncements.mockResolvedValue({
      announcements: [expiredAnnouncement],
    });

    render(<AnnouncementsTab />);

    await waitFor(() => {
      expect(screen.getByText('Expired')).toBeInTheDocument();
    });
  });

  it('should show creator email', async () => {
    mockGetAnnouncements.mockResolvedValue({
      announcements: [sampleAnnouncement],
    });

    render(<AnnouncementsTab />);

    await waitFor(() => {
      expect(screen.getByText('admin@orkyo.io')).toBeInTheDocument();
    });
  });

  it('should show revision number', async () => {
    mockGetAnnouncements.mockResolvedValue({
      announcements: [importantAnnouncement],
    });

    render(<AnnouncementsTab />);

    await waitFor(() => {
      expect(screen.getByText('2')).toBeInTheDocument();
    });
  });

  // ========================================================================
  // Error handling
  // ========================================================================

  it('should display error when load fails', async () => {
    mockGetAnnouncements.mockRejectedValue(new Error('Network error'));

    render(<AnnouncementsTab />);

    await waitFor(() => {
      expect(screen.getByText('Network error')).toBeInTheDocument();
    });
  });

  // ========================================================================
  // Create dialog
  // ========================================================================

  it('should open create dialog when "New Announcement" clicked', async () => {
    const user = userEvent.setup();
    render(<AnnouncementsTab />);

    await waitFor(() => screen.getByText('New Announcement'));
    await user.click(screen.getByText('New Announcement'));

    expect(screen.getByText('New Announcement', { selector: '[role="dialog"] *' })).toBeInTheDocument();
    expect(screen.getByLabelText(/title/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/body/i)).toBeInTheDocument();
  });

  it('should call createAnnouncement with form values', async () => {
    mockCreateAnnouncement.mockResolvedValue(sampleAnnouncement);
    const user = userEvent.setup();

    render(<AnnouncementsTab />);
    await waitFor(() => screen.getByText('New Announcement'));

    // Open dialog
    await user.click(screen.getByText('New Announcement'));

    // Fill in form
    await user.type(screen.getByLabelText(/title/i), 'Test Title');
    await user.type(screen.getByLabelText(/body/i), 'Test Body');

    // Submit
    await user.click(screen.getByRole('button', { name: /^create$/i }));

    await waitFor(() => {
      expect(mockCreateAnnouncement).toHaveBeenCalledWith(
        expect.objectContaining({
          title: 'Test Title',
          body: 'Test Body',
          isImportant: false,
          retentionDays: 90,
        })
      );
    });
  });

  // ========================================================================
  // Edit dialog
  // ========================================================================

  it('should open edit dialog when edit button clicked', async () => {
    mockGetAnnouncements.mockResolvedValue({
      announcements: [sampleAnnouncement],
    });
    const user = userEvent.setup();

    render(<AnnouncementsTab />);
    await waitFor(() => screen.getByText('Scheduled Maintenance'));

    // Click edit button (pencil icon)
    const editButtons = screen.getAllByRole('button').filter(
      (btn) => btn.querySelector('svg.lucide-pencil')
    );
    expect(editButtons.length).toBeGreaterThan(0);
    await user.click(editButtons[0]);

    // Check dialog opened with pre-filled values
    expect(screen.getByText('Edit Announcement')).toBeInTheDocument();
    expect(screen.getByLabelText(/title/i)).toHaveValue('Scheduled Maintenance');
  });

  // ========================================================================
  // Delete confirmation
  // ========================================================================

  it('should open delete confirmation when delete button clicked', async () => {
    mockGetAnnouncements.mockResolvedValue({
      announcements: [sampleAnnouncement],
    });
    const user = userEvent.setup();

    render(<AnnouncementsTab />);
    await waitFor(() => screen.getByText('Scheduled Maintenance'));

    // Each row has 2 action buttons: edit (pencil) and delete (trash).
    // The delete button has the destructive class.
    const actionButtons = screen.getAllByRole('button').filter(
      (btn) => btn.classList.contains('text-destructive')
    );
    expect(actionButtons.length).toBeGreaterThan(0);
    await user.click(actionButtons[0]);

    // Check confirmation dialog
    expect(screen.getByText('Delete Announcement')).toBeInTheDocument();
    expect(screen.getAllByText(/scheduled maintenance/i).length).toBeGreaterThanOrEqual(2);
  });

  it('should call deleteAnnouncement when confirmed', async () => {
    mockGetAnnouncements.mockResolvedValue({
      announcements: [sampleAnnouncement],
    });
    mockDeleteAnnouncement.mockResolvedValue(undefined);
    const user = userEvent.setup();

    render(<AnnouncementsTab />);
    await waitFor(() => screen.getByText('Scheduled Maintenance'));

    // Click delete
    const actionButtons = screen.getAllByRole('button').filter(
      (btn) => btn.classList.contains('text-destructive')
    );
    await user.click(actionButtons[0]);

    // Confirm deletion — AlertDialogAction renders as a button
    const confirmDeleteBtn = screen.getAllByRole('button').find(
      (btn) => btn.textContent === 'Delete'
    );
    expect(confirmDeleteBtn).toBeDefined();
    await user.click(confirmDeleteBtn!);

    await waitFor(() => {
      expect(mockDeleteAnnouncement).toHaveBeenCalledWith(sampleAnnouncement.id);
    });
  });

  // ========================================================================
  // Fetches with includeExpired=true by default
  // ========================================================================

  it('should call getAnnouncements with includeExpired=true', async () => {
    render(<AnnouncementsTab />);

    await waitFor(() => {
      expect(mockGetAnnouncements).toHaveBeenCalledWith(true);
    });
  });
});
