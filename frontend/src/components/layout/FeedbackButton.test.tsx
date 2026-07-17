import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { FeedbackButton } from './FeedbackButton';
import { submitFeedback } from '@foundation/src/lib/api/feedback-api';

vi.mock('@foundation/src/lib/api/feedback-api', () => ({
  submitFeedback: vi.fn(() => Promise.resolve()),
}));

function renderFeedbackButton() {
  return render(
    <MemoryRouter>
      <FeedbackButton />
    </MemoryRouter>,
  );
}

async function openDialog() {
  const user = userEvent.setup();
  renderFeedbackButton();
  await user.click(screen.getByRole('button', { name: /send feedback/i }));
  await waitFor(() => expect(screen.getByRole('dialog')).toBeInTheDocument());
  return user;
}

describe('FeedbackButton', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders the feedback trigger button', () => {
    renderFeedbackButton();
    expect(screen.getByRole('button', { name: /send feedback/i })).toBeInTheDocument();
  });

  it('opens dialog on click', async () => {
    await openDialog();
    expect(screen.getByRole('heading', { name: /send feedback/i })).toBeInTheDocument();
  });

  it('renders title input, description textarea, and type selector in dialog', async () => {
    await openDialog();
    expect(screen.getByLabelText(/title/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/description/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/type/i)).toBeInTheDocument();
  });

  it('shows validation error when submitting with empty title', async () => {
    const user = await openDialog();
    await user.click(screen.getByRole('button', { name: /^submit$/i }));
    expect(screen.getByText('Please enter a title')).toBeInTheDocument();
    expect(submitFeedback).not.toHaveBeenCalled();
  });

  it('submits with valid title and calls submitFeedback', async () => {
    const user = await openDialog();
    await user.type(screen.getByLabelText(/title/i), 'App crashes on login');
    await user.click(screen.getByRole('button', { name: /^submit$/i }));
    await waitFor(() =>
      expect(submitFeedback).toHaveBeenCalledWith(
        expect.objectContaining({ title: 'App crashes on login', feedbackType: 'bug' }),
      ),
    );
  });

  it('shows success state after successful submit', async () => {
    const user = await openDialog();
    await user.type(screen.getByLabelText(/title/i), 'Good idea');
    await user.click(screen.getByRole('button', { name: /^submit$/i }));
    await waitFor(() => expect(screen.getByText(/thank you/i)).toBeInTheDocument());
    expect(screen.queryByRole('button', { name: /^submit$/i })).not.toBeInTheDocument();
  });

  it('shows error message when API fails', async () => {
    vi.mocked(submitFeedback).mockRejectedValueOnce(new Error('Server unavailable'));
    const user = await openDialog();
    await user.type(screen.getByLabelText(/title/i), 'Something broken');
    await user.click(screen.getByRole('button', { name: /^submit$/i }));
    await waitFor(() => expect(screen.getByText('Server unavailable')).toBeInTheDocument());
  });

  it('stringifies a non-Error rejection via the shared errorMessage normalizer', async () => {
    vi.mocked(submitFeedback).mockRejectedValueOnce('oops');
    const user = await openDialog();
    await user.type(screen.getByLabelText(/title/i), 'Something');
    await user.click(screen.getByRole('button', { name: /^submit$/i }));
    await waitFor(() => expect(screen.getByText('oops')).toBeInTheDocument());
  });

  it('closes dialog when Cancel is clicked', async () => {
    const user = await openDialog();
    await user.click(screen.getByRole('button', { name: /cancel/i }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('shows description text matching the selected feedback type', async () => {
    await openDialog();
    // Default type is 'bug' — description should mention "working"
    expect(screen.getByText(/isn't working correctly/i)).toBeInTheDocument();
  });

  it('includes the current page URL in the submission payload', async () => {
    const user = await openDialog();
    await user.type(screen.getByLabelText(/title/i), 'Route test');
    await user.click(screen.getByRole('button', { name: /^submit$/i }));
    await waitFor(() =>
      expect(submitFeedback).toHaveBeenCalledWith(
        expect.objectContaining({ pageUrl: expect.any(String) }),
      ),
    );
  });

  it('omits description from payload when left empty', async () => {
    const user = await openDialog();
    await user.type(screen.getByLabelText(/title/i), 'Title only');
    await user.click(screen.getByRole('button', { name: /^submit$/i }));
    await waitFor(() =>
      expect(submitFeedback).toHaveBeenCalledWith(
        expect.objectContaining({ description: undefined }),
      ),
    );
  });

  it('includes description in payload when provided', async () => {
    const user = await openDialog();
    await user.type(screen.getByLabelText(/title/i), 'With desc');
    await user.type(screen.getByLabelText(/description/i), 'Detailed info');
    await user.click(screen.getByRole('button', { name: /^submit$/i }));
    await waitFor(() =>
      expect(submitFeedback).toHaveBeenCalledWith(
        expect.objectContaining({ description: 'Detailed info' }),
      ),
    );
  });
});
