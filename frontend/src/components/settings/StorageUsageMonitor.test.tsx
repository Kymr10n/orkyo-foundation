import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { StorageUsageMonitor } from './StorageUsageMonitor';
import type { NumericQuota } from '@foundation/src/lib/api/quotas-api';

function quota(p: Partial<NumericQuota>): NumericQuota {
  return { key: 'storage', used: 0, limit: 100, unlimited: false, percentUsed: 0, ...p } as NumericQuota;
}

describe('StorageUsageMonitor', () => {
  it('shows used / limit and a neutral message below 80%', () => {
    render(<StorageUsageMonitor quota={quota({ used: 50, limit: 100, percentUsed: 50 })} />);
    expect(screen.getByText('50 B')).toBeInTheDocument();
    expect(screen.getByText(/100 B used/)).toBeInTheDocument();
    expect(screen.getByText(/50% of 100 B used/)).toBeInTheDocument();
  });

  it('warns when approaching the limit', () => {
    render(<StorageUsageMonitor quota={quota({ used: 90, limit: 100, percentUsed: 90 })} />);
    expect(screen.getByText(/approaching limit/)).toBeInTheDocument();
  });

  it('shows an at-capacity message when full but not over', () => {
    render(<StorageUsageMonitor quota={quota({ used: 100, limit: 100, percentUsed: 100 })} />);
    expect(screen.getByText(/At capacity/)).toBeInTheDocument();
  });

  it('shows a blocked message when over the limit', () => {
    render(<StorageUsageMonitor quota={quota({ used: 120, limit: 100, percentUsed: 120 })} />);
    expect(screen.getByText(/over limit/)).toBeInTheDocument();
  });

  it('renders an unlimited (no limit) state without a usage message', () => {
    render(
      <StorageUsageMonitor
        quota={quota({ used: 5000, limit: 0, unlimited: true, percentUsed: 0 })}
      />,
    );
    expect(screen.getByText(/no limit/)).toBeInTheDocument();
    // The usage-message paragraph is omitted entirely when unlimited.
    expect(screen.queryByText(/% of|approaching|At capacity|over limit/)).not.toBeInTheDocument();
  });
});
