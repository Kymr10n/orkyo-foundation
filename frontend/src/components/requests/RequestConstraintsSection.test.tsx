import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import type { ReactNode } from 'react';
import { RequestConstraintsSection } from './RequestConstraintsSection';

vi.mock('@foundation/src/components/ui/collapsible', () => ({
  Collapsible: ({
    children,
    onOpenChange,
  }: {
    children: ReactNode;
    open: boolean;
    onOpenChange: () => void;
  }) => <div onClick={onOpenChange}>{children}</div>,
  CollapsibleTrigger: ({
    children,
    className,
  }: {
    children: ReactNode;
    className?: string;
  }) => (
    <button type="button" className={className}>
      {children}
    </button>
  ),
  CollapsibleContent: ({ children }: { children: ReactNode }) => (
    <div>{children}</div>
  ),
}));

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
function makeState(constraintsOpen = false): any {
  return {
    openSections: { constraints: constraintsOpen },
    earliestStartDate: '',
    earliestStartTime: '',
    latestEndDate: '',
    latestEndTime: '',
  };
}

describe('RequestConstraintsSection', () => {
  it('renders the section trigger label', () => {
    render(
      <RequestConstraintsSection
        state={makeState()}
        setField={vi.fn()}
        toggleSection={vi.fn()}
      />,
    );
    expect(
      screen.getByText('Scheduling Constraints (Optional)'),
    ).toBeInTheDocument();
  });

  it('calls toggleSection("constraints") when trigger is clicked', () => {
    const toggleSection = vi.fn();
    render(
      <RequestConstraintsSection
        state={makeState()}
        setField={vi.fn()}
        toggleSection={toggleSection}
      />,
    );
    fireEvent.click(screen.getByRole('button'));
    expect(toggleSection).toHaveBeenCalledWith('constraints');
  });

  it('renders Earliest Start and Latest End pickers', () => {
    render(
      <RequestConstraintsSection
        state={makeState(true)}
        setField={vi.fn()}
        toggleSection={vi.fn()}
      />,
    );
    expect(screen.getByTestId('picker-earliestStart')).toBeInTheDocument();
    expect(screen.getByTestId('picker-latestEnd')).toBeInTheDocument();
  });

  it('renders the correct placeholder on each picker', () => {
    render(
      <RequestConstraintsSection
        state={makeState(true)}
        setField={vi.fn()}
        toggleSection={vi.fn()}
      />,
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
      <RequestConstraintsSection
        state={makeState(true)}
        setField={setField}
        toggleSection={vi.fn()}
      />,
    );
    fireEvent.change(screen.getByTestId('picker-earliestStart'), {
      target: { value: '2026-06-01T09:00' },
    });
    expect(setField).toHaveBeenCalledWith('earliestStartDate', '2026-06-01');
  });
});
