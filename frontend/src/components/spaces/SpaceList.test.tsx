import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { SpaceList } from './SpaceList';
import type { Space } from '@foundation/src/types/space';

function makeSpace(overrides: Partial<Space> = {}): Space {
  return {
    id: 'space-1',
    siteId: 'site-1',
    name: 'Room A',
    code: 'RM-A',
    description: 'A meeting room',
    isPhysical: true,
    geometry: {
      type: 'rectangle',
      coordinates: [
        { x: 0, y: 0 },
        { x: 100, y: 0 },
        { x: 100, y: 100 },
        { x: 0, y: 100 },
      ],
    },
    properties: {},
    groupId: undefined,
    capacity: 1,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    ...overrides,
  };
}

const defaultProps = {
  spaces: [makeSpace()],
  selectedSpaceId: null,
  onSpaceSelect: vi.fn(),
};

describe('SpaceList', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders loading state', () => {
    render(<SpaceList {...defaultProps} isLoading />);
    expect(screen.getByText('Loading spaces...')).toBeInTheDocument();
  });

  it('renders empty state', () => {
    render(<SpaceList {...defaultProps} spaces={[]} />);
    expect(screen.getByText('No spaces created yet')).toBeInTheDocument();
  });

  it('renders space name and code', () => {
    render(<SpaceList {...defaultProps} />);
    expect(screen.getByText('Room A')).toBeInTheDocument();
    expect(screen.getByText('RM-A')).toBeInTheDocument();
  });

  it('renders description', () => {
    render(<SpaceList {...defaultProps} />);
    expect(screen.getByText('A meeting room')).toBeInTheDocument();
  });

  it('renders geometry info', () => {
    render(<SpaceList {...defaultProps} />);
    expect(screen.getByText('rectangle · 4 points')).toBeInTheDocument();
  });

  it('calls onSpaceSelect when clicking a space', () => {
    render(<SpaceList {...defaultProps} />);
    fireEvent.click(screen.getByText('Room A'));
    expect(defaultProps.onSpaceSelect).toHaveBeenCalledWith('space-1');
  });

  it('highlights selected space', () => {
    render(<SpaceList {...defaultProps} selectedSpaceId="space-1" />);
    const spaceEl = screen.getByText('Room A').closest('[class*="border-primary"]');
    expect(spaceEl).toBeInTheDocument();
  });

  it('renders edit button when onSpaceEdit provided', () => {
    const onSpaceEdit = vi.fn();
    render(<SpaceList {...defaultProps} onSpaceEdit={onSpaceEdit} />);
    const editBtn = screen.getByTitle('Edit Space');
    fireEvent.click(editBtn);
    expect(onSpaceEdit).toHaveBeenCalledWith(expect.objectContaining({ id: 'space-1' }));
    expect(defaultProps.onSpaceSelect).not.toHaveBeenCalled(); // e.stopPropagation
  });

  it('renders delete button when onSpaceDelete provided', () => {
    const onSpaceDelete = vi.fn();
    window.confirm = vi.fn(() => true);
    render(<SpaceList {...defaultProps} onSpaceDelete={onSpaceDelete} />);
    const deleteBtn = screen.getByTitle('Delete Space');
    fireEvent.click(deleteBtn);
    expect(onSpaceDelete).toHaveBeenCalledWith('space-1');
  });

  it('does not call onSpaceDelete when confirm is cancelled', () => {
    const onSpaceDelete = vi.fn();
    window.confirm = vi.fn(() => false);
    render(<SpaceList {...defaultProps} onSpaceDelete={onSpaceDelete} />);
    fireEvent.click(screen.getByTitle('Delete Space'));
    expect(onSpaceDelete).not.toHaveBeenCalled();
  });

  it('renders capabilities button when onCapabilitiesEdit provided', () => {
    const onCapabilitiesEdit = vi.fn();
    render(<SpaceList {...defaultProps} onCapabilitiesEdit={onCapabilitiesEdit} />);
    const capBtn = screen.getByTitle('Edit Capabilities');
    fireEvent.click(capBtn);
    expect(onCapabilitiesEdit).toHaveBeenCalledWith(expect.objectContaining({ id: 'space-1' }));
  });

  it('renders multiple spaces', () => {
    const spaces = [
      makeSpace({ id: 's1', name: 'Room A' }),
      makeSpace({ id: 's2', name: 'Room B', geometry: { type: 'polygon', coordinates: [{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 1, y: 1 }] } }),
    ];
    render(<SpaceList {...defaultProps} spaces={spaces} />);
    expect(screen.getByText('Room A')).toBeInTheDocument();
    expect(screen.getByText('Room B')).toBeInTheDocument();
  });

  it('handles space without code', () => {
    render(<SpaceList {...defaultProps} spaces={[makeSpace({ code: '' })]} />);
    expect(screen.getByText('Room A')).toBeInTheDocument();
  });
});
