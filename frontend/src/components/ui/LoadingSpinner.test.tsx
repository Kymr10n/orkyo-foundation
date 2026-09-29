import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { LoadingSpinner } from './LoadingSpinner';

describe('LoadingSpinner', () => {
  it('renders with a message', () => {
    render(<LoadingSpinner message="Loading…" />);
    expect(screen.getByText('Loading…')).toBeInTheDocument();
  });

  it('renders without a message when none is provided', () => {
    const { container } = render(<LoadingSpinner />);
    expect(container.querySelector('p')).not.toBeInTheDocument();
  });

  it('exposes an accessible busy status', () => {
    render(<LoadingSpinner message="Loading requests…" />);
    const status = screen.getByRole('status');
    expect(status).toHaveAttribute('aria-busy', 'true');
  });

  it('provides a screen-reader label when no message is given', () => {
    render(<LoadingSpinner />);
    expect(screen.getByText('Loading')).toBeInTheDocument();
  });

  it('applies a caller className in contained mode', () => {
    const { container } = render(<LoadingSpinner fullScreen={false} className="h-64" />);
    const root = container.firstChild as HTMLElement;
    expect(root.className).toMatch(/h-64/);
  });
});
