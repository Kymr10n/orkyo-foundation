import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { NotificationPreferencesSection } from './NotificationPreferencesSection';
import {
  getNotificationPreferences,
  updateNotificationPreferences,
} from '@foundation/src/lib/api/security-api';
import { createFeedbackMutationCache } from '@foundation/src/lib/core/query-client';
import { toast } from 'sonner';

vi.mock('@foundation/src/lib/api/security-api', () => ({
  getNotificationPreferences: vi.fn(),
  updateNotificationPreferences: vi.fn(),
}));

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

// The success/error toast + cache invalidation now originate from the central
// feedback MutationCache (meta), not the component — so wire the real cache here
// (with the mocked toast) exactly as production does. See docs/dialog-feedback.md.
function renderNotif(props: { locked?: boolean } = {}) {
  const queryClient: QueryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    mutationCache: createFeedbackMutationCache(() => queryClient, toast),
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <NotificationPreferencesSection {...props} />
    </QueryClientProvider>,
  );
}

describe('NotificationPreferencesSection', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getNotificationPreferences).mockResolvedValue({ announcementEmailOptOut: false });
    vi.mocked(updateNotificationPreferences).mockResolvedValue({ message: 'ok' });
  });

  it('shows a loading spinner while fetching', () => {
    vi.mocked(getNotificationPreferences).mockReturnValue(new Promise(() => {}));
    const { container } = renderNotif();
    expect(container.querySelector('.animate-spin')).toBeTruthy();
  });

  it('renders the switch on when the user is opted in', async () => {
    vi.mocked(getNotificationPreferences).mockResolvedValue({ announcementEmailOptOut: false });
    renderNotif();
    expect(await screen.findByRole('switch')).toHaveAttribute('aria-checked', 'true');
  });

  it('renders the switch off when the user is opted out', async () => {
    vi.mocked(getNotificationPreferences).mockResolvedValue({ announcementEmailOptOut: true });
    renderNotif();
    expect(await screen.findByRole('switch')).toHaveAttribute('aria-checked', 'false');
  });

  it('opting out calls updateNotificationPreferences(true) and toasts success', async () => {
    vi.mocked(getNotificationPreferences).mockResolvedValue({ announcementEmailOptOut: false });
    renderNotif();

    // Switch is on (opted in) → toggling sends checked=false → opt-out=true.
    fireEvent.click(await screen.findByRole('switch'));

    await waitFor(() => expect(updateNotificationPreferences).toHaveBeenCalledWith(true));
    await waitFor(() => expect(toast.success).toHaveBeenCalled());
  });

  it('toasts an error when the update fails', async () => {
    vi.mocked(getNotificationPreferences).mockResolvedValue({ announcementEmailOptOut: false });
    vi.mocked(updateNotificationPreferences).mockRejectedValueOnce(new Error('boom'));
    renderNotif();

    fireEvent.click(await screen.findByRole('switch'));

    await waitFor(() => expect(toast.error).toHaveBeenCalled());
  });

  it('disables the switch for a locked account', async () => {
    vi.mocked(getNotificationPreferences).mockResolvedValue({ announcementEmailOptOut: false });
    renderNotif({ locked: true });
    expect(await screen.findByRole('switch')).toBeDisabled();
  });
});
