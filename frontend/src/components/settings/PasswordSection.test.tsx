import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { PasswordSection } from './PasswordSection';
import { createTestQueryWrapper } from '@foundation/src/test-utils';

vi.mock('@foundation/src/lib/api/security-api', () => ({
  changePassword: vi.fn(),
  getMfaStatus: vi.fn(() => Promise.resolve({ totpEnabled: false, recoveryCodesConfigured: false })),
}));

const { changePassword, getMfaStatus } = await import('@foundation/src/lib/api/security-api');

const defaultProps = {
  isFederated: false,
  identityProvider: null as string | null,
};

function renderPassword(props: Partial<typeof defaultProps> = {}) {
  const wrapper = createTestQueryWrapper();
  return render(<PasswordSection {...defaultProps} {...props} />, { wrapper });
}

describe('PasswordSection', () => {
  it('renders card title', () => {
    renderPassword();
    expect(screen.getByText('Password')).toBeInTheDocument();
  });

  it('shows Change Password button for non-federated users', () => {
    renderPassword();
    expect(screen.getByRole('button', { name: 'Change Password' })).toBeInTheDocument();
  });

  it('shows federated message for federated users', () => {
    renderPassword({ isFederated: true, identityProvider: 'Google' });
    expect(screen.getByText(/managed by Google/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Change Password' })).not.toBeInTheDocument();
  });

  it('opens change password dialog on button click', () => {
    renderPassword();
    fireEvent.click(screen.getByRole('button', { name: 'Change Password' }));
    expect(screen.getByText('Change Password', { selector: '[class*="DialogTitle"], h2' })).toBeInTheDocument();
    expect(screen.getByLabelText('Current Password')).toBeInTheDocument();
    expect(screen.getByLabelText('New Password')).toBeInTheDocument();
    expect(screen.getByLabelText('Confirm New Password')).toBeInTheDocument();
  });

  it('shows error when passwords do not match', async () => {
    renderPassword();
    fireEvent.click(screen.getByRole('button', { name: 'Change Password' }));

    fireEvent.change(screen.getByLabelText('Current Password'), { target: { value: 'oldpass1' } });
    fireEvent.change(screen.getByLabelText('New Password'), { target: { value: 'newpass12' } });
    fireEvent.change(screen.getByLabelText('Confirm New Password'), { target: { value: 'different' } });
    fireEvent.submit(screen.getByLabelText('Current Password').closest('form')!);

    await waitFor(() => {
      expect(screen.getByText('Passwords do not match')).toBeInTheDocument();
    });
    expect(changePassword).not.toHaveBeenCalled();
  });

  it('shows error when password too short', async () => {
    renderPassword();
    fireEvent.click(screen.getByRole('button', { name: 'Change Password' }));

    fireEvent.change(screen.getByLabelText('Current Password'), { target: { value: 'oldpass1' } });
    fireEvent.change(screen.getByLabelText('New Password'), { target: { value: 'short' } });
    fireEvent.change(screen.getByLabelText('Confirm New Password'), { target: { value: 'short' } });
    fireEvent.submit(screen.getByLabelText('Current Password').closest('form')!);

    await waitFor(() => {
      expect(screen.getByText('Password must be at least 8 characters')).toBeInTheDocument();
    });
  });

  it('calls changePassword API on valid submission', async () => {
    vi.mocked(changePassword).mockResolvedValue({ message: 'ok' } as never);
    renderPassword();
    fireEvent.click(screen.getByRole('button', { name: 'Change Password' }));

    fireEvent.change(screen.getByLabelText('Current Password'), { target: { value: 'oldpass1' } });
    fireEvent.change(screen.getByLabelText('New Password'), { target: { value: 'newpass12' } });
    fireEvent.change(screen.getByLabelText('Confirm New Password'), { target: { value: 'newpass12' } });
    fireEvent.submit(screen.getByLabelText('Current Password').closest('form')!);

    await waitFor(() => {
      expect(changePassword).toHaveBeenCalledWith(
        expect.objectContaining({
          currentPassword: 'oldpass1',
          newPassword: 'newpass12',
          confirmPassword: 'newpass12',
        }),
        expect.anything(),
      );
    });
  });

  it('asks a TOTP user for the code and sends it', async () => {
    vi.mocked(getMfaStatus).mockResolvedValueOnce({ totpEnabled: true, recoveryCodesConfigured: false });
    vi.mocked(changePassword).mockResolvedValue({ message: 'ok' } as never);
    renderPassword();
    fireEvent.click(screen.getByRole('button', { name: 'Change Password' }));

    fireEvent.change(screen.getByLabelText('Current Password'), { target: { value: 'oldpass1' } });
    fireEvent.change(screen.getByLabelText('New Password'), { target: { value: 'newpass12' } });
    fireEvent.change(screen.getByLabelText('Confirm New Password'), { target: { value: 'newpass12' } });
    fireEvent.change(await screen.findByLabelText('Current Authenticator Code'), { target: { value: '12a3456' } });
    fireEvent.submit(screen.getByLabelText('Current Password').closest('form')!);

    await waitFor(() => {
      expect(changePassword).toHaveBeenCalledWith(
        expect.objectContaining({ currentPassword: 'oldpass1', currentCode: '123456' }),
        expect.anything(),
      );
    });
  });

  it('refuses a TOTP user without a full code', async () => {
    vi.mocked(changePassword).mockClear();
    vi.mocked(getMfaStatus).mockResolvedValueOnce({ totpEnabled: true, recoveryCodesConfigured: false });
    renderPassword();
    fireEvent.click(screen.getByRole('button', { name: 'Change Password' }));

    fireEvent.change(screen.getByLabelText('Current Password'), { target: { value: 'oldpass1' } });
    fireEvent.change(screen.getByLabelText('New Password'), { target: { value: 'newpass12' } });
    fireEvent.change(screen.getByLabelText('Confirm New Password'), { target: { value: 'newpass12' } });
    fireEvent.change(await screen.findByLabelText('Current Authenticator Code'), { target: { value: '123' } });
    fireEvent.submit(screen.getByLabelText('Current Password').closest('form')!);

    expect(await screen.findByText('Enter the 6-digit code from your authenticator app')).toBeInTheDocument();
    expect(changePassword).not.toHaveBeenCalled();
  });

  it('shows no code field and sends no code without TOTP', async () => {
    vi.mocked(changePassword).mockResolvedValue({ message: 'ok' } as never);
    renderPassword();
    fireEvent.click(screen.getByRole('button', { name: 'Change Password' }));

    fireEvent.change(screen.getByLabelText('Current Password'), { target: { value: 'oldpass1' } });
    fireEvent.change(screen.getByLabelText('New Password'), { target: { value: 'newpass12' } });
    fireEvent.change(screen.getByLabelText('Confirm New Password'), { target: { value: 'newpass12' } });
    fireEvent.submit(screen.getByLabelText('Current Password').closest('form')!);

    await waitFor(() => expect(changePassword).toHaveBeenCalled());
    expect(screen.queryByLabelText('Current Authenticator Code')).not.toBeInTheDocument();
    expect(vi.mocked(changePassword).mock.calls.at(-1)![0]).toMatchObject({ currentCode: undefined });
  });

  it('shows success message on successful change', async () => {
    vi.mocked(changePassword).mockResolvedValue({ message: 'ok' } as never);
    renderPassword();
    fireEvent.click(screen.getByRole('button', { name: 'Change Password' }));

    fireEvent.change(screen.getByLabelText('Current Password'), { target: { value: 'oldpass1' } });
    fireEvent.change(screen.getByLabelText('New Password'), { target: { value: 'newpass12' } });
    fireEvent.change(screen.getByLabelText('Confirm New Password'), { target: { value: 'newpass12' } });
    fireEvent.submit(screen.getByLabelText('Current Password').closest('form')!);

    await waitFor(() => {
      expect(screen.getByText('Password changed')).toBeInTheDocument();
    });
  });

  it('shows identity provider name in federated description', () => {
    renderPassword({ isFederated: true, identityProvider: 'Azure AD' });
    expect(screen.getByText(/managed by Azure AD/)).toBeInTheDocument();
    // No "Go to" button: the API knows the provider's name but no URL for it, and the
    // old button linked to href="#" — a new tab to nowhere.
    expect(screen.queryByRole('link', { name: /go to/i })).not.toBeInTheDocument();
  });

  it('closes dialog on cancel', () => {
    renderPassword();
    fireEvent.click(screen.getByRole('button', { name: 'Change Password' }));
    expect(screen.getByLabelText('Current Password')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByLabelText('Current Password')).not.toBeInTheDocument();
  });
});
