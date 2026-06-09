import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { LoadingSpinner } from './LoadingSpinner';

describe('LoadingSpinner', () => {
  it('renders with a message', () => {
    render(<LoadingSpinner message="Loading..." />);
    expect(screen.getByText('Loading...')).toBeInTheDocument();
  });

  it('renders the spinner icon', () => {
    const { container } = render(<LoadingSpinner message="Loading..." />);
    expect(container.querySelector('.animate-spin')).toBeInTheDocument();
  });

  it('renders without a message when none is provided', () => {
    const { container } = render(<LoadingSpinner />);
    expect(container.querySelector('p')).not.toBeInTheDocument();
    expect(container.querySelector('.animate-spin')).toBeInTheDocument();
  });

  it('defaults to a full-screen layout', () => {
    const { container } = render(<LoadingSpinner />);
    expect(container.querySelector('.min-h-screen')).toBeInTheDocument();
  });

  it('fills its container (not full-screen) when fullScreen is false', () => {
    const { container } = render(<LoadingSpinner fullScreen={false} />);
    expect(container.querySelector('.min-h-screen')).not.toBeInTheDocument();
    const root = container.firstChild as HTMLElement;
    expect(root.className).toMatch(/h-full/);
    expect(root.className).toMatch(/w-full/);
  });
});
