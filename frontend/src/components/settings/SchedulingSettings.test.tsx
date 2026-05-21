/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { SchedulingSettings } from './SchedulingSettings';

const mockUseAppStore = vi.fn();
vi.mock('@foundation/src/store/app-store', () => ({
  useAppStore: (sel: any) => mockUseAppStore(sel),
}));

const mockSettings = {
  timeZone: 'Europe/Berlin',
  workingHoursEnabled: true,
  workingDayStart: '08:00',
  workingDayEnd: '18:00',
  weekendsEnabled: true,
  publicHolidaysEnabled: false,
  publicHolidayRegion: null,
};

const mockUseSchedulingSettings = vi.fn((_?: any): any => ({ data: mockSettings, isLoading: false }));
const mockUseAvailabilityEvents = vi.fn((_?: any): any => ({ data: [], isLoading: false }));
const mockUpsertMutateAsync = vi.fn();
const mockDeleteSettingsMutateAsync = vi.fn();
const mockCreateEventMutateAsync = vi.fn();
const mockUpdateEventMutateAsync = vi.fn();
const mockDeleteEventMutateAsync = vi.fn();

vi.mock('@foundation/src/hooks/useScheduling', () => ({
  useSchedulingSettings: (siteId: any) => mockUseSchedulingSettings(siteId),
  useUpsertSchedulingSettings: () => ({ mutateAsync: mockUpsertMutateAsync }),
  useDeleteSchedulingSettings: () => ({ mutateAsync: mockDeleteSettingsMutateAsync }),
  useAvailabilityEvents: (siteId: any) => mockUseAvailabilityEvents(siteId),
  useCreateAvailabilityEvent: () => ({ mutateAsync: mockCreateEventMutateAsync }),
  useUpdateAvailabilityEvent: () => ({ mutateAsync: mockUpdateEventMutateAsync }),
  useDeleteAvailabilityEvent: () => ({ mutateAsync: mockDeleteEventMutateAsync }),
}));

// AvailabilityEventDialog mock exposes onSave so handleSaveEvent can be tested
vi.mock('./AvailabilityEventDialog', () => ({
  AvailabilityEventDialog: ({ open, onSave }: any) =>
    open ? (
      <button
        data-testid="mock-event-save"
        onClick={() =>
          onSave({
            title: 'Test Availability Event',
            eventType: 'shutdown',
            defaultEffect: 'closed',
            startTs: '2026-12-24T00:00:00.000Z',
            endTs: '2026-12-26T00:00:00.000Z',
            enabled: true,
            isRecurring: false,
          })
        }
      >
        Save Availability Event
      </button>
    ) : null,
}));

const mockAvailabilityEvent = {
  id: 'event-1',
  siteId: 'site-1',
  title: 'Christmas Closure',
  description: null,
  eventType: 'public_holiday' as const,
  defaultEffect: 'closed' as const,
  startTs: '2024-12-24T00:00:00.000Z',
  endTs: '2024-12-26T00:00:00.000Z',
  enabled: true,
  isRecurring: false,
  recurrenceRule: null,
  scopes: [],
  createdAt: '2024-01-01T00:00:00.000Z',
  updatedAt: '2024-01-01T00:00:00.000Z',
};

function setup() {
  vi.clearAllMocks();
  mockUseAppStore.mockImplementation((selector: any) =>
    selector({ selectedSiteId: 'site-1' }),
  );
  mockUseSchedulingSettings.mockReturnValue({ data: mockSettings, isLoading: false });
  mockUseAvailabilityEvents.mockReturnValue({ data: [], isLoading: false });
  mockUpsertMutateAsync.mockResolvedValue(undefined);
  mockDeleteSettingsMutateAsync.mockResolvedValue(undefined);
  mockCreateEventMutateAsync.mockResolvedValue(undefined);
  mockUpdateEventMutateAsync.mockResolvedValue(undefined);
  mockDeleteEventMutateAsync.mockResolvedValue(undefined);
}

// ── Render tests ─────────────────────────────────────────────────────────────

describe('SchedulingSettings', () => {
  beforeEach(setup);

  it('renders the scheduling settings header', () => {
    render(<SchedulingSettings />);
    expect(screen.getByText('Scheduling')).toBeInTheDocument();
  });

  it('renders timezone section', () => {
    render(<SchedulingSettings />);
    expect(screen.getByText('Timezone')).toBeInTheDocument();
  });

  it('renders working hours section', () => {
    render(<SchedulingSettings />);
    expect(screen.getByText('Working Hours')).toBeInTheDocument();
  });

  it('renders weekends section', () => {
    render(<SchedulingSettings />);
    expect(screen.getByText('Weekends')).toBeInTheDocument();
  });

  it('renders availability events section with Add button', () => {
    render(<SchedulingSettings />);
    expect(screen.getByText('Availability Events')).toBeInTheDocument();
    expect(screen.getByText('Add')).toBeInTheDocument();
  });

  it('shows "Select a site" when no site selected', () => {
    mockUseAppStore.mockImplementation((selector: any) =>
      selector({ selectedSiteId: null }),
    );
    render(<SchedulingSettings />);
    expect(screen.getByText('Select a site to configure scheduling.')).toBeInTheDocument();
  });

  it('shows loading spinner when settings are loading', () => {
    mockUseSchedulingSettings.mockReturnValue({ data: null, isLoading: true });
    const { container } = render(<SchedulingSettings />);
    expect(container.querySelector('.animate-spin')).toBeInTheDocument();
    expect(screen.queryByText('Scheduling')).not.toBeInTheDocument();
  });

  it('shows loading spinner when availability events are loading', () => {
    mockUseAvailabilityEvents.mockReturnValue({ data: [], isLoading: true });
    const { container } = render(<SchedulingSettings />);
    expect(container.querySelector('.animate-spin')).toBeInTheDocument();
  });

  it('shows "No availability events configured" when list is empty', () => {
    render(<SchedulingSettings />);
    expect(screen.getByText('No availability events configured.')).toBeInTheDocument();
  });

  it('renders availability event items when present', () => {
    mockUseAvailabilityEvents.mockReturnValue({ data: [mockAvailabilityEvent], isLoading: false });
    render(<SchedulingSettings />);
    expect(screen.getByText('Christmas Closure')).toBeInTheDocument();
    expect(screen.getByText('public holiday')).toBeInTheDocument();
    expect(screen.getByText('closed')).toBeInTheDocument();
  });

  it('shows Recurring badge on recurring availability events', () => {
    mockUseAvailabilityEvents.mockReturnValue({
      data: [{ ...mockAvailabilityEvent, id: 'event-2', title: 'Weekly Maintenance', eventType: 'maintenance', isRecurring: true }],
      isLoading: false,
    });
    render(<SchedulingSettings />);
    expect(screen.getByText('Recurring')).toBeInTheDocument();
  });

  it('shows Disabled badge and scope count on scoped availability events', () => {
    mockUseAvailabilityEvents.mockReturnValue({
      data: [{
        ...mockAvailabilityEvent,
        id: 'event-3',
        title: 'Paused Event',
        eventType: 'custom',
        enabled: false,
        scopes: [
          { id: 'scope-1', availabilityEventId: 'event-3', targetType: 'resource', targetId: 's1', effect: 'available' },
          { id: 'scope-2', availabilityEventId: 'event-3', targetType: 'resource', targetId: 's2', effect: 'available' },
        ],
      }],
      isLoading: false,
    });
    render(<SchedulingSettings />);
    expect(screen.getByText('Disabled')).toBeInTheDocument();
    expect(screen.getByText(/2 override\(s\)/)).toBeInTheDocument();
  });

  it('renders public holidays section', () => {
    render(<SchedulingSettings />);
    expect(screen.getByText('Public Holidays')).toBeInTheDocument();
  });

  it('shows Reset to Defaults button', () => {
    render(<SchedulingSettings />);
    expect(screen.getByText('Reset to Defaults')).toBeInTheDocument();
  });
});

// ── Interaction tests — covers all handlers ───────────────────────────────────

describe('SchedulingSettings — interactions', () => {
  beforeEach(setup);

  it('toggling weekends switch changes form state (updateField)', async () => {
    const user = userEvent.setup();
    render(<SchedulingSettings />);

    const weekendsSwitch = screen.getByRole('switch', { name: /Exclude weekends/i });
    const before = weekendsSwitch.getAttribute('aria-checked');
    await user.click(weekendsSwitch);

    await waitFor(() => {
      expect(weekendsSwitch.getAttribute('aria-checked')).not.toBe(before);
    });
  });

  it('toggling weekends switch triggers debounced save', async () => {
    const user = userEvent.setup();
    render(<SchedulingSettings />);

    await user.click(screen.getByRole('switch', { name: /Exclude weekends/i }));

    await waitFor(
      () => expect(mockUpsertMutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({ weekendsEnabled: expect.any(Boolean) }),
      ),
      { timeout: 2000 },
    );
  });

  it('toggling working hours switch fires updateField', async () => {
    const user = userEvent.setup();
    render(<SchedulingSettings />);

    const sw = screen.getByRole('switch', { name: /Enable working hours/i });
    const before = sw.getAttribute('aria-checked');
    await user.click(sw);

    await waitFor(() => expect(sw.getAttribute('aria-checked')).not.toBe(before));
  });

  it('clicking Add opens the availability event dialog', async () => {
    const user = userEvent.setup();
    render(<SchedulingSettings />);

    await user.click(screen.getByRole('button', { name: /^Add$/i }));

    await waitFor(() => {
      expect(screen.getByTestId('mock-event-save')).toBeInTheDocument();
    });
  });

  it('saving from dialog creates an availability event', async () => {
    const user = userEvent.setup();
    render(<SchedulingSettings />);

    await user.click(screen.getByRole('button', { name: /^Add$/i }));
    await waitFor(() => screen.getByTestId('mock-event-save'));
    await user.click(screen.getByTestId('mock-event-save'));

    await waitFor(() => {
      expect(mockCreateEventMutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({ title: 'Test Availability Event', eventType: 'shutdown' }),
      );
    });
  });

  it('clicking edit on an availability event opens dialog', async () => {
    const user = userEvent.setup();
    mockUseAvailabilityEvents.mockReturnValue({ data: [mockAvailabilityEvent], isLoading: false });
    render(<SchedulingSettings />);

    const iconButtons = screen.getAllByRole('button').filter((b) => !b.textContent?.trim());
    await user.click(iconButtons[0]);

    await waitFor(() => {
      expect(screen.getByTestId('mock-event-save')).toBeInTheDocument();
    });
  });

  it('saving from edit dialog updates availability event', async () => {
    const user = userEvent.setup();
    mockUseAvailabilityEvents.mockReturnValue({ data: [mockAvailabilityEvent], isLoading: false });
    render(<SchedulingSettings />);

    const iconButtons = screen.getAllByRole('button').filter((b) => !b.textContent?.trim());
    await user.click(iconButtons[0]);
    await waitFor(() => screen.getByTestId('mock-event-save'));
    await user.click(screen.getByTestId('mock-event-save'));

    await waitFor(() => {
      expect(mockUpdateEventMutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({ eventId: mockAvailabilityEvent.id }),
      );
    });
  });

  it('deleting an availability event through confirm dialog fires handleDeleteEvent', async () => {
    const user = userEvent.setup();
    mockUseAvailabilityEvents.mockReturnValue({ data: [mockAvailabilityEvent], isLoading: false });
    render(<SchedulingSettings />);

    const iconButtons = screen.getAllByRole('button').filter((b) => !b.textContent?.trim());
    await user.click(iconButtons[1]);

    const deleteButton = await screen.findByRole('button', { name: /^Delete$/i });
    await user.click(deleteButton);

    await waitFor(() => {
      expect(mockDeleteEventMutateAsync).toHaveBeenCalledWith(mockAvailabilityEvent.id);
    });
  });

  it('Reset to Defaults opens confirm dialog and calls handleReset on confirm', async () => {
    const user = userEvent.setup();
    render(<SchedulingSettings />);

    await user.click(screen.getByRole('button', { name: /Reset to Defaults/i }));

    const confirmButton = await screen.findByRole('button', { name: /^Reset$/i });
    await user.click(confirmButton);

    await waitFor(() => {
      expect(mockDeleteSettingsMutateAsync).toHaveBeenCalled();
    });
  });

  it('save error from upsert shows error alert', async () => {
    mockUpsertMutateAsync.mockRejectedValue(new Error('Network error'));
    const user = userEvent.setup();
    render(<SchedulingSettings />);

    await user.click(screen.getByRole('switch', { name: /Exclude weekends/i }));

    await waitFor(
      () => expect(screen.getByText('Network error')).toBeInTheDocument(),
      { timeout: 2000 },
    );
  });
});
