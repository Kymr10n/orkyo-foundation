import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { DateTimePicker } from './date-time-picker';

describe('DateTimePicker', () => {
  it('shows the placeholder when value is empty', () => {
    render(<DateTimePicker value="" onChange={() => {}} placeholder="Pick when" />);
    expect(screen.getByText('Pick when')).toBeInTheDocument();
  });

  it('renders the formatted date+time for a value', () => {
    render(<DateTimePicker value="2026-06-15T09:30" onChange={() => {}} />);
    expect(screen.getByText('Jun 15, 2026 09:30')).toBeInTheDocument();
  });

  it('can be disabled', () => {
    render(<DateTimePicker value="" onChange={() => {}} id="dt" disabled />);
    expect(screen.getByRole('button')).toBeDisabled();
  });

  it('opens a calendar and time controls', () => {
    render(<DateTimePicker value="2026-06-15T09:30" onChange={() => {}} />);
    fireEvent.click(screen.getByRole('button'));
    expect(screen.getByRole('grid')).toBeInTheDocument();
    expect(screen.getByText('Time:')).toBeInTheDocument();
  });

  it('keeps the existing time when a new day is picked', () => {
    const onChange = vi.fn();
    render(<DateTimePicker value="2026-06-15T09:30" onChange={onChange} />);
    fireEvent.click(screen.getByRole('button'));
    // Pick another day in the visible month; the 09:30 time must be preserved.
    const day = screen.getByRole('gridcell', { name: '20' }).querySelector('button')
      ?? screen.getByText('20');
    fireEvent.click(day);
    expect(onChange).toHaveBeenCalledWith('2026-06-20T09:30');
  });

  it('updates the hour while keeping the date and minutes', () => {
    const onChange = vi.fn();
    render(<DateTimePicker value="2026-06-15T09:30" onChange={onChange} />);
    fireEvent.click(screen.getByRole('button'));
    const [hourTrigger] = screen.getAllByRole('combobox');
    fireEvent.click(hourTrigger);
    fireEvent.click(screen.getByRole('option', { name: '14' }));
    expect(onChange).toHaveBeenCalledWith('2026-06-15T14:30');
  });

  it('updates the minutes while keeping the date and hour', () => {
    const onChange = vi.fn();
    render(<DateTimePicker value="2026-06-15T09:30" onChange={onChange} />);
    fireEvent.click(screen.getByRole('button'));
    const minuteTrigger = screen.getAllByRole('combobox')[1];
    fireEvent.click(minuteTrigger);
    fireEvent.click(screen.getByRole('option', { name: '45' }));
    expect(onChange).toHaveBeenCalledWith('2026-06-15T09:45');
  });
});
