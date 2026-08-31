import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { PlatformApiSettings } from './PlatformApiSettings';
import type { ApiAccessTokenSummary } from '@foundation/src/lib/api/api-access-tokens-api';

vi.mock('@foundation/src/lib/api/api-access-tokens-api', async (importOriginal) => ({
  // grantsWrite / API_SCOPES are pure helpers the component relies on — keep the real ones so a
  // change to the scope vocabulary is caught here rather than mocked over.
  ...(await importOriginal<object>()),
  listApiAccessTokens: vi.fn(),
  createApiAccessToken: vi.fn(),
  revokeApiAccessToken: vi.fn(),
}));

vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

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
  useAuth: () => ({
    membership: authState.membership,
    isLoading: authState.isLoading,
    isSiteAdmin: false,
  }),
}));

import {
  listApiAccessTokens,
  createApiAccessToken,
  revokeApiAccessToken,
  API_SCOPES,
} from '@foundation/src/lib/api/api-access-tokens-api';

const readToken: ApiAccessTokenSummary = {
  id: 'tok-read',
  tenantId: 'tenant-1',
  name: 'Reporting bot',
  tokenPrefix: 'aaa111',
  scopes: API_SCOPES.scheduleRead,
  createdByUserId: 'user-1',
  isActive: true,
  createdAtUtc: '2026-01-01T00:00:00Z',
  lastUsedAtUtc: null,
  expiresAtUtc: null,
  revokedAtUtc: null,
};

const writeToken: ApiAccessTokenSummary = {
  ...readToken,
  id: 'tok-write',
  name: 'Planning assistant',
  tokenPrefix: 'bbb222',
  scopes: `${API_SCOPES.scheduleRead} ${API_SCOPES.scheduleWrite}`,
};

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <PlatformApiSettings upgradeHref="/plans" />
      </QueryClientProvider>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  authState.membership = entitled;
  authState.isLoading = false;
  vi.mocked(listApiAccessTokens).mockResolvedValue([]);
});

describe('PlatformApiSettings', () => {
  it('tells the reader at a glance which tokens can change the schedule', async () => {
    // The one thing this table has that the reporting one does not: a token that can write is
    // materially different from one that cannot, so it must be visible without opening anything.
    vi.mocked(listApiAccessTokens).mockResolvedValue([readToken, writeToken]);
    renderPage();

    const writeRow = await screen.findByText('Planning assistant');
    const readRow = await screen.findByText('Reporting bot');

    expect(within(writeRow.closest('tr')!).getByText('Read & write')).toBeInTheDocument();
    expect(within(readRow.closest('tr')!).getByText('Read only')).toBeInTheDocument();
  });

  it('defaults a new token to read-only', async () => {
    // Granting write should be a decision someone makes on purpose, not the path of least
    // resistance through the form.
    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: /New token/ }));

    expect(screen.getByRole('radio', { name: /Read only/ })).toBeChecked();
    expect(screen.getByRole('radio', { name: /Read and write/ })).not.toBeChecked();
  });

  it('warns about the blast radius only once write is actually selected', async () => {
    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: /New token/ }));

    expect(screen.queryByText(/mark resources unavailable/i)).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('radio', { name: /Read and write/ }));

    // Names the powers a write token actually carries, including the ones v2 added — the person
    // granting it should not have to infer "auto-schedule a whole site" from the word "write".
    const warning = await screen.findByText(/mark resources unavailable/i);
    expect(warning).toHaveTextContent(/create work/i);
    expect(warning).toHaveTextContent(/auto-schedule a whole site/i);
  });

  it('sends both scopes for a read-and-write token, because write implies read', async () => {
    vi.mocked(createApiAccessToken).mockResolvedValue({
      summary: writeToken,
      rawToken: 'orkyo_api_bbb222_secret',
    });
    renderPage();

    await userEvent.click(await screen.findByRole('button', { name: /New token/ }));
    await userEvent.type(screen.getByLabelText('Name'), 'Planning assistant');
    await userEvent.click(screen.getByRole('radio', { name: /Read and write/ }));
    await userEvent.click(screen.getByRole('button', { name: /Create token/ }));

    await waitFor(() =>
      expect(createApiAccessToken).toHaveBeenCalledWith(
        expect.objectContaining({
          name: 'Planning assistant',
          scopes: [API_SCOPES.scheduleRead, API_SCOPES.scheduleWrite],
        }),
      ),
    );
  });

  it('sends only the read scope for a read-only token', async () => {
    vi.mocked(createApiAccessToken).mockResolvedValue({
      summary: readToken,
      rawToken: 'orkyo_api_aaa111_secret',
    });
    renderPage();

    await userEvent.click(await screen.findByRole('button', { name: /New token/ }));
    await userEvent.type(screen.getByLabelText('Name'), 'Reporting bot');
    await userEvent.click(screen.getByRole('button', { name: /Create token/ }));

    await waitFor(() =>
      expect(createApiAccessToken).toHaveBeenCalledWith(
        expect.objectContaining({ scopes: [API_SCOPES.scheduleRead] }),
      ),
    );
  });

  it('shows the secret once, with what holding it allows', async () => {
    vi.mocked(createApiAccessToken).mockResolvedValue({
      summary: writeToken,
      rawToken: 'orkyo_api_bbb222_thesecret',
    });
    renderPage();

    await userEvent.click(await screen.findByRole('button', { name: /New token/ }));
    await userEvent.type(screen.getByLabelText('Name'), 'Planning assistant');
    await userEvent.click(screen.getByRole('button', { name: /Create token/ }));

    expect(await screen.findByText('orkyo_api_bbb222_thesecret')).toBeInTheDocument();
    expect(screen.getByText(/will not be shown again/i)).toBeInTheDocument();
  });

  it('will not submit a token with no name', async () => {
    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: /New token/ }));

    expect(screen.getByRole('button', { name: /Create token/ })).toBeDisabled();
  });

  it('revokes a token after confirmation', async () => {
    vi.mocked(listApiAccessTokens).mockResolvedValue([writeToken]);
    vi.mocked(revokeApiAccessToken).mockResolvedValue(undefined);
    renderPage();

    await userEvent.click(await screen.findByRole('button', { name: /Revoke Planning assistant/ }));
    await userEvent.click(await screen.findByRole('button', { name: /^Revoke$/ }));

    await waitFor(() => expect(revokeApiAccessToken).toHaveBeenCalledWith('tok-write'));
  });

  it('offers the MCP server URL to paste into a client', async () => {
    renderPage();

    expect(await screen.findByText(/\/api\/mcp$/)).toBeInTheDocument();
  });

  it('sells the feature rather than 404ing when the tenant is not entitled', async () => {
    authState.membership = notEntitled;
    renderPage();

    expect(await screen.findByText(/API & AI access/)).toBeInTheDocument();
    expect(listApiAccessTokens).not.toHaveBeenCalled();
  });
});
