import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { CanvasInstructions } from './CanvasInstructions';

const defaultProps = {
  isPassiveMode: false,
  drawingMode: 'rectangle' as const,
  drawingPoints: [] as { x: number; y: number }[],
  selectedSpaceId: undefined,
};

function renderInstructions(props: Partial<React.ComponentProps<typeof CanvasInstructions>> = {}) {
  return render(<CanvasInstructions {...defaultProps} {...props} />);
}

describe('CanvasInstructions', () => {
  it('renders nothing in passive mode', () => {
    const { container } = renderInstructions({ isPassiveMode: true });
    expect(container.innerHTML).toBe('');
  });

  it('renders rectangle instruction — first corner', () => {
    renderInstructions({ drawingMode: 'rectangle', drawingPoints: [] });
    expect(screen.getByText('Click to place first corner')).toBeInTheDocument();
  });

  it('renders rectangle instruction — opposite corner', () => {
    renderInstructions({
      drawingMode: 'rectangle',
      drawingPoints: [{ x: 0, y: 0 }],
    });
    expect(screen.getByText('Click to place opposite corner')).toBeInTheDocument();
  });

  it('renders polygon instruction — needs more points', () => {
    renderInstructions({ drawingMode: 'polygon', drawingPoints: [{ x: 0, y: 0 }] });
    expect(screen.getByText(/1\/3 minimum/)).toBeInTheDocument();
  });

  it('renders polygon instruction — enough points', () => {
    renderInstructions({
      drawingMode: 'polygon',
      drawingPoints: [{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 1, y: 1 }],
    });
    expect(screen.getByText('Double-click to complete polygon')).toBeInTheDocument();
  });

  it('shows ESC cancel hint in drawing mode', () => {
    renderInstructions();
    expect(screen.getByText('Press ESC to cancel')).toBeInTheDocument();
  });

  it('renders resize instructions when in resize mode with selected space', () => {
    renderInstructions({
      isPassiveMode: true,
      drawingMode: 'resize',
      drawingPoints: [],
      selectedSpaceId: 'space-1',
    });
    expect(screen.getByText('Drag the handles to resize the space')).toBeInTheDocument();
  });

  it('renders nothing in resize mode without selected space', () => {
    const { container } = renderInstructions({
      isPassiveMode: true,
      drawingMode: 'resize',
      drawingPoints: [],
      selectedSpaceId: undefined,
    });
    expect(container.innerHTML).toBe('');
  });
});
