import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
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

// Tier gate: ReportingApiPage requires API access (Professional+). Mock useAuth so
// tests control the current tier; default to Professional so the page renders.
const { authState } = vi.hoisted(() => ({
  authState: {
    membership: { tier: 'Professional' } as { tier: string } | null,
    isLoading: false,
  },
}));
vi.mock('@foundation/src/contexts/AuthContext', () => ({
  useAuth: () => ({ membership: authState.membership, isLoading: authState.isLoading }),
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
      <MemoryRouter>
        <ReportingApiPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

function addLocalDays(date: Date, days: number): Date {
  const next = new Date(date);
  next.setDate(next.getDate() + days);
  return next;
}

function toDateOnly(date: Date): string {
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function expectedPresetExpiry(days: number): string {
  return toDateOnly(addLocalDays(new Date(), days));
}

function expectedPresetLabel(days: number): string {
  return addLocalDays(new Date(), days).toLocaleDateString(undefined, {
    month: 'short',
    day: '2-digit',
    year: 'numeric',
  });
}

describe('ReportingApiPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    authState.membership = { tier: 'Professional' };
    authState.isLoading = false;
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

  it('forwards Free-tier users (token UI never renders)', async () => {
    authState.membership = { tier: 'Free' };
    renderPage();
    await waitFor(() => {
      expect(screen.queryByText('Reporting API')).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: /New token/i })).not.toBeInTheDocument();
    });
    // Free tier never hits the tokens API (query is gated/disabled)
    expect(listReportingTokens).not.toHaveBeenCalled();
  });

  it('renders for Enterprise tier', async () => {
    authState.membership = { tier: 'Enterprise' };
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

  it('shows the default expiration selector in the create token dialog', async () => {
    const user = userEvent.setup();
    renderPage();
    await waitFor(() => screen.getByText('Power BI Dashboard'));
    await user.click(screen.getByRole('button', { name: /New token/i }));

    expect(screen.getByText('Expiration')).toBeInTheDocument();
    expect(screen.getAllByText(`7 days (${expectedPresetLabel(7)})`).length).toBeGreaterThan(0);
    expect(screen.getByText('The token will expire on the selected date')).toBeInTheDocument();
  });

  it('shows all expiration menu options', async () => {
    const user = userEvent.setup();
    renderPage();
    await waitFor(() => screen.getByText('Power BI Dashboard'));
    await user.click(screen.getByRole('button', { name: /New token/i }));
    await user.click(screen.getByRole('combobox', { name: 'Expiration' }));

    expect(screen.getByRole('option', { name: `7 days (${expectedPresetLabel(7)})` })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: `30 days (${expectedPresetLabel(30)})` })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: `60 days (${expectedPresetLabel(60)})` })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: `90 days (${expectedPresetLabel(90)})` })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Custom' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'No expiration' })).toBeInTheDocument();
  });

  it('creates a token with the default 7-day expiry', async () => {
    const user = userEvent.setup();
    renderPage();
    await waitFor(() => screen.getByText('Power BI Dashboard'));
    await user.click(screen.getByRole('button', { name: /New token/i }));
    await user.type(screen.getByLabelText('Name'), 'Ops Dashboard');
    await user.click(screen.getByRole('button', { name: 'Create token' }));

    await waitFor(() => {
      expect(createReportingToken).toHaveBeenCalledWith({
        name: 'Ops Dashboard',
        expiresAt: expectedPresetExpiry(7),
      });
    });
  });

  it('creates a token with the selected preset expiry', async () => {
    const user = userEvent.setup();
    renderPage();
    await waitFor(() => screen.getByText('Power BI Dashboard'));
    await user.click(screen.getByRole('button', { name: /New token/i }));
    await user.type(screen.getByLabelText('Name'), 'Ops Dashboard');
    await user.click(screen.getByRole('combobox', { name: 'Expiration' }));
    await user.click(screen.getByRole('option', { name: `30 days (${expectedPresetLabel(30)})` }));
    await user.click(screen.getByRole('button', { name: 'Create token' }));

    await waitFor(() => {
      expect(createReportingToken).toHaveBeenCalledWith({
        name: 'Ops Dashboard',
        expiresAt: expectedPresetExpiry(30),
      });
    });
  });

  it('creates a token without expiresAt when No expiration is selected', async () => {
    const user = userEvent.setup();
    renderPage();
    await waitFor(() => screen.getByText('Power BI Dashboard'));
    await user.click(screen.getByRole('button', { name: /New token/i }));
    await user.type(screen.getByLabelText('Name'), 'Ops Dashboard');
    await user.click(screen.getByRole('combobox', { name: 'Expiration' }));
    await user.click(screen.getByRole('option', { name: 'No expiration' }));

    expect(screen.getByText('The token will not expire automatically')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Create token' }));

    await waitFor(() => {
      expect(createReportingToken).toHaveBeenCalledWith({
        name: 'Ops Dashboard',
      });
    });
  });

  it('reveals the required custom date picker when Custom is selected', async () => {
    const user = userEvent.setup();
    renderPage();
    await waitFor(() => screen.getByText('Power BI Dashboard'));
    await user.click(screen.getByRole('button', { name: /New token/i }));
    await user.click(screen.getByRole('combobox'));
    await user.click(screen.getByRole('option', { name: 'Custom' }));

    expect(screen.getByText('Select date *')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Select date *' })).toBeInTheDocument();
    expect(screen.getByText('dd . mm . yyyy')).toBeInTheDocument();
  });

  it('blocks token creation when custom expiry has no selected date', async () => {
    const user = userEvent.setup();
    renderPage();
    await waitFor(() => screen.getByText('Power BI Dashboard'));
    await user.click(screen.getByRole('button', { name: /New token/i }));
    await user.type(screen.getByLabelText('Name'), 'Ops Dashboard');
    await user.click(screen.getByRole('combobox'));
    await user.click(screen.getByRole('option', { name: 'Custom' }));

    expect(screen.getByRole('button', { name: 'Create token' })).toBeDisabled();
    expect(createReportingToken).not.toHaveBeenCalled();
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
