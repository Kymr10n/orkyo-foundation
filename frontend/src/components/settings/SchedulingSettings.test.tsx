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
const mockUseOffTimes = vi.fn((_?: any): any => ({ data: [], isLoading: false }));
const mockUpsertMutateAsync = vi.fn();
const mockDeleteSettingsMutateAsync = vi.fn();
const mockCreateOffTimeMutateAsync = vi.fn();
const mockUpdateOffTimeMutateAsync = vi.fn();
const mockDeleteOffTimeMutateAsync = vi.fn();

vi.mock('@foundation/src/hooks/useScheduling', () => ({
  useSchedulingSettings: (siteId: any) => mockUseSchedulingSettings(siteId),
  useUpsertSchedulingSettings: () => ({ mutateAsync: mockUpsertMutateAsync }),
  useDeleteSchedulingSettings: () => ({ mutateAsync: mockDeleteSettingsMutateAsync }),
  useOffTimes: (siteId: any) => mockUseOffTimes(siteId),
  useCreateOffTime: () => ({ mutateAsync: mockCreateOffTimeMutateAsync }),
  useUpdateOffTime: () => ({ mutateAsync: mockUpdateOffTimeMutateAsync }),
  useDeleteOffTime: () => ({ mutateAsync: mockDeleteOffTimeMutateAsync }),
}));

vi.mock('@foundation/src/domain/scheduling/types', () => ({
  OFF_TIME_TYPE_LABELS: { maintenance: 'Maintenance', holiday: 'Holiday', custom: 'Custom' },
}));

// OffTimeDialog mock exposes onSave so handleSaveOffTime can be tested
vi.mock('./OffTimeDialog', () => ({
  OffTimeDialog: ({ open, onSave }: any) =>
    open ? (
      <button
        data-testid="mock-offtime-save"
        onClick={() =>
          onSave({
            title: 'Test Off Time',
            type: 'holiday',
            startMs: 1000,
            endMs: 2000,
            enabled: true,
            isRecurring: false,
            appliesToAllSpaces: true,
            spaceIds: [],
          })
        }
      >
        Save Off Time
      </button>
    ) : null,
}));

const mockOffTime = {
  id: 'ot-1',
  siteId: 'site-1',
  title: 'Christmas Closure',
  type: 'holiday' as const,
  startMs: new Date('2024-12-24').getTime(),
  endMs: new Date('2024-12-26').getTime(),
  enabled: true,
  isRecurring: false,
  appliesToAllSpaces: true,
  spaceIds: [],
};

function setup() {
  vi.clearAllMocks();
  mockUseAppStore.mockImplementation((selector: any) =>
    selector({ selectedSiteId: 'site-1' }),
  );
  mockUseSchedulingSettings.mockReturnValue({ data: mockSettings, isLoading: false });
  mockUseOffTimes.mockReturnValue({ data: [], isLoading: false });
  mockUpsertMutateAsync.mockResolvedValue(undefined);
  mockDeleteSettingsMutateAsync.mockResolvedValue(undefined);
  mockCreateOffTimeMutateAsync.mockResolvedValue(undefined);
  mockUpdateOffTimeMutateAsync.mockResolvedValue(undefined);
  mockDeleteOffTimeMutateAsync.mockResolvedValue(undefined);
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

  it('renders off-times section with Add button', () => {
    render(<SchedulingSettings />);
    expect(screen.getByText('Off-Times')).toBeInTheDocument();
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

  it('shows loading spinner when off-times are loading', () => {
    mockUseOffTimes.mockReturnValue({ data: [], isLoading: true });
    const { container } = render(<SchedulingSettings />);
    expect(container.querySelector('.animate-spin')).toBeInTheDocument();
  });

  it('shows "No off-times configured" when list is empty', () => {
    render(<SchedulingSettings />);
    expect(screen.getByText('No off-times configured.')).toBeInTheDocument();
  });

  it('renders off-time items when present', () => {
    mockUseOffTimes.mockReturnValue({ data: [mockOffTime], isLoading: false });
    render(<SchedulingSettings />);
    expect(screen.getByText('Christmas Closure')).toBeInTheDocument();
    expect(screen.getByText('Holiday')).toBeInTheDocument();
    expect(screen.getByText(/All spaces/)).toBeInTheDocument();
  });

  it('shows Recurring badge on recurring off-times', () => {
    mockUseOffTimes.mockReturnValue({
      data: [{ ...mockOffTime, id: 'ot-2', title: 'Weekly Maintenance', type: 'maintenance', isRecurring: true }],
      isLoading: false,
    });
    render(<SchedulingSettings />);
    expect(screen.getByText('Recurring')).toBeInTheDocument();
  });

  it('shows Disabled badge on disabled off-times', () => {
    mockUseOffTimes.mockReturnValue({
      data: [{ ...mockOffTime, id: 'ot-3', title: 'Paused Event', type: 'custom', enabled: false, appliesToAllSpaces: false, spaceIds: ['s1', 's2'] }],
      isLoading: false,
    });
    render(<SchedulingSettings />);
    expect(screen.getByText('Disabled')).toBeInTheDocument();
    expect(screen.getByText(/2 space\(s\)/)).toBeInTheDocument();
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

  it('clicking Add fires handleCreateOffTime and opens dialog', async () => {
    const user = userEvent.setup();
    render(<SchedulingSettings />);

    await user.click(screen.getByRole('button', { name: /^Add$/i }));

    await waitFor(() => {
      expect(screen.getByTestId('mock-offtime-save')).toBeInTheDocument();
    });
  });

  it('saving from dialog calls handleSaveOffTime → createOffTime', async () => {
    const user = userEvent.setup();
    render(<SchedulingSettings />);

    await user.click(screen.getByRole('button', { name: /^Add$/i }));
    await waitFor(() => screen.getByTestId('mock-offtime-save'));
    await user.click(screen.getByTestId('mock-offtime-save'));

    await waitFor(() => {
      expect(mockCreateOffTimeMutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({ title: 'Test Off Time', type: 'holiday' }),
      );
    });
  });

  it('clicking edit on an off-time fires handleEditOffTime', async () => {
    const user = userEvent.setup();
    mockUseOffTimes.mockReturnValue({ data: [mockOffTime], isLoading: false });
    render(<SchedulingSettings />);

    const iconButtons = screen.getAllByRole('button').filter((b) => !b.textContent?.trim());
    await user.click(iconButtons[0]);

    await waitFor(() => {
      expect(screen.getByTestId('mock-offtime-save')).toBeInTheDocument();
    });
  });

  it('saving from edit dialog calls handleSaveOffTime → updateOffTime', async () => {
    const user = userEvent.setup();
    mockUseOffTimes.mockReturnValue({ data: [mockOffTime], isLoading: false });
    render(<SchedulingSettings />);

    const iconButtons = screen.getAllByRole('button').filter((b) => !b.textContent?.trim());
    await user.click(iconButtons[0]);
    await waitFor(() => screen.getByTestId('mock-offtime-save'));
    await user.click(screen.getByTestId('mock-offtime-save'));

    await waitFor(() => {
      expect(mockUpdateOffTimeMutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({ offTimeId: mockOffTime.id }),
      );
    });
  });

  it('deleting an off-time through confirm dialog fires handleDeleteOffTime', async () => {
    const user = userEvent.setup();
    mockUseOffTimes.mockReturnValue({ data: [mockOffTime], isLoading: false });
    render(<SchedulingSettings />);

    const iconButtons = screen.getAllByRole('button').filter((b) => !b.textContent?.trim());
    await user.click(iconButtons[1]);

    const deleteButton = await screen.findByRole('button', { name: /^Delete$/i });
    await user.click(deleteButton);

    await waitFor(() => {
      expect(mockDeleteOffTimeMutateAsync).toHaveBeenCalledWith(mockOffTime.id);
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
