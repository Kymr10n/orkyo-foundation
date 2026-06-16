import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import {
  StatusIndicator,
  StatusBanner,
  StatusMessageList,
  TabIndicatorDot,
  severityDotClass,
  worstSeverity,
  type StatusItem,
} from './status-indicator';

const warn: StatusItem = { id: 's1', severity: 'warning', message: 'A warning happened' };
const err: StatusItem = { id: 's2', severity: 'error', message: 'An error happened' };

describe('worstSeverity', () => {
  it('returns null when empty', () => {
    expect(worstSeverity([])).toBeNull();
  });

  it('returns warning for warning-only items', () => {
    expect(worstSeverity([warn])).toBe('warning');
  });

  it('escalates to error when any item is an error', () => {
    expect(worstSeverity([warn, err])).toBe('error');
  });
});

describe('severityDotClass', () => {
  it('returns null when there are no items', () => {
    expect(severityDotClass([])).toBeNull();
  });

  it('returns the amber class for warning-only items', () => {
    expect(severityDotClass([warn])).toBe('bg-amber-500');
  });

  it('returns the destructive class when any item is an error', () => {
    expect(severityDotClass([warn, err])).toBe('bg-destructive');
  });
});

describe('StatusIndicator', () => {
  it('renders nothing when there are no items', () => {
    const { container } = render(<StatusIndicator items={[]} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('renders a warning indicator for warning-only items', () => {
    render(<StatusIndicator items={[warn]} />);
    expect(screen.getByTestId('status-indicator')).toHaveAttribute('aria-label', 'Has a warning');
  });

  it('escalates to an error indicator when any item is an error', () => {
    render(<StatusIndicator items={[warn, err]} />);
    expect(screen.getByTestId('status-indicator')).toHaveAttribute('aria-label', 'Has a conflict');
  });

  it('honours a custom testId', () => {
    render(<StatusIndicator items={[warn]} testId="my-indicator" />);
    expect(screen.getByTestId('my-indicator')).toBeInTheDocument();
  });
});

describe('StatusBanner', () => {
  it('renders nothing when there are no items', () => {
    const { container } = render(<StatusBanner items={[]} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('defaults to a generic issue count and lists every message', () => {
    render(<StatusBanner items={[warn, err]} />);
    const banner = screen.getByTestId('status-banner');
    expect(banner).toHaveTextContent('2 issues');
    expect(banner).toHaveTextContent('A warning happened');
    expect(banner).toHaveTextContent('An error happened');
  });

  it('uses the singular for a single item', () => {
    render(<StatusBanner items={[warn]} />);
    expect(screen.getByTestId('status-banner')).toHaveTextContent('1 issue');
  });

  it('renders a custom title', () => {
    render(<StatusBanner items={[warn]} title="Custom heading" />);
    expect(screen.getByTestId('status-banner')).toHaveTextContent('Custom heading');
  });
});

describe('StatusMessageList', () => {
  it('renders nothing when there are no items', () => {
    const { container } = render(<StatusMessageList items={[]} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('renders one row per item with its message', () => {
    render(<StatusMessageList items={[warn, err]} />);
    expect(screen.getByText('A warning happened')).toBeInTheDocument();
    expect(screen.getByText('An error happened')).toBeInTheDocument();
  });
});

describe('TabIndicatorDot', () => {
  it('renders nothing when dotClass is null', () => {
    const { container } = render(<TabIndicatorDot dotClass={null} label="x" />);
    expect(container).toBeEmptyDOMElement();
  });

  it('renders a labelled dot with the given class', () => {
    render(<TabIndicatorDot dotClass="bg-amber-500" label="timing warning" />);
    const dot = screen.getByLabelText('timing warning');
    expect(dot).toHaveClass('bg-amber-500', 'rounded-full');
  });
});
