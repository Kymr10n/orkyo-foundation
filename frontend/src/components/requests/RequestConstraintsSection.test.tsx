import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { RequestConstraintsSection } from './RequestConstraintsSection';

vi.mock('@foundation/src/components/ui/date-time-picker', () => ({
  DateTimePicker: ({
    id,
    placeholder,
    onChange,
  }: {
    id: string;
    value?: string | null;
    placeholder?: string;
    onChange?: (v: string | null) => void;
  }) => (
    <input
      data-testid={`picker-${id}`}
      placeholder={placeholder}
      onChange={(e) => onChange?.(e.target.value || null)}
    />
  ),
}));

vi.mock('@foundation/src/lib/utils/picker-utils', () => ({
  combineDateTimeFields: (d?: string, t?: string) =>
    d && t ? `${d}T${t}` : null,
  splitDateTimeFields: (
    v: string | null,
    setDate: (d: string) => void,
    setTime: (t: string) => void,
  ) => {
    if (v) {
      setDate(v.slice(0, 10));
      setTime(v.slice(11));
    }
  },
}));

// eslint-disable-next-line @typescript-eslint/no-explicit-any
function makeState(): any {
  return {
    earliestStartDate: '',
    earliestStartTime: '',
    latestEndDate: '',
    latestEndTime: '',
  };
}

describe('RequestConstraintsSection', () => {
  it('renders the section heading', () => {
    render(
      <RequestConstraintsSection state={makeState()} setField={vi.fn()} />,
    );
    expect(
      screen.getByText('Scheduling Constraints (Optional)'),
    ).toBeInTheDocument();
  });

  it('renders Earliest Start and Latest End pickers', () => {
    render(
      <RequestConstraintsSection state={makeState()} setField={vi.fn()} />,
    );
    expect(screen.getByTestId('picker-earliestStart')).toBeInTheDocument();
    expect(screen.getByTestId('picker-latestEnd')).toBeInTheDocument();
  });

  it('renders the correct placeholder on each picker', () => {
    render(
      <RequestConstraintsSection state={makeState()} setField={vi.fn()} />,
    );
    expect(
      screen.getByPlaceholderText('Pick earliest start'),
    ).toBeInTheDocument();
    expect(
      screen.getByPlaceholderText('Pick latest end'),
    ).toBeInTheDocument();
  });

  it('calls setField when the earliest start picker changes', () => {
    const setField = vi.fn();
    render(
      <RequestConstraintsSection state={makeState()} setField={setField} />,
    );
    fireEvent.change(screen.getByTestId('picker-earliestStart'), {
      target: { value: '2026-06-01T09:00' },
    });
    expect(setField).toHaveBeenCalledWith('earliestStartDate', '2026-06-01');
  });
});
