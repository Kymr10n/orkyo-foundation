import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { GroupHeader } from './GroupHeader';
import type { SpacesByGroup } from './scheduler-types';

const mockGroup: SpacesByGroup = {
  groupId: 'g-1',
  groupName: 'Production Hall',
  groupColor: '#3b82f6',
  spaces: [
    { id: 'sp-1', name: 'Zone A', code: 'ZA', siteId: 's1', isPhysical: true, capacity: 1, createdAt: '', updatedAt: '' },
    { id: 'sp-2', name: 'Zone B', code: 'ZB', siteId: 's1', isPhysical: true, capacity: 1, createdAt: '', updatedAt: '' },
  ],
};

const noColorGroup: SpacesByGroup = {
  groupId: 'ungrouped',
  groupName: 'Ungrouped',
  groupColor: undefined,
  spaces: [{ id: 'sp-3', name: 'Zone C', code: 'ZC', siteId: 's1', isPhysical: true, capacity: 1, createdAt: '', updatedAt: '' }],
};

const defaultProps = {
  group: mockGroup,
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

  it('renders space count', () => {
    renderHeader();
    expect(screen.getByText('(2)')).toBeInTheDocument();
  });

  it('renders color indicator when groupColor is set', () => {
    const { container } = renderHeader();
    const colorDiv = container.querySelector('[style*="background-color"]');
    expect(colorDiv).toBeTruthy();
  });

  it('does not render color indicator when no groupColor', () => {
    const { container } = renderHeader({ group: noColorGroup });
    const colorDiv = container.querySelector('[style*="background-color"]');
    expect(colorDiv).toBeNull();
  });

  it('calls onToggle when clicked', () => {
    renderHeader();
    fireEvent.click(screen.getByText('Production Hall'));
    expect(defaultProps.onToggle).toHaveBeenCalled();
  });
});
