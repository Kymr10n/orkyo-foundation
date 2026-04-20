import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { ScaleSelect, type TimeScale } from './ScaleSelect';

const defaultProps = {
  value: 'week' as TimeScale,
  onChange: vi.fn(),
};

describe('ScaleSelect', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders with current value', () => {
    render(<ScaleSelect {...defaultProps} />);
    expect(screen.getByText('Week')).toBeInTheDocument();
  });

  it('shows all scale options when opened', () => {
    render(<ScaleSelect {...defaultProps} />);
    fireEvent.click(screen.getByRole('combobox'));
    expect(screen.getByText('Year')).toBeInTheDocument();
    expect(screen.getByText('Month')).toBeInTheDocument();
    expect(screen.getByText('Day')).toBeInTheDocument();
    expect(screen.getByText('Hour')).toBeInTheDocument();
  });

  it('calls onChange when a scale is selected', () => {
    render(<ScaleSelect {...defaultProps} />);
    fireEvent.click(screen.getByRole('combobox'));
    fireEvent.click(screen.getByText('Month'));
    expect(defaultProps.onChange).toHaveBeenCalledWith('month');
  });
});
