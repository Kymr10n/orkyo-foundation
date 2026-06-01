import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ReportingApiPage } from './ReportingApiPage';
import type { ReportingTokenSummary } from '@foundation/src/lib/api/reporting-tokens-api';

vi.mock('@foundation/src/lib/api/reporting-tokens-api', () => ({
  listReportingTokens: vi.fn(),
  createReportingToken: vi.fn(),
  revokeReportingToken: vi.fn(),
}));

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

import {
  listReportingTokens,
  createReportingToken,
  revokeReportingToken,
  type CreatedReportingToken,
} from '@foundation/src/lib/api/reporting-tokens-api';

const activeToken: ReportingTokenSummary = {
  id: 'tok-1',
  tenantId: 'tenant-1',
  name: 'Power BI Dashboard',
  tokenPrefix: 'abc123',
  scopes: 'reporting:read',
  createdByUserId: 'user-1',
  isActive: true,
  createdAtUtc: '2026-01-01T00:00:00Z',
  lastUsedAtUtc: null,
  expiresAtUtc: null,
  revokedAtUtc: null,
};

const revokedToken: ReportingTokenSummary = {
  id: 'tok-2',
  tenantId: 'tenant-1',
  name: 'Old Token',
  tokenPrefix: 'def456',
  scopes: 'reporting:read',
  createdByUserId: null,
  isActive: false,
  createdAtUtc: '2025-06-01T00:00:00Z',
  lastUsedAtUtc: '2025-12-01T00:00:00Z',
  expiresAtUtc: null,
  revokedAtUtc: '2026-01-15T00:00:00Z',
};

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <ReportingApiPage />
    </QueryClientProvider>,
  );
}

describe('ReportingApiPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(listReportingTokens).mockResolvedValue([activeToken]);
    vi.mocked(createReportingToken).mockResolvedValue({
      summary: { ...activeToken, id: 'new-tok' },
      rawToken: 'supersecrettoken',
    } satisfies CreatedReportingToken);
    vi.mocked(revokeReportingToken).mockResolvedValue();
  });

  it('renders page heading', async () => {
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('Reporting API')).toBeInTheDocument();
    });
  });

  it('shows loading spinner initially', () => {
    vi.mocked(listReportingTokens).mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(document.querySelector('svg.animate-spin')).toBeInTheDocument();
  });

  it('renders token table after loading', async () => {
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('Power BI Dashboard')).toBeInTheDocument();
      expect(screen.getByText('abc123…')).toBeInTheDocument();
    });
  });

  it('shows Active badge for active token', async () => {
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('Active')).toBeInTheDocument();
    });
  });

  it('shows Revoked badge for revoked token', async () => {
    vi.mocked(listReportingTokens).mockResolvedValue([revokedToken]);
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('Revoked')).toBeInTheDocument();
    });
  });

  it('shows empty state when no tokens', async () => {
    vi.mocked(listReportingTokens).mockResolvedValue([]);
    renderPage();
    await waitFor(() => {
      expect(screen.getByText(/No reporting tokens yet/)).toBeInTheDocument();
    });
  });

  it('shows error state on API failure', async () => {
    vi.mocked(listReportingTokens).mockRejectedValue(new Error('Unauthorized'));
    renderPage();
    await waitFor(() => {
      expect(screen.getByText(/Failed to load reporting tokens/)).toBeInTheDocument();
    });
  });

  it('shows Power BI quick-start section', async () => {
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('Power BI quick-start')).toBeInTheDocument();
    });
  });

  it('has a New token button', async () => {
    renderPage();
    await waitFor(() => screen.getByText('Power BI Dashboard'));
    expect(screen.getByRole('button', { name: /New token/i })).toBeInTheDocument();
  });

  it('opens create token dialog when New token is clicked', async () => {
    const user = userEvent.setup();
    renderPage();
    await waitFor(() => screen.getByText('Power BI Dashboard'));
    await user.click(screen.getByRole('button', { name: /New token/i }));
    await waitFor(() => {
      expect(screen.getByRole('heading', { name: 'Create Reporting Token' })).toBeInTheDocument();
    });
  });

  it('shows revoke button only for active tokens', async () => {
    vi.mocked(listReportingTokens).mockResolvedValue([activeToken, revokedToken]);
    renderPage();
    await waitFor(() => screen.getByText('Power BI Dashboard'));
    // Only the active token has a Revoke button
    expect(screen.getByRole('button', { name: /Revoke Power BI Dashboard/i })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Revoke Old Token/i })).not.toBeInTheDocument();
  });
});
