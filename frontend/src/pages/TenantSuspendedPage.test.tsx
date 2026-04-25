import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';

// Mock useAuth
const mockSend = vi.fn();
const mockAuthState = {
  membership: null as Record<string, unknown> | null,
  send: mockSend,
};

vi.mock('@foundation/src/contexts/AuthContext', () => ({
  useAuth: () => mockAuthState,
}));

vi.mock('@foundation/src/lib/core/api-utils', () => ({
  API_BASE_URL: 'http://localhost:5000',
  getApiHeaders: (method: string) => ({
    'Content-Type': 'application/json',
    'X-Correlation-ID': 'test-correlation-id',
    'X-Tenant-Slug': 'test-tenant',
    ...(method === 'POST' ? { 'X-CSRF-Token': 'test-csrf' } : {}),
  }),
}));

import { TenantSuspendedPage } from '@foundation/src/pages/TenantSuspendedPage';

describe('TenantSuspendedPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockAuthState.membership = null;
    mockAuthState.send = mockSend;
  });

  it('renders workspace suspended heading', () => {
    mockAuthState.membership = {
      state: 'suspended',
      suspensionReason: 'inactivity',
      canReactivate: true,
    };
    render(<TenantSuspendedPage />);
    expect(screen.getByText('Workspace suspended')).toBeInTheDocument();
  });

  it('shows inactivity message when reason is inactivity', () => {
    mockAuthState.membership = {
      state: 'suspended',
      suspensionReason: 'inactivity',
      canReactivate: true,
    };
    render(<TenantSuspendedPage />);
    expect(screen.getByText(/automatically suspended due to 30 days of inactivity/)).toBeInTheDocument();
  });

  it('shows generic message when reason is not inactivity', () => {
    mockAuthState.membership = {
      state: 'suspended',
      suspensionReason: 'manual_admin',
      canReactivate: false,
    };
    render(<TenantSuspendedPage />);
    expect(screen.getByText(/Please contact support/)).toBeInTheDocument();
  });

  it('shows reactivate button when canReactivate is true', () => {
    mockAuthState.membership = {
      state: 'suspended',
      suspensionReason: 'inactivity',
      canReactivate: true,
    };
    render(<TenantSuspendedPage />);
    expect(screen.getByRole('button', { name: /reactivate workspace/i })).toBeInTheDocument();
  });

  it('does not show reactivate button when canReactivate is false', () => {
    mockAuthState.membership = {
      state: 'suspended',
      suspensionReason: 'manual_admin',
      canReactivate: false,
    };
    render(<TenantSuspendedPage />);
    expect(screen.queryByRole('button', { name: /reactivate workspace/i })).not.toBeInTheDocument();
  });

  it('shows contact support when canReactivate is false', () => {
    mockAuthState.membership = {
      state: 'suspended',
      suspensionReason: 'manual_admin',
      canReactivate: false,
    };
    render(<TenantSuspendedPage />);
    expect(screen.getByRole('link', { name: /contact support/i })).toBeInTheDocument();
  });

  it('always shows sign out button', () => {
    mockAuthState.membership = {
      state: 'suspended',
      suspensionReason: 'inactivity',
      canReactivate: true,
    };
    render(<TenantSuspendedPage />);
    expect(screen.getByRole('button', { name: /sign out/i })).toBeInTheDocument();
  });

  it('sends LOGOUT event on sign out click', () => {
    mockAuthState.membership = {
      state: 'suspended',
      suspensionReason: 'inactivity',
      canReactivate: true,
    };
    render(<TenantSuspendedPage />);
    fireEvent.click(screen.getByRole('button', { name: /sign out/i }));
    expect(mockSend).toHaveBeenCalledWith({ type: 'LOGOUT' });
  });

  it('calls reactivation API and sends REACTIVATE on success', async () => {
    mockAuthState.membership = {
      state: 'suspended',
      suspensionReason: 'inactivity',
      canReactivate: true,
    };

    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(
      new Response(JSON.stringify({ success: true }), { status: 200 })
    );

    render(<TenantSuspendedPage />);
    fireEvent.click(screen.getByRole('button', { name: /reactivate workspace/i }));

    await waitFor(() => {
      expect(fetchSpy).toHaveBeenCalledWith(
        'http://localhost:5000/api/tenant/reactivate',
        expect.objectContaining({
          method: 'POST',
          credentials: 'include',
          headers: expect.objectContaining({
            'X-Tenant-Slug': 'test-tenant',
            'X-CSRF-Token': 'test-csrf',
          }),
        })
      );
    });

    await waitFor(() => {
      expect(mockSend).toHaveBeenCalledWith({ type: 'REACTIVATE' });
    });

    fetchSpy.mockRestore();
  });

  it('shows error message when reactivation fails', async () => {
    mockAuthState.membership = {
      state: 'suspended',
      suspensionReason: 'inactivity',
      canReactivate: true,
    };

    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(
      new Response(JSON.stringify({ message: 'Not allowed' }), { status: 403 })
    );

    render(<TenantSuspendedPage />);
    fireEvent.click(screen.getByRole('button', { name: /reactivate workspace/i }));

    await waitFor(() => {
      expect(screen.getByText('Not allowed')).toBeInTheDocument();
    });

    expect(mockSend).not.toHaveBeenCalledWith({ type: 'REACTIVATE' });

    fetchSpy.mockRestore();
  });

  it('shows network error on fetch failure', async () => {
    mockAuthState.membership = {
      state: 'suspended',
      suspensionReason: 'inactivity',
      canReactivate: true,
    };

    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockRejectedValueOnce(new Error('Network error'));

    render(<TenantSuspendedPage />);
    fireEvent.click(screen.getByRole('button', { name: /reactivate workspace/i }));

    await waitFor(() => {
      expect(screen.getByText(/Network error/)).toBeInTheDocument();
    });

    fetchSpy.mockRestore();
  });
});
