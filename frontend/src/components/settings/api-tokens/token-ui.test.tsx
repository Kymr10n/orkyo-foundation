import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import {
  CopyButton,
  ExpiryFields,
  tokenStatus,
  resolveExpiry,
  formatDate,
  getPresetExpiry,
  fromDateOnly,
  toDateOnly,
  type ExpiryMode,
  type TokenSummaryLike,
} from './token-ui';

vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
import { toast } from 'sonner';

const baseToken: TokenSummaryLike = {
  id: 't1',
  name: 'Agent',
  tokenPrefix: 'abc123',
  createdAtUtc: '2026-01-01T00:00:00Z',
  lastUsedAtUtc: null,
  expiresAtUtc: null,
  revokedAtUtc: null,
  isActive: true,
};

beforeEach(() => vi.clearAllMocks());

describe('tokenStatus', () => {
  it('reports a revoked token as revoked even if it had not expired', () => {
    expect(tokenStatus({ ...baseToken, revokedAtUtc: '2026-02-01T00:00:00Z' })).toBe('revoked');
  });

  it('reports a past expiry as expired', () => {
    expect(tokenStatus({ ...baseToken, expiresAtUtc: '2020-01-01T00:00:00Z' })).toBe('expired');
  });

  it('reports a future expiry as still active', () => {
    expect(tokenStatus({ ...baseToken, expiresAtUtc: '2099-01-01T00:00:00Z' })).toBe('active');
  });

  it('treats revocation as decisive over expiry', () => {
    // Both set: the token was revoked before it lapsed, and that is the more useful fact.
    expect(
      tokenStatus({
        ...baseToken,
        revokedAtUtc: '2026-02-01T00:00:00Z',
        expiresAtUtc: '2020-01-01T00:00:00Z',
      }),
    ).toBe('revoked');
  });
});

describe('resolveExpiry', () => {
  it('returns an empty string for "no expiration", which the caller omits from the request', () => {
    expect(resolveExpiry('none', '')).toBe('');
  });

  it('passes the custom date straight through', () => {
    expect(resolveExpiry('custom', '2026-12-24')).toBe('2026-12-24');
  });

  it.each(['7', '30', '60', '90'] as ExpiryMode[])('resolves the %s-day preset to a date', (mode) => {
    expect(resolveExpiry(mode, '')).toBe(getPresetExpiry(Number(mode)));
  });
});

describe('date helpers', () => {
  it('round-trips a local date without shifting across a timezone boundary', () => {
    // toDateOnly uses local parts on purpose: an ISO conversion can land on the previous day
    // for anyone west of UTC, silently offering an expiry a day early.
    const date = new Date(2026, 11, 24);
    expect(fromDateOnly(toDateOnly(date))?.getDate()).toBe(24);
  });

  it('rejects a malformed date string', () => {
    expect(fromDateOnly('not-a-date')).toBeUndefined();
  });

  it('shows an em dash rather than "Invalid Date" for a token never used', () => {
    expect(formatDate(null)).toBe('—');
  });
});

/** navigator.clipboard is getter-only in jsdom, so it has to be redefined rather than assigned. */
function setClipboard(clipboard: { writeText: (t: string) => Promise<void> } | undefined) {
  Object.defineProperty(navigator, 'clipboard', {
    value: clipboard,
    configurable: true,
    writable: true,
  });
}

describe('CopyButton', () => {
  it('copies and confirms', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    setClipboard({ writeText });

    render(<CopyButton text="orkyo_api_secret" />);
    await userEvent.click(screen.getByRole('button', { name: /Copy/ }));

    expect(writeText).toHaveBeenCalledWith('orkyo_api_secret');
    expect(await screen.findByText('Copied')).toBeInTheDocument();
  });

  it('says so instead of failing silently when the clipboard is unavailable', async () => {
    // Community self-hosts may be reached over plain HTTP on a LAN, where the clipboard API is
    // not exposed. The token is still on screen, so the user can copy it by hand.
    setClipboard(undefined);

    render(<CopyButton text="orkyo_api_secret" />);
    await userEvent.click(screen.getByRole('button', { name: /Copy/ }));

    expect(toast.error).toHaveBeenCalledWith(expect.stringContaining('Clipboard unavailable'));
  });
});

function ExpiryHarness() {
  const [mode, setMode] = useState<ExpiryMode>('7');
  const [custom, setCustom] = useState('');
  return (
    <>
      <ExpiryFields
        mode={mode}
        onModeChange={setMode}
        customExpiresAt={custom}
        onCustomChange={setCustom}
      />
      <output data-testid="resolved">{resolveExpiry(mode, custom)}</output>
    </>
  );
}

describe('ExpiryFields', () => {
  it('offers a date picker only once "Custom" is chosen', async () => {
    render(<ExpiryHarness />);

    expect(screen.queryByLabelText(/Select date/)).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('combobox'));
    await userEvent.click(await screen.findByRole('option', { name: 'Custom' }));

    expect(await screen.findByLabelText(/Select date/)).toBeInTheDocument();
  });

  it('resolves to no expiry when "No expiration" is chosen', async () => {
    render(<ExpiryHarness />);

    await userEvent.click(screen.getByRole('combobox'));
    await userEvent.click(await screen.findByRole('option', { name: 'No expiration' }));

    await waitFor(() => expect(screen.getByTestId('resolved')).toHaveTextContent(''));
    expect(screen.getByText(/will not expire automatically/)).toBeInTheDocument();
  });

  it('explains that a dated token expires on that date', async () => {
    render(<ExpiryHarness />);

    expect(screen.getByText(/will expire on the selected date/)).toBeInTheDocument();
  });
});
