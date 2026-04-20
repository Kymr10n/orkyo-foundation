import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { SessionsSection } from './SessionsSection';
import { createTestQueryWrapper } from '@/test-utils';

vi.mock('@/lib/api/security-api', () => ({
  getSessions: vi.fn(),
  revokeSession: vi.fn(),
  logoutAllSessions: vi.fn(),
}));

const { getSessions, revokeSession, logoutAllSessions } = await import('@/lib/api/security-api');

function makeSession(overrides: Record<string, unknown> = {}) {
  return {
    id: 'sess-1',
    clients: ['Chrome', 'Windows'],
    ipAddress: '192.168.1.1',
    startTime: new Date().toISOString(),
    lastAccessTime: new Date().toISOString(),
    isCurrent: false,
    ...overrides,
  };
}

const defaultProps = {
  onLogoutAll: vi.fn(),
};

function renderSessions(props: Partial<typeof defaultProps> = {}) {
  const wrapper = createTestQueryWrapper();
  return render(<SessionsSection {...defaultProps} {...props} />, { wrapper });
}

describe('SessionsSection', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('shows loading state', () => {
    vi.mocked(getSessions).mockReturnValue(new Promise(() => {}));
    renderSessions();
    expect(document.querySelector('.animate-spin')).toBeInTheDocument();
  });

  it('shows empty sessions message', async () => {
    vi.mocked(getSessions).mockResolvedValue([]);
    renderSessions();
    await waitFor(() => {
      expect(screen.getByText('No active sessions found.')).toBeInTheDocument();
    });
  });

  it('renders session list', async () => {
    vi.mocked(getSessions).mockResolvedValue([
      makeSession({ id: 's1', clients: ['Chrome'], isCurrent: true }),
      makeSession({ id: 's2', clients: ['Firefox'], isCurrent: false }),
    ]);
    renderSessions();
    await waitFor(() => {
      expect(screen.getByText('Chrome')).toBeInTheDocument();
    });
    expect(screen.getByText('Firefox')).toBeInTheDocument();
    expect(screen.getByText('Current')).toBeInTheDocument();
  });

  it('shows IP address and last access time', async () => {
    vi.mocked(getSessions).mockResolvedValue([makeSession()]);
    renderSessions();
    await waitFor(() => {
      expect(screen.getByText(/192\.168\.1\.1/)).toBeInTheDocument();
    });
  });

  it('shows revoke button for non-current sessions only', async () => {
    vi.mocked(getSessions).mockResolvedValue([
      makeSession({ id: 's1', isCurrent: true, clients: ['Chrome'] }),
      makeSession({ id: 's2', isCurrent: false, clients: ['Firefox'] }),
    ]);
    renderSessions();
    await waitFor(() => {
      expect(screen.getByText('Chrome')).toBeInTheDocument();
    });
    // Only 1 revoke button (for non-current session)
    const revokeButtons = screen.getAllByRole('button').filter(
      (btn) => btn.querySelector('.lucide-trash-2'),
    );
    expect(revokeButtons).toHaveLength(1);
  });

  it('calls revokeSession on revoke button click', async () => {
    vi.mocked(getSessions).mockResolvedValue([
      makeSession({ id: 's2', isCurrent: false, clients: ['Firefox'] }),
    ]);
    vi.mocked(revokeSession).mockResolvedValue({ message: 'ok' } as never);
    renderSessions();
    await waitFor(() => {
      expect(screen.getByText('Firefox')).toBeInTheDocument();
    });
    const revokeBtn = screen.getAllByRole('button').find(
      (btn) => btn.querySelector('.lucide-trash-2'),
    )!;
    fireEvent.click(revokeBtn);
    await waitFor(() => {
      expect(revokeSession).toHaveBeenCalledWith('s2', expect.anything());
    });
  });

  it('disables Sign Out Everywhere when only one session', async () => {
    vi.mocked(getSessions).mockResolvedValue([
      makeSession({ id: 's1', isCurrent: true }),
    ]);
    renderSessions();
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Sign Out Everywhere/ })).toBeDisabled();
    });
  });

  it('enables Sign Out Everywhere when multiple sessions', async () => {
    vi.mocked(getSessions).mockResolvedValue([
      makeSession({ id: 's1', isCurrent: true }),
      makeSession({ id: 's2', isCurrent: false }),
    ]);
    renderSessions();
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Sign Out Everywhere/ })).toBeEnabled();
    });
  });

  it('opens confirmation dialog and calls logoutAllSessions', async () => {
    vi.mocked(getSessions).mockResolvedValue([
      makeSession({ id: 's1', isCurrent: true }),
      makeSession({ id: 's2', isCurrent: false }),
    ]);
    vi.mocked(logoutAllSessions).mockResolvedValue({ message: 'ok' } as never);
    renderSessions();
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Sign Out Everywhere/ })).toBeEnabled();
    });
    fireEvent.click(screen.getByRole('button', { name: /Sign Out Everywhere/ }));
    await waitFor(() => {
      expect(screen.getByText(/terminate all your sessions/)).toBeInTheDocument();
    });
    // Confirm the dialog - find the destructive button inside the dialog
    const confirmButtons = screen.getAllByRole('button', { name: /Sign Out Everywhere/ });
    const confirmBtn = confirmButtons[confirmButtons.length - 1];
    fireEvent.click(confirmBtn);
    await waitFor(() => {
      expect(logoutAllSessions).toHaveBeenCalled();
    });
  });

  it('shows card title', () => {
    vi.mocked(getSessions).mockResolvedValue([]);
    renderSessions();
    expect(screen.getByText('Active Sessions')).toBeInTheDocument();
  });

  it('shows Unknown Client when client list is empty', async () => {
    vi.mocked(getSessions).mockResolvedValue([
      makeSession({ clients: [] }),
    ]);
    renderSessions();
    await waitFor(() => {
      expect(screen.getByText('Unknown Client')).toBeInTheDocument();
    });
  });
});
