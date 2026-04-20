import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { TimeNavigator } from './TimeNavigator';
import type { TimeScale } from './ScaleSelect';

const defaultProps = {
  scale: 'week' as TimeScale,
  anchorTs: new Date('2026-03-15T00:00:00'),
  onAnchorChange: vi.fn(),
  onPrevious: vi.fn(),
  onNext: vi.fn(),
  onToday: vi.fn(),
};

function renderNavigator(props: Partial<React.ComponentProps<typeof TimeNavigator>> = {}) {
  return render(<TimeNavigator {...defaultProps} {...props} />);
}

describe('TimeNavigator', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders Today button for non-hour scales', () => {
    renderNavigator();
    expect(screen.getByText('Today')).toBeInTheDocument();
  });

  it('renders Now button for hour scale', () => {
    renderNavigator({ scale: 'hour' });
    expect(screen.getByText('Now')).toBeInTheDocument();
  });

  it('formats year scale', () => {
    renderNavigator({ scale: 'year' });
    expect(screen.getByText('2026')).toBeInTheDocument();
  });

  it('formats month scale', () => {
    renderNavigator({ scale: 'month' });
    expect(screen.getByText('March 2026')).toBeInTheDocument();
  });

  it('formats week scale', () => {
    renderNavigator({ scale: 'week' });
    expect(screen.getByText('Mar 15, 2026')).toBeInTheDocument();
  });

  it('formats day scale', () => {
    renderNavigator({ scale: 'day' });
    expect(screen.getByText(/Sunday, Mar 15, 2026/)).toBeInTheDocument();
  });

  it('calls onPrevious when left arrow clicked', () => {
    renderNavigator();
    fireEvent.click(screen.getByTitle('Previous week'));
    expect(defaultProps.onPrevious).toHaveBeenCalled();
  });

  it('calls onNext when right arrow clicked', () => {
    renderNavigator();
    fireEvent.click(screen.getByTitle('Next week'));
    expect(defaultProps.onNext).toHaveBeenCalled();
  });

  it('calls onToday when Today button clicked', () => {
    renderNavigator();
    fireEvent.click(screen.getByText('Today'));
    expect(defaultProps.onToday).toHaveBeenCalled();
  });
});
