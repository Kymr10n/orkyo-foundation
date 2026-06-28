import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { NotificationPreferencesSection } from './NotificationPreferencesSection';
import { updateNotificationPreferences } from '@foundation/src/lib/api/security-api';
import { toast } from 'sonner';

const mockQueryResult = vi.hoisted(() => ({
  current: { data: null as unknown, isLoading: false },
}));

// useMutation mock runs the real mutationFn then onSuccess/onError so we can assert side effects.
vi.mock('@tanstack/react-query', () => ({
  useQuery: () => mockQueryResult.current,
  useQueryClient: () => ({ invalidateQueries: vi.fn() }),
  useMutation: (opts: {
    mutationFn: (v: boolean) => Promise<unknown>;
    onSuccess?: () => void;
    onError?: () => void;
  }) => ({
    mutate: async (v: boolean) => {
      try {
        await opts.mutationFn(v);
        opts.onSuccess?.();
      } catch {
        opts.onError?.();
      }
    },
    isPending: false,
  }),
}));

vi.mock('@foundation/src/lib/api/security-api', () => ({
  getNotificationPreferences: vi.fn(),
  updateNotificationPreferences: vi.fn(),
}));

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

describe('NotificationPreferencesSection', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(updateNotificationPreferences).mockResolvedValue({ message: 'ok' });
  });

  it('shows a loading spinner while fetching', () => {
    mockQueryResult.current = { data: null, isLoading: true };
    const { container } = render(<NotificationPreferencesSection />);
    expect(container.querySelector('.animate-spin')).toBeTruthy();
  });

  it('renders the switch on when the user is opted in', () => {
    mockQueryResult.current = { data: { announcementEmailOptOut: false }, isLoading: false };
    render(<NotificationPreferencesSection />);
    expect(screen.getByRole('switch')).toHaveAttribute('aria-checked', 'true');
  });

  it('renders the switch off when the user is opted out', () => {
    mockQueryResult.current = { data: { announcementEmailOptOut: true }, isLoading: false };
    render(<NotificationPreferencesSection />);
    expect(screen.getByRole('switch')).toHaveAttribute('aria-checked', 'false');
  });

  it('opting out calls updateNotificationPreferences(true) and toasts success', async () => {
    mockQueryResult.current = { data: { announcementEmailOptOut: false }, isLoading: false };
    render(<NotificationPreferencesSection />);

    // Switch is on (opted in) → toggling sends checked=false → opt-out=true.
    fireEvent.click(screen.getByRole('switch'));

    await waitFor(() => expect(updateNotificationPreferences).toHaveBeenCalledWith(true));
    expect(toast.success).toHaveBeenCalled();
  });

  it('toasts an error when the update fails', async () => {
    mockQueryResult.current = { data: { announcementEmailOptOut: false }, isLoading: false };
    vi.mocked(updateNotificationPreferences).mockRejectedValueOnce(new Error('boom'));
    render(<NotificationPreferencesSection />);

    fireEvent.click(screen.getByRole('switch'));

    await waitFor(() => expect(toast.error).toHaveBeenCalled());
  });

  it('disables the switch for a locked account', () => {
    mockQueryResult.current = { data: { announcementEmailOptOut: false }, isLoading: false };
    render(<NotificationPreferencesSection locked />);
    expect(screen.getByRole('switch')).toBeDisabled();
  });
});
