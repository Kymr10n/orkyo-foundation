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
    { key: 'production_sites', unit: 'count', used: 3, limit: 5, unlimited: false, percentUsed: 60 },
    { key: 'spaces', unit: 'count', used: 68, limit: 250, unlimited: false, percentUsed: 27 },
    { key: 'active_seats', unit: 'count', used: 9, limit: 0, unlimited: true, percentUsed: 0 },
  ],
  entitlements: [
    { key: 'api_access_enabled', enabled: true },
    { key: 'ai_assistant_enabled', enabled: false },
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

  it('labels the spaces quota with the current vocabulary', () => {
    // The 0.18.0 rename (spaces -> stations) reached every other surface; the quota
    // label was the straggler. The key stays `spaces` in the database.
    setQuotas({ data });
    render(<UsageLimitsSettings />);

    expect(screen.getByText('Stations')).toBeInTheDocument();
    expect(screen.queryByText('Spaces')).not.toBeInTheDocument();
  });

  it('names every entitlement it shows', () => {
    // A key with no label used to fall back to the raw key, so `ai_assistant_enabled`
    // reached people as snake_case for a whole release. Every key we render needs a name.
    setQuotas({ data });
    render(<UsageLimitsSettings />);

    expect(screen.getByText('AI Assistant')).toBeInTheDocument();
    expect(screen.queryByText(/_enabled/)).not.toBeInTheDocument();
  });

  it('never renders a quota key it has no name for', () => {
    // The label map decides what belongs on this page. Without the filter, an unnamed key
    // would reach people as raw snake_case through the `?? quota.key` fallback.
    setQuotas({
      data: {
        ...data,
        quotas: [
          ...data.quotas,
          { key: 'unlabelled_future_quota', unit: 'count', used: 0, limit: 0, unlimited: true, percentUsed: 0 },
        ],
      },
    });
    render(<UsageLimitsSettings />);

    expect(screen.queryByText(/unlabelled_future_quota/)).not.toBeInTheDocument();
  });
});
