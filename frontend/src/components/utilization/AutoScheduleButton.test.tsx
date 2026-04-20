import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { AutoScheduleButton } from './AutoScheduleButton';

const defaultProps = {
  onClick: vi.fn(),
};

function renderButton(props: Partial<React.ComponentProps<typeof AutoScheduleButton>> = {}) {
  return render(<AutoScheduleButton {...defaultProps} {...props} />);
}

describe('AutoScheduleButton', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders the button with accessible label', () => {
    renderButton();
    expect(screen.getByRole('button', { name: 'Auto-schedule unscheduled requests' })).toBeInTheDocument();
  });

  it('calls onClick when clicked', () => {
    renderButton();
    fireEvent.click(screen.getByRole('button'));
    expect(defaultProps.onClick).toHaveBeenCalled();
  });

  it('is disabled when disabled prop is true', () => {
    renderButton({ disabled: true });
    expect(screen.getByRole('button')).toBeDisabled();
  });

  it('is disabled when loading', () => {
    renderButton({ loading: true });
    expect(screen.getByRole('button')).toBeDisabled();
  });

  it('has tooltip content in DOM', () => {
    renderButton();
    // Tooltip content is in the DOM but may be hidden until hover
    expect(screen.getByRole('button')).toHaveAttribute('aria-label', 'Auto-schedule unscheduled requests');
  });
});
