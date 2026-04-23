import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { RequestScheduleSection } from './RequestScheduleSection';
import type { Space } from '@foundation/src/types/space';
import type { ReactNode } from 'react';

vi.mock('@foundation/src/components/ui/collapsible', () => ({
  Collapsible: ({ children, open }: { children: ReactNode; open: boolean }) =>
    open ? <div>{children}</div> : <div>{(children as ReactNode[])?.[0]}</div>,
  CollapsibleTrigger: ({ children }: { children: ReactNode }) => <button>{children}</button>,
  CollapsibleContent: ({ children }: { children: ReactNode }) => <div>{children}</div>,
}));

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
  DateTimePicker: ({ placeholder, id }: { placeholder: string; id: string }) =>
    <input data-testid={id} placeholder={placeholder} />,
}));

vi.mock('@foundation/src/constants', () => ({
  SPACE_NONE_PLACEHOLDER: '__none__',
}));

vi.mock('@foundation/src/lib/utils/picker-utils', () => ({
  combineDateTimeFields: (d: string, t: string) => d && t ? `${d}T${t}` : '',
  splitDateTimeFields: vi.fn(),
}));

const mockSpaces: Space[] = [
  { id: 's1', name: 'Room A', siteId: 'site1', isPhysical: true, capacity: 10, createdAt: '', updatedAt: '' },
];

const baseState = {
  name: '',
  description: '',
  planningMode: 'leaf' as const,
  parentRequestId: '',
  selectedSpaceId: '',
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
  requirements: new Map<string, boolean | number | string | null>(),
  selectedCriterionId: '',
  openSections: { basic: true, schedule: true, constraints: true, duration: true, requirements: true },
};

describe('RequestScheduleSection', () => {
  const defaultProps = {
    state: baseState,
    setField: vi.fn(),
    toggleSection: vi.fn(),
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
});
