import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { RequestScheduleSection } from './RequestScheduleSection';
import type { Space } from '@foundation/src/types/space';
import type { RequirementEntry } from '@foundation/src/hooks/useRequestForm';
import type { ReactNode } from 'react';

vi.mock('@foundation/src/components/ui/select', () => ({
  Select: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  SelectTrigger: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  SelectValue: ({ placeholder }: { placeholder: string }) => <span>{placeholder}</span>,
  SelectContent: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  SelectItem: ({ children }: { children: ReactNode; value: string }) => <div>{children}</div>,
}));

vi.mock('@foundation/src/components/ui/label', () => ({
  Label: ({ children }: { children: ReactNode }) => <label>{children}</label>,
}));

vi.mock('@foundation/src/components/ui/date-time-picker', () => ({
  DateTimePicker: ({
    placeholder,
    id,
    onChange,
  }: {
    placeholder: string;
    id: string;
    value?: string;
    onChange?: (v: string) => void;
  }) => (
    <input
      data-testid={id}
      placeholder={placeholder}
      onChange={(e) => onChange?.(e.target.value)}
    />
  ),
}));

vi.mock('@foundation/src/constants', () => ({
  SPACE_NONE_PLACEHOLDER: '__none__',
}));

const mockSplitDateTimeFields = vi.fn();
vi.mock('@foundation/src/lib/utils/picker-utils', () => ({
  combineDateTimeFields: (d: string, t: string) => d && t ? `${d}T${t}` : '',
  splitDateTimeFields: (...args: unknown[]) => mockSplitDateTimeFields(...args),
}));

const mockSpaces: Space[] = [
  { id: 's1', name: 'Room A', siteId: 'site1', isPhysical: true, capacity: 10, createdAt: '', updatedAt: '' },
];

const baseState = {
  name: '',
  description: '',
  icon: null,
  planningMode: 'leaf' as const,
  parentRequestId: '',
  selectedResourceId: '',
  startDate: '',
  startTime: '',
  endDate: '',
  endTime: '',
  earliestStartDate: '',
  earliestStartTime: '',
  latestEndDate: '',
  latestEndTime: '',
  durationValue: 0,
  durationUnit: 'hours' as const,
  schedulingSettingsApply: false,
  requirements: new Map<string, RequirementEntry>(),
  selectedCriterionId: '',
};

describe('RequestScheduleSection', () => {
  const defaultProps = {
    state: baseState,
    setField: vi.fn(),
    availableSpaces: mockSpaces,
  };

  it('renders schedule heading', () => {
    render(<RequestScheduleSection {...defaultProps} />);
    expect(screen.getByText(/schedule \(optional\)/i)).toBeInTheDocument();
  });

  it('renders start and end date time pickers', () => {
    render(<RequestScheduleSection {...defaultProps} />);
    expect(screen.getByTestId('startDateTime')).toBeInTheDocument();
    expect(screen.getByTestId('endDateTime')).toBeInTheDocument();
  });

  it('renders space selector with available spaces', () => {
    render(<RequestScheduleSection {...defaultProps} />);
    expect(screen.getByText('Room A')).toBeInTheDocument();
  });

  it('shows help text about unscheduled requests', () => {
    render(<RequestScheduleSection {...defaultProps} />);
    expect(screen.getByText(/leave dates blank/i)).toBeInTheDocument();
  });

  it('calls splitDateTimeFields (and setField) when start date changes', () => {
    render(<RequestScheduleSection {...defaultProps} />);
    fireEvent.change(screen.getByTestId('startDateTime'), { target: { value: '2026-06-01T09:00' } });
    expect(mockSplitDateTimeFields).toHaveBeenCalled();
  });

  it('calls splitDateTimeFields when end date changes', () => {
    render(<RequestScheduleSection {...defaultProps} />);
    fireEvent.change(screen.getByTestId('endDateTime'), { target: { value: '2026-06-01T17:00' } });
    expect(mockSplitDateTimeFields).toHaveBeenCalled();
  });

  it('renders with no available spaces without crashing', () => {
    render(<RequestScheduleSection {...defaultProps} availableSpaces={[]} />);
    expect(screen.getByText(/schedule \(optional\)/i)).toBeInTheDocument();
  });

  it('shows "No space assigned" placeholder text', () => {
    render(<RequestScheduleSection {...defaultProps} />);
    expect(screen.getByText(/no space \(unscheduled\)/i)).toBeInTheDocument();
  });
});
