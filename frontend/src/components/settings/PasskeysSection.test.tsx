import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { PasskeysSection } from './PasskeysSection';
import { createTestQueryWrapper } from '@foundation/src/test-utils';

vi.mock('@foundation/src/lib/api/security-api', () => ({
  getPasskeys: vi.fn(),
  getMfaStatus: vi.fn(),
  renamePasskey: vi.fn(),
  removePasskey: vi.fn(),
}));

vi.mock('@foundation/src/lib/utils/tenant-navigation', () => ({
  buildBffLoginUrl: vi.fn(() => 'https://api.example/api/auth/bff/login?x'),
}));

const { getPasskeys, getMfaStatus, renamePasskey, removePasskey } = await import(
  '@foundation/src/lib/api/security-api'
);
const { buildBffLoginUrl } = await import('@foundation/src/lib/utils/tenant-navigation');

function renderPasskeys(locked = false) {
  return render(<PasskeysSection locked={locked} />, { wrapper: createTestQueryWrapper() });
}

const laptop = { id: 'pk-1', label: 'Work laptop', createdDate: new Date().toISOString() };

describe('PasskeysSection', () => {
  beforeEach(() => {
    vi.mocked(getMfaStatus).mockResolvedValue({ totpEnabled: false, recoveryCodesConfigured: false });
  });

  it('shows the empty state and the add button', async () => {
    vi.mocked(getPasskeys).mockResolvedValue([]);
    renderPasskeys();
    expect(await screen.findByText(/No passkeys yet/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Add a passkey/ })).toBeInTheDocument();
  });

  it('lists passkeys with their label, falling back to "Passkey"', async () => {
    vi.mocked(getPasskeys).mockResolvedValue([laptop, { id: 'pk-2', label: null, createdDate: null }]);
    renderPasskeys();
    expect(await screen.findByText('Work laptop')).toBeInTheDocument();
    expect(screen.getByText('Passkey')).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: /Remove/ })).toHaveLength(2);
  });

  it('shows the locked notice and no actions on the shared demo account', () => {
    vi.mocked(getPasskeys).mockResolvedValue([laptop]);
    renderPasskeys(true);
    expect(screen.getByText(/disabled for the shared demo account/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Add a passkey/ })).not.toBeInTheDocument();
  });

  it('starts Keycloak passkey registration and comes back to the security tab', async () => {
    vi.mocked(getPasskeys).mockResolvedValue([]);
    const replace = vi.fn();
    // Only the two members startAddPasskey reads; the rest of Location is irrelevant here.
    vi.spyOn(window, 'location', 'get').mockReturnValue({
      origin: 'https://acme.orkyo.com',
      replace,
    } as unknown as Location);
    renderPasskeys();

    fireEvent.click(await screen.findByRole('button', { name: /Add a passkey/ }));

    expect(buildBffLoginUrl).toHaveBeenCalledWith({
      returnTo: 'https://acme.orkyo.com/account?tab=security',
      kcAction: 'webauthn-register-passwordless',
    });
    expect(replace).toHaveBeenCalledWith('https://api.example/api/auth/bff/login?x');
    vi.restoreAllMocks();
  });

  it('renames a passkey with the trimmed name', async () => {
    vi.mocked(getPasskeys).mockResolvedValue([laptop]);
    vi.mocked(renamePasskey).mockResolvedValue(undefined);
    renderPasskeys();

    fireEvent.click(await screen.findByRole('button', { name: 'Rename Work laptop' }));
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: '  Phone  ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => {
      expect(vi.mocked(renamePasskey).mock.calls[0][0]).toEqual({ id: 'pk-1', label: 'Phone' });
    });
  });

  it('removes a passkey with the password only when the user has no TOTP', async () => {
    vi.mocked(getPasskeys).mockResolvedValue([laptop]);
    vi.mocked(removePasskey).mockResolvedValue(undefined);
    renderPasskeys();

    fireEvent.click(await screen.findByRole('button', { name: /Remove/ }));
    expect(await screen.findByText('Remove this passkey?')).toBeInTheDocument();
    expect(screen.queryByLabelText('Current Authenticator Code')).not.toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Current Password'), { target: { value: 'secret' } });
    fireEvent.click(screen.getByRole('button', { name: 'Remove passkey' }));

    await waitFor(() => {
      expect(vi.mocked(removePasskey).mock.calls[0][0]).toEqual({
        id: 'pk-1',
        currentPassword: 'secret',
        currentCode: undefined,
      });
    });
  });

  it('asks a TOTP user for the code too, and shows a failed removal inline', async () => {
    vi.mocked(getMfaStatus).mockResolvedValue({ totpEnabled: true, recoveryCodesConfigured: false });
    vi.mocked(getPasskeys).mockResolvedValue([laptop]);
    vi.mocked(removePasskey).mockRejectedValue(new Error('Current password is incorrect'));
    renderPasskeys();

    fireEvent.click(await screen.findByRole('button', { name: /Remove/ }));
    fireEvent.change(await screen.findByLabelText('Current Password'), { target: { value: 'secret' } });
    const confirm = screen.getByRole('button', { name: 'Remove passkey' });
    expect(confirm).toBeDisabled();

    fireEvent.change(await screen.findByLabelText('Current Authenticator Code'), { target: { value: '123456' } });
    fireEvent.click(confirm);

    expect(await screen.findByText('Current password is incorrect')).toBeInTheDocument();
    expect(vi.mocked(removePasskey).mock.calls[0][0]).toMatchObject({ currentCode: '123456' });
  });
});
