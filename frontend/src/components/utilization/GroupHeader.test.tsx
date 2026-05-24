import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { GroupHeader } from './GroupHeader';

const defaultProps = {
  groupName: 'Production Hall',
  groupColor: '#3b82f6',
  count: 2,
  isCollapsed: false,
  onToggle: vi.fn(),
};

function renderHeader(props: Partial<React.ComponentProps<typeof GroupHeader>> = {}) {
  return render(<GroupHeader {...defaultProps} {...props} />);
}

describe('GroupHeader', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders group name', () => {
    renderHeader();
    expect(screen.getByText('Production Hall')).toBeInTheDocument();
  });

  it('renders count', () => {
    renderHeader();
    expect(screen.getByText('(2)')).toBeInTheDocument();
  });

  it('renders color indicator when groupColor is set', () => {
    const { container } = renderHeader();
    const colorDiv = container.querySelector('[style*="background-color"]');
    expect(colorDiv).toBeTruthy();
  });

  it('does not render color indicator when no groupColor', () => {
    const { container } = renderHeader({ groupColor: undefined });
    const colorDiv = container.querySelector('[style*="background-color"]');
    expect(colorDiv).toBeNull();
  });

  it('calls onToggle when clicked', () => {
    renderHeader();
    fireEvent.click(screen.getByText('Production Hall'));
    expect(defaultProps.onToggle).toHaveBeenCalled();
  });
});
