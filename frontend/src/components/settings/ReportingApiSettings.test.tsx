import { describe, it, expect, vi, beforeEach } from 'vitest';
import type { ComponentProps } from 'react';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ReportingApiSettings } from './ReportingApiSettings';
import { toast } from 'sonner';
import type { ReportingTokenSummary } from '@foundation/src/lib/api/reporting-tokens-api';

vi.mock('@foundation/src/lib/api/reporting-tokens-api', () => ({
  listReportingTokens: vi.fn(),
  createReportingToken: vi.fn(),
  revokeReportingToken: vi.fn(),
}));

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

// Entitlement gate: ReportingApiSettings requires the server-reported api_access_enabled
// entitlement. Mock useAuth so tests control it; default to entitled so the page renders.
const { authState, entitled, notEntitled } = vi.hoisted(() => {
  const entitled = { entitlements: { api_access_enabled: true } };
  const notEntitled = { entitlements: { api_access_enabled: false } };
  return {
    entitled,
    notEntitled,
    authState: {
      membership: entitled as { entitlements: Record<string, boolean> } | null,
      isLoading: false,
    },
  };
});
vi.mock('@foundation/src/contexts/AuthContext', () => ({
  useAuth: () => ({ membership: authState.membership, isLoading: authState.isLoading, isSiteAdmin: false }),
}));

import {
  listReportingTokens,
  createReportingToken,
  revokeReportingToken,
  type CreatedReportingToken,
} from '@foundation/src/lib/api/reporting-tokens-api';
import { renderWithQuery } from '@foundation/src/test-utils';
import { formatDateForInput } from '@foundation/src/lib/utils';

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

function renderPage(props?: ComponentProps<typeof ReportingApiSettings>) {
  // Production-identical feedback MutationCache (dialog-feedback.md).
  return renderWithQuery(<ReportingApiSettings {...props} />, { router: true, feedback: true });
}

function addLocalDays(date: Date, days: number): Date {
  const next = new Date(date);
  next.setDate(next.getDate() + days);
  return next;
}

function expectedPresetExpiry(days: number): string {
  return formatDateForInput(addLocalDays(new Date(), days));
}

function expectedPresetLabel(days: number): string {
  return addLocalDays(new Date(), days).toLocaleDateString(undefined, {
    month: 'short',
    day: '2-digit',
    year: 'numeric',
  });
}

describe('ReportingApiSettings', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    authState.membership = entitled;
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

  it('shows generic unavailable message for tenants without API access', async () => {
    authState.membership = notEntitled;
    renderPage();

    await waitFor(() => {
      expect(
        screen.getByText('Reporting API access is not available for this workspace.'),
      ).toBeInTheDocument();
    });
    expect(listReportingTokens).not.toHaveBeenCalled();
  });

  it('shows an upsell with a plans link (no redirect) when an upgrade href is provided', async () => {
    authState.membership = notEntitled;
    renderWithQuery(<ReportingApiSettings upgradeHref="/account?tab=upgrade" />, {
      router: '/settings/integrations',
    });

    // Upsell renders in place — the user is not silently redirected, and the generic
    // "not available" notice is not used when an upgrade path exists.
    await waitFor(() => {
      expect(screen.getByText(/Available on Professional and Enterprise plans/i)).toBeInTheDocument();
    });
    expect(
      screen.queryByText('Reporting API access is not available for this workspace.'),
    ).not.toBeInTheDocument();

    // CTA is a link to the provided href; navigation happens only on click.
    expect(screen.getByRole('link', { name: /view plans/i })).toHaveAttribute(
      'href',
      '/account?tab=upgrade',
    );

    expect(listReportingTokens).not.toHaveBeenCalled();
  });

  it('forwards unentitled tenants (token UI never renders)', async () => {
    authState.membership = notEntitled;
    renderPage();
    await waitFor(() => {
      expect(screen.queryByText('Reporting API')).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: /New token/i })).not.toBeInTheDocument();
    });
    // Unentitled tenants never hit the tokens API (query is gated/disabled)
    expect(listReportingTokens).not.toHaveBeenCalled();
  });

  it('renders whenever the entitlement is present', async () => {
    authState.membership = entitled;
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

  it('offers a retry that reloads the list after a failed load', async () => {
    vi.mocked(listReportingTokens).mockRejectedValueOnce(new Error('Unauthorized'));
    const user = userEvent.setup();
    renderPage();

    await user.click(await screen.findByRole('button', { name: 'Try again' }));

    expect(await screen.findByText('Power BI Dashboard')).toBeInTheDocument();
    expect(listReportingTokens).toHaveBeenCalledTimes(2);
  });

  it('keeps the create dialog open with the failure inline, not a toast', async () => {
    vi.mocked(createReportingToken).mockRejectedValueOnce(new Error('Token limit reached'));
    const user = userEvent.setup();
    renderPage();
    await waitFor(() => screen.getByText('Power BI Dashboard'));
    await user.click(screen.getByRole('button', { name: /New token/i }));
    await user.type(screen.getByLabelText('Name'), 'Ops Dashboard');
    await user.click(screen.getByRole('button', { name: 'Create token' }));

    expect(await screen.findByText('Token limit reached')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Create Reporting Token' })).toBeInTheDocument();
    expect(toast.error).not.toHaveBeenCalled();
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
