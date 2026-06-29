import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FeedbackTab } from './FeedbackTab';

const mockGet = vi.fn();
const mockGetOne = vi.fn();
const mockUpdate = vi.fn();

vi.mock('@foundation/src/lib/api/feedback-admin-api', () => ({
  getFeedback: (...args: unknown[]) => mockGet(...args),
  getFeedbackItem: (...args: unknown[]) => mockGetOne(...args),
  updateFeedback: (...args: unknown[]) => mockUpdate(...args),
}));

const mockToast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }));
vi.mock('sonner', () => ({ toast: mockToast }));

const summary = {
  id: 'fb-1',
  feedbackType: 'bug',
  title: 'Grid does not load',
  status: 'new',
  tenantName: 'Acme',
  submitterEmail: 'user@acme.com',
  createdAt: '2026-01-10T10:00:00Z',
};

const detail = {
  ...summary,
  description: 'It just spins forever.',
  pageUrl: '/spaces',
  userAgent: 'Mozilla/5.0',
  adminNotes: null,
  githubIssueUrl: null,
  updatedAt: '2026-01-10T10:00:00Z',
};

describe('FeedbackTab', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGet.mockResolvedValue({ items: [], total: 0 });
  });

  it('shows loading then the empty message', async () => {
    render(<FeedbackTab />);
    expect(screen.getByText(/Loading feedback/i)).toBeInTheDocument();
    await waitFor(() => expect(screen.getByText(/No feedback yet/i)).toBeInTheDocument());
  });

  it('renders feedback rows', async () => {
    mockGet.mockResolvedValue({ items: [summary], total: 1 });
    render(<FeedbackTab />);
    await waitFor(() => expect(screen.getByText('Grid does not load')).toBeInTheDocument());
    expect(screen.getByText(/user@acme.com/)).toBeInTheDocument();
  });

  it('opens the detail dialog and loads the full item', async () => {
    mockGet.mockResolvedValue({ items: [summary], total: 1 });
    mockGetOne.mockResolvedValue(detail);
    const user = userEvent.setup();
    render(<FeedbackTab />);

    await waitFor(() => screen.getByText('Grid does not load'));
    await user.click(screen.getByRole('button', { name: /Review Grid does not load/i }));

    await waitFor(() => expect(mockGetOne).toHaveBeenCalledWith('fb-1'));
    expect(await screen.findByText(/It just spins forever/i)).toBeInTheDocument();
  });

  it('saves notes/github changes via updateFeedback', async () => {
    mockGet.mockResolvedValue({ items: [summary], total: 1 });
    mockGetOne.mockResolvedValue(detail);
    mockUpdate.mockResolvedValue({ ...detail, adminNotes: 'On it.' });
    const user = userEvent.setup();
    render(<FeedbackTab />);

    await waitFor(() => screen.getByText('Grid does not load'));
    await user.click(screen.getByRole('button', { name: /Review Grid does not load/i }));
    await screen.findByText(/It just spins forever/i);

    fireEvent.change(screen.getByLabelText(/Admin notes/i), { target: { value: 'On it.' } });
    await user.click(screen.getByRole('button', { name: /^save$/i }));

    await waitFor(() => expect(mockUpdate).toHaveBeenCalledWith(
      'fb-1',
      expect.objectContaining({ adminNotes: 'On it.' }),
    ));
    expect(mockToast.success).toHaveBeenCalled();
  });
});
