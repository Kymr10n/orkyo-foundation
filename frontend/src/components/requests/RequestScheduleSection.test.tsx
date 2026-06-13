import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { RequestScheduleSection } from './RequestScheduleSection';
import type { ReactNode } from 'react';
import type { RequirementEntry } from '@foundation/src/hooks/useRequestForm';

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

const mockSplitDateTimeFields = vi.fn();
vi.mock('@foundation/src/lib/utils/picker-utils', () => ({
  combineDateTimeFields: (d: string, t: string) => d && t ? `${d}T${t}` : '',
  splitDateTimeFields: (...args: unknown[]) => mockSplitDateTimeFields(...args),
}));

const baseState = {
  name: '',
  description: '',
  icon: null,
  planningMode: 'leaf' as const,
  parentRequestId: '',
  siteId: '',
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

});
