/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
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

vi.mock('@foundation/src/hooks/useScheduling', () => ({
  useSchedulingSettings: (siteId: any) => mockUseSchedulingSettings(siteId),
  useUpsertSchedulingSettings: () => ({ mutateAsync: vi.fn() }),
  useDeleteSchedulingSettings: () => ({ mutateAsync: vi.fn() }),
  useOffTimes: (siteId: any) => mockUseOffTimes(siteId),
  useCreateOffTime: () => ({ mutateAsync: vi.fn() }),
  useUpdateOffTime: () => ({ mutateAsync: vi.fn() }),
  useDeleteOffTime: () => ({ mutateAsync: vi.fn() }),
}));

vi.mock('@foundation/src/domain/scheduling/types', () => ({
  OFF_TIME_TYPE_LABELS: { maintenance: 'Maintenance', holiday: 'Holiday', custom: 'Custom' },
}));

vi.mock('./OffTimeDialog', () => ({
  OffTimeDialog: () => null,
}));

describe('SchedulingSettings', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseAppStore.mockImplementation((selector: any) =>
      selector({ selectedSiteId: 'site-1' }),
    );
    mockUseSchedulingSettings.mockReturnValue({ data: mockSettings, isLoading: false });
    mockUseOffTimes.mockReturnValue({ data: [], isLoading: false });
  });

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
    // Loader2 renders an svg with animate-spin class
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
    mockUseOffTimes.mockReturnValue({
      data: [
        {
          id: 'ot-1',
          siteId: 'site-1',
          title: 'Christmas Closure',
          type: 'holiday',
          startMs: new Date('2024-12-24').getTime(),
          endMs: new Date('2024-12-26').getTime(),
          enabled: true,
          isRecurring: false,
          appliesToAllSpaces: true,
          spaceIds: [],
        },
      ],
      isLoading: false,
    });
    render(<SchedulingSettings />);
    expect(screen.getByText('Christmas Closure')).toBeInTheDocument();
    expect(screen.getByText('Holiday')).toBeInTheDocument();
    expect(screen.getByText(/All spaces/)).toBeInTheDocument();
  });

  it('shows Recurring badge on recurring off-times', () => {
    mockUseOffTimes.mockReturnValue({
      data: [
        {
          id: 'ot-2',
          siteId: 'site-1',
          title: 'Weekly Maintenance',
          type: 'maintenance',
          startMs: Date.now(),
          endMs: Date.now() + 3600000,
          enabled: true,
          isRecurring: true,
          appliesToAllSpaces: true,
          spaceIds: [],
        },
      ],
      isLoading: false,
    });
    render(<SchedulingSettings />);
    expect(screen.getByText('Recurring')).toBeInTheDocument();
  });

  it('shows Disabled badge on disabled off-times', () => {
    mockUseOffTimes.mockReturnValue({
      data: [
        {
          id: 'ot-3',
          siteId: 'site-1',
          title: 'Paused Event',
          type: 'custom',
          startMs: Date.now(),
          endMs: Date.now() + 3600000,
          enabled: false,
          isRecurring: false,
          appliesToAllSpaces: false,
          spaceIds: ['s1', 's2'],
        },
      ],
      isLoading: false,
    });
    render(<SchedulingSettings />);
    expect(screen.getByText('Disabled')).toBeInTheDocument();
    expect(screen.getByText('Custom')).toBeInTheDocument();
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
