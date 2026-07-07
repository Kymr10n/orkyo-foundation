import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { RequestStatusBadge } from './RequestStatusBadge';

describe('RequestStatusBadge', () => {
  it('humanises the status label', () => {
    render(<RequestStatusBadge status="in_progress" />);
    expect(screen.getByText('In Progress')).toBeInTheDocument();
  });

  it('applies the status colour tint', () => {
    render(<RequestStatusBadge status="in_progress" />);
    expect(screen.getByText('In Progress').className).toContain('bg-amber-500/10');
  });

  it('merges a caller className', () => {
    render(<RequestStatusBadge status="new" className="text-xs flex-shrink-0" />);
    const badge = screen.getByText('New');
    expect(badge.className).toContain('text-xs');
    expect(badge.className).toContain('flex-shrink-0');
  });

  it('falls back to the raw value for an unknown status', () => {
    render(<RequestStatusBadge status="mystery" />);
    expect(screen.getByText('mystery')).toBeInTheDocument();
  });
});
