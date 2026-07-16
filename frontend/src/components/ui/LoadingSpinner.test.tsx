import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { LoadingSpinner } from './LoadingSpinner';

describe('LoadingSpinner', () => {
  it('renders with a message', () => {
    render(<LoadingSpinner message="Loading…" />);
    expect(screen.getByText('Loading…')).toBeInTheDocument();
  });

  it('renders the spinner icon', () => {
    const { container } = render(<LoadingSpinner message="Loading…" />);
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

  it('exposes an accessible busy status', () => {
    render(<LoadingSpinner message="Loading requests…" />);
    const status = screen.getByRole('status');
    expect(status).toHaveAttribute('aria-busy', 'true');
  });

  it('provides a screen-reader label when no message is given', () => {
    render(<LoadingSpinner />);
    expect(screen.getByText('Loading')).toBeInTheDocument();
  });

  it('renders a small muted icon for the section-loader variant', () => {
    const { container } = render(<LoadingSpinner size="sm" muted fullScreen={false} />);
    const icon = container.querySelector('.animate-spin') as HTMLElement;
    expect(icon.classList.contains('h-6')).toBe(true);
    expect(icon.classList.contains('text-muted-foreground')).toBe(true);
  });

  it('merges caller container classes over the contained-mode defaults', () => {
    const { container } = render(<LoadingSpinner fullScreen={false} className="h-64" />);
    const root = container.firstChild as HTMLElement;
    expect(root.className).toMatch(/h-64/);
    expect(root.className).not.toMatch(/h-full/);
  });
});
