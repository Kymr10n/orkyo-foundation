import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MfaSection } from './MfaSection';
import { createTestQueryWrapper } from '@/test-utils';

vi.mock('@/lib/api/security-api', () => ({
  getMfaStatus: vi.fn(),
  removeMfa: vi.fn(),
  enableMfa: vi.fn(),
}));

const { getMfaStatus, removeMfa, enableMfa } = await import('@/lib/api/security-api');

function renderMfa() {
  const wrapper = createTestQueryWrapper();
  return render(<MfaSection />, { wrapper });
}

describe('MfaSection', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('shows loading state', () => {
    vi.mocked(getMfaStatus).mockReturnValue(new Promise(() => {}));
    renderMfa();
    // Spinner should be present
    expect(document.querySelector('.animate-spin')).toBeInTheDocument();
  });

  it('shows MFA not configured state', async () => {
    vi.mocked(getMfaStatus).mockResolvedValue({ totpEnabled: false, recoveryCodesConfigured: false });
    renderMfa();
    await waitFor(() => {
      expect(screen.getByText('MFA not configured')).toBeInTheDocument();
    });
    expect(screen.getByRole('button', { name: /Enable MFA/ })).toBeInTheDocument();
  });

  it('shows MFA enabled state with remove button', async () => {
    vi.mocked(getMfaStatus).mockResolvedValue({
      totpEnabled: true,
      totpLabel: 'My Phone',
      totpCreatedDate: new Date().toISOString(),
      recoveryCodesConfigured: true,
    });
    renderMfa();
    await waitFor(() => {
      expect(screen.getByText('Enabled')).toBeInTheDocument();
    });
    expect(screen.getByText('My Phone', { exact: false })).toBeInTheDocument();
    expect(screen.getByText('Recovery codes configured')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Remove/ })).toBeInTheDocument();
  });

  it('opens remove MFA confirmation dialog', async () => {
    vi.mocked(getMfaStatus).mockResolvedValue({ totpEnabled: true, totpLabel: 'App', recoveryCodesConfigured: false });
    renderMfa();
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Remove/ })).toBeInTheDocument();
    });
    fireEvent.click(screen.getByRole('button', { name: /Remove/ }));
    await waitFor(() => {
      expect(screen.getByText('Remove Two-Factor Authentication?')).toBeInTheDocument();
    });
  });

  it('calls removeMfa when confirmed', async () => {
    vi.mocked(getMfaStatus).mockResolvedValue({ totpEnabled: true, totpLabel: 'App', recoveryCodesConfigured: false });
    vi.mocked(removeMfa).mockResolvedValue({ message: 'ok' } as never);
    renderMfa();
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Remove/ })).toBeInTheDocument();
    });
    fireEvent.click(screen.getByRole('button', { name: /Remove/ }));
    await waitFor(() => {
      expect(screen.getByText('Remove Two-Factor Authentication?')).toBeInTheDocument();
    });
    fireEvent.click(screen.getByRole('button', { name: /Remove MFA/ }));
    await waitFor(() => {
      expect(removeMfa).toHaveBeenCalled();
    });
  });

  it('enables MFA enrollment on button click', async () => {
    vi.mocked(getMfaStatus).mockResolvedValue({ totpEnabled: false, recoveryCodesConfigured: false });
    vi.mocked(enableMfa).mockResolvedValue({ message: 'ok' } as never);
    renderMfa();
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Enable MFA/ })).toBeInTheDocument();
    });
    fireEvent.click(screen.getByRole('button', { name: /Enable MFA/ }));
    await waitFor(() => {
      expect(enableMfa).toHaveBeenCalled();
    });
  });

  it('shows success message after enabling MFA', async () => {
    vi.mocked(getMfaStatus).mockResolvedValue({ totpEnabled: false, recoveryCodesConfigured: false });
    vi.mocked(enableMfa).mockResolvedValue({ message: 'ok' } as never);
    renderMfa();
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Enable MFA/ })).toBeInTheDocument();
    });
    fireEvent.click(screen.getByRole('button', { name: /Enable MFA/ }));
    await waitFor(() => {
      expect(screen.getByText(/MFA enrollment enabled/)).toBeInTheDocument();
    });
  });

  it('shows card title', async () => {
    vi.mocked(getMfaStatus).mockResolvedValue({ totpEnabled: false, recoveryCodesConfigured: false });
    renderMfa();
    expect(screen.getByText('Two-Factor Authentication (MFA)')).toBeInTheDocument();
  });

  it('shows error when enableMfa fails', async () => {
    vi.mocked(getMfaStatus).mockResolvedValue({ totpEnabled: false, recoveryCodesConfigured: false });
    vi.mocked(enableMfa).mockRejectedValue(new Error('Enable failed'));
    renderMfa();
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Enable MFA/ })).toBeInTheDocument();
    });
    fireEvent.click(screen.getByRole('button', { name: /Enable MFA/ }));
    await waitFor(() => {
      expect(screen.getByText(/Enable failed|failed/i)).toBeInTheDocument();
    });
  });
});
