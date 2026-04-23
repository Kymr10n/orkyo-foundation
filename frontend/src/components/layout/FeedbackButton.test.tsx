import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { FeedbackButton } from './FeedbackButton';

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

describe('FeedbackButton', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders the feedback trigger button', () => {
    renderFeedbackButton();
    expect(screen.getByRole('button', { name: /feedback/i })).toBeInTheDocument();
  });

  it('opens dialog on click', async () => {
    renderFeedbackButton();
    fireEvent.click(screen.getByRole('button', { name: /feedback/i }));
    await waitFor(() => {
      expect(screen.getByText(/send.*feedback/i)).toBeInTheDocument();
    });
  });
});
