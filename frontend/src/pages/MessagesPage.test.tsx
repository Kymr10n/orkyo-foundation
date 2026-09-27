/** @jsxImportSource react */
import { describe, it, expect, vi } from 'vitest';
import { screen, waitFor, fireEvent } from '@testing-library/react';
import { MessagesPage } from './MessagesPage';
import { renderWithQuery } from '@foundation/src/test-utils';

// Mock navigate
const mockNavigate = vi.fn();
vi.mock('react-router', async () => {
  const actual = await vi.importActual('react-router');
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

describe('MessagesPage', () => {
  it('renders page heading and description', async () => {
    renderWithQuery(<MessagesPage />, { router: true });

    await waitFor(() => {
      expect(screen.getByText('Messages')).toBeInTheDocument();
    });
    expect(screen.getByText('Stay up to date with platform news')).toBeInTheDocument();
  });

  it('renders Back button', async () => {
    renderWithQuery(<MessagesPage />, { router: true });

    await waitFor(() => {
      expect(screen.getByText('Back')).toBeInTheDocument();
    });
  });

  it('renders the MessagesTab component', async () => {
    renderWithQuery(<MessagesPage />, { router: true });

    // MessagesTab shows "No messages at this time." when empty
    await waitFor(() => {
      expect(screen.getByText('No messages at this time.')).toBeInTheDocument();
    });
  });

  it('clicking Back calls navigate(-1)', async () => {
    renderWithQuery(<MessagesPage />, { router: true });
    await waitFor(() => expect(screen.getByText('Back')).toBeInTheDocument());
    fireEvent.click(screen.getByText('Back'));
    expect(mockNavigate).toHaveBeenCalledWith(-1);
  });
});
