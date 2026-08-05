import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { CalendarFeedDialog } from './CalendarFeedDialog';
import {
  createCalendarSubscription,
  getCalendarSubscriptions,
  revokeCalendarSubscription,
} from '@foundation/src/lib/api/calendar-feed-api';

vi.mock('@foundation/src/lib/api/calendar-feed-api', () => ({
  getCalendarSubscriptions: vi.fn(),
  createCalendarSubscription: vi.fn(),
  revokeCalendarSubscription: vi.fn(),
}));

vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

let mockSelectedSiteId: string | null = 'site-1';
vi.mock('@foundation/src/store/app-store', () => ({
  useAppStore: <T,>(selector: (state: { selectedSiteId: string | null }) => T) =>
    selector({ selectedSiteId: mockSelectedSiteId }),
}));

function renderDialog() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <CalendarFeedDialog
        open
        onOpenChange={vi.fn()}
        label="Utilization schedule"
        description="Add this schedule to Outlook, Google Calendar or Apple Calendar."
      />
    </QueryClientProvider>,
  );
}

let writeText: ReturnType<typeof vi.fn>;

const subscription = {
  id: 'sub-1',
  userId: 'user-1',
  label: 'Outlook, laptop',
  siteId: 'site-1',
  createdAt: '2026-08-01T10:00:00Z',
  lastUsedAt: null,
};

beforeEach(() => {
  vi.clearAllMocks();
  mockSelectedSiteId = 'site-1';
  vi.mocked(getCalendarSubscriptions).mockResolvedValue([]);
  // jsdom exposes navigator.clipboard as getter-only.
  writeText = vi.fn().mockResolvedValue(undefined);
  Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
});

describe('CalendarFeedDialog', () => {
  it('tells the user there are none yet', async () => {
    renderDialog();
    expect(await screen.findByText(/no calendar subscriptions yet/i)).toBeInTheDocument();
  });

  it('lists existing subscriptions with their usage', async () => {
    vi.mocked(getCalendarSubscriptions).mockResolvedValue([subscription]);
    renderDialog();

    expect(await screen.findByText('Outlook, laptop')).toBeInTheDocument();
    // "never used" is the signal that a subscription can be safely revoked.
    expect(screen.getByText(/never used/i)).toBeInTheDocument();
  });

  it('hides subscriptions belonging to another site', async () => {
    vi.mocked(getCalendarSubscriptions).mockResolvedValue([
      { ...subscription, siteId: 'site-2' },
    ]);
    renderDialog();

    // The endpoint returns every site's subscriptions; this dialog speaks for one.
    expect(await screen.findByText(/no calendar subscriptions yet/i)).toBeInTheDocument();
    expect(screen.queryByText('Outlook, laptop')).not.toBeInTheDocument();
  });

  it('scopes a new subscription to the selected site', async () => {
    vi.mocked(createCalendarSubscription).mockResolvedValue({
      id: 'sub-2',
      feedUrl: 'https://acme.orkyo.com/api/calendar/feed/tok.ics',
      label: null,
      siteId: 'site-1',
    });
    renderDialog();

    await userEvent.click(await screen.findByRole('button', { name: /create feed/i }));

    await waitFor(() =>
      expect(createCalendarSubscription).toHaveBeenCalledWith({ label: undefined, siteId: 'site-1' }),
    );
  });

  it('shows the feed URL once after creating, with subscribe instructions', async () => {
    vi.mocked(createCalendarSubscription).mockResolvedValue({
      id: 'sub-2',
      feedUrl: 'https://acme.orkyo.com/api/calendar/feed/tok.ics',
      label: 'Outlook, laptop',
      siteId: 'site-1',
    });
    renderDialog();

    await userEvent.type(await screen.findByLabelText(/name this subscription/i), 'Outlook, laptop');
    await userEvent.click(screen.getByRole('button', { name: /create feed/i }));

    const urlField = await screen.findByLabelText(/calendar feed address/i);
    expect(urlField).toHaveValue('https://acme.orkyo.com/api/calendar/feed/tok.ics');
    // The URL is unrecoverable afterwards, so the warning has to be present.
    expect(screen.getByText(/shown only once/i)).toBeInTheDocument();
    expect(screen.getByText(/subscribe from web/i)).toBeInTheDocument();
  });

  it('copies the feed URL to the clipboard', async () => {
    vi.mocked(createCalendarSubscription).mockResolvedValue({
      id: 'sub-2',
      feedUrl: 'https://acme.orkyo.com/api/calendar/feed/tok.ics',
      label: null,
      siteId: 'site-1',
    });
    renderDialog();

    await userEvent.click(await screen.findByRole('button', { name: /create feed/i }));
    await userEvent.click(await screen.findByRole('button', { name: /copy/i }));

    expect(writeText).toHaveBeenCalledWith('https://acme.orkyo.com/api/calendar/feed/tok.ics');
  });

  it('revokes only after the user confirms', async () => {
    vi.mocked(getCalendarSubscriptions).mockResolvedValue([subscription]);
    vi.mocked(revokeCalendarSubscription).mockResolvedValue(undefined);
    renderDialog();

    await userEvent.click(await screen.findByRole('button', { name: /revoke outlook, laptop/i }));
    expect(revokeCalendarSubscription).not.toHaveBeenCalled();

    await userEvent.click(await screen.findByRole('button', { name: /^revoke$/i }));
    await waitFor(() => expect(revokeCalendarSubscription).toHaveBeenCalledWith('sub-1'));
  });
});
