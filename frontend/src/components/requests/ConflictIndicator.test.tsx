import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ConflictIndicator, ConflictBanner, conflictDotClass } from './ConflictIndicator';
import type { Conflict } from '@foundation/src/types/requests';

const warn: Conflict = {
  id: 'c1', kind: 'starts_in_off_time', severity: 'warning',
  message: 'Resource has off-time during this period',
};
const err: Conflict = {
  id: 'c2', kind: 'overlap', severity: 'error',
  message: 'Resource is already assigned during this time window',
};

describe('ConflictIndicator', () => {
  it('renders nothing when there are no conflicts', () => {
    const { container } = render(<ConflictIndicator conflicts={[]} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('renders a warning indicator for warning-only conflicts', () => {
    render(<ConflictIndicator conflicts={[warn]} />);
    expect(screen.getByTestId('conflict-indicator')).toHaveAttribute('aria-label', 'Has a warning');
  });

  it('escalates to an error indicator when any conflict is an error', () => {
    render(<ConflictIndicator conflicts={[warn, err]} />);
    expect(screen.getByTestId('conflict-indicator')).toHaveAttribute('aria-label', 'Has a conflict');
  });
});

describe('conflictDotClass', () => {
  it('returns null when there are no conflicts', () => {
    expect(conflictDotClass([])).toBeNull();
  });

  it('returns the amber class for warning-only conflicts', () => {
    expect(conflictDotClass([warn])).toBe('bg-amber-500');
  });

  it('returns the destructive class when any conflict is an error', () => {
    expect(conflictDotClass([warn, err])).toBe('bg-destructive');
  });
});

describe('ConflictBanner', () => {
  it('renders nothing when there are no conflicts', () => {
    const { container } = render(<ConflictBanner conflicts={[]} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('lists every conflict message with a plural count', () => {
    render(<ConflictBanner conflicts={[warn, err]} />);
    const banner = screen.getByTestId('conflict-banner');
    expect(banner).toHaveTextContent('2 conflicts on this request');
    expect(banner).toHaveTextContent('Resource has off-time during this period');
    expect(banner).toHaveTextContent('Resource is already assigned during this time window');
  });

  it('uses the singular for a single conflict', () => {
    render(<ConflictBanner conflicts={[warn]} />);
    expect(screen.getByTestId('conflict-banner')).toHaveTextContent('1 conflict on this request');
  });
});
