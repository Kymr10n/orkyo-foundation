import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { UsageLimitsSettings } from './UsageLimitsSettings';
import { useQuotas } from '@foundation/src/hooks/useQuotas';
import type { TenantQuotasResponse } from '@foundation/src/lib/api/quotas-api';

vi.mock('@foundation/src/hooks/useQuotas', () => ({ useQuotas: vi.fn() }));
const mockUseQuotas = vi.mocked(useQuotas);

function setQuotas(over: Partial<ReturnType<typeof useQuotas>>) {
  mockUseQuotas.mockReturnValue({
    data: undefined,
    isLoading: false,
    isError: false,
    ...over,
  } as ReturnType<typeof useQuotas>);
}

const data: TenantQuotasResponse = {
  quotas: [
    { key: 'storage_bytes', unit: 'bytes', used: 50, limit: 100, unlimited: false, percentUsed: 50 },
    { key: 'sites', unit: 'count', used: 3, limit: 5, unlimited: false, percentUsed: 60 },
    { key: 'users', unit: 'count', used: 9, limit: 0, unlimited: true, percentUsed: 0 },
  ],
  entitlements: [
    { key: 'reporting_api', enabled: true },
    { key: 'sso', enabled: false },
  ],
};

describe('UsageLimitsSettings', () => {
  beforeEach(() => mockUseQuotas.mockReset());

  it('renders a loading skeleton', () => {
    setQuotas({ isLoading: true });
    const { container } = render(<UsageLimitsSettings />);
    expect(container.querySelector('.animate-pulse')).toBeInTheDocument();
  });

  it('renders an error state', () => {
    setQuotas({ isError: true });
    render(<UsageLimitsSettings />);
    expect(screen.getByText(/Unable to load usage data/)).toBeInTheDocument();
  });

  it('renders storage, count quotas (limited + unlimited) and entitlements', () => {
    setQuotas({ data });
    render(<UsageLimitsSettings />);
    expect(screen.getByText('Storage')).toBeInTheDocument();
    expect(screen.getByText('Usage limits')).toBeInTheDocument();
    // limited count quota shows "used / limit"
    expect(screen.getByText('3')).toBeInTheDocument();
    expect(screen.getByText(/\/ 5/)).toBeInTheDocument();
    // unlimited count quota shows "(no limit)"
    expect(screen.getByText(/no limit/)).toBeInTheDocument();
    // entitlements
    expect(screen.getByText('Enabled')).toBeInTheDocument();
    expect(screen.getByText('Not available')).toBeInTheDocument();
  });
});
