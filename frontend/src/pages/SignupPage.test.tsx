import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { SignupPage } from './SignupPage';
import type * as ApiUtils from '@foundation/src/lib/core/api-utils';

// SignupPage builds the BFF login URL via the real `buildBffLoginUrl`
// (tenant-navigation) — left unmocked so the redirect assertions are faithful.

vi.mock('@foundation/src/lib/core/api-utils', async (importOriginal) => {
  const mod = await importOriginal<typeof ApiUtils>();
  return {
    ...mod,
    API_BASE_URL: 'http://localhost:5000',
  };
});

vi.mock('@foundation/src/lib/core/api-paths', () => ({
  API_PATHS: {
    INVITATION_VALIDATE: '/api/invitations/validate',
    INVITATION_ACCEPT: '/api/invitations/accept',
  },
}));

function renderSignup(search = '') {
  return render(
    <MemoryRouter
      initialEntries={[`/signup${search}`]}
    >
      <SignupPage />
    </MemoryRouter>,
  );
}

describe('SignupPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.stubGlobal('fetch', vi.fn());
    // Stub window.location so handleBackToLogin's full-page navigations are
    // assertable instead of triggering a jsdom "navigation not implemented" error.
    Object.defineProperty(window, 'location', {
      value: { href: '', origin: 'http://localhost:5173' },
      writable: true,
    });
  });

  it('shows error when no invitation token', async () => {
    renderSignup();
    await waitFor(() => {
      expect(screen.getByText(/no invitation token/i)).toBeInTheDocument();
    });
  });

  it('validates invitation token on mount', async () => {
    const mockFetch = vi.fn(() =>
      Promise.resolve({
        ok: true,
        json: () => Promise.resolve({ email: 'test@test.com', expiresAt: '2099-01-01', tenantName: 'Acme' }),
      }),
    );
    vi.stubGlobal('fetch', mockFetch);

    renderSignup('?invitation=valid-token');
    await waitFor(() => {
      expect(mockFetch).toHaveBeenCalledWith(
        expect.stringContaining('valid-token'),
        expect.anything(),
      );
    });
  });

  it('shows invitation details after validation', async () => {
    vi.stubGlobal('fetch', vi.fn(() =>
      Promise.resolve({
        ok: true,
        json: () => Promise.resolve({ email: 'user@acme.com', expiresAt: '2099-06-01T00:00:00Z', tenantName: 'Acme Corp' }),
      }),
    ));

    renderSignup('?invitation=valid-token');
    await waitFor(() => {
      expect(screen.getByText(/acme corp/i)).toBeInTheDocument();
      expect(screen.getByText('user@acme.com')).toBeInTheDocument();
    });
  });

  it('shows error for invalid invitation', async () => {
    vi.stubGlobal('fetch', vi.fn(() =>
      Promise.resolve({
        ok: false,
        status: 400,
        statusText: 'Bad Request',
        json: () => Promise.resolve({ error: 'Invitation expired' }),
        content: { readAsStringAsync: () => Promise.resolve('') },
      }),
    ));

    renderSignup('?invitation=expired-token');
    await waitFor(() => {
      // handleApiError wraps the body.error as "API Error (status): {message}"
      expect(screen.getByText(/Invitation expired/)).toBeInTheDocument();
    });
  });

  it('shows error when passwords do not match', async () => {
    const mockFetch = vi.fn().mockResolvedValueOnce({
      ok: true,
      json: () => Promise.resolve({ email: 'user@acme.com', expiresAt: '2099-06-01T00:00:00Z', tenantName: 'Acme' }),
    });
    vi.stubGlobal('fetch', mockFetch);
    renderSignup('?invitation=valid-token');
    await waitFor(() => expect(screen.getByText('user@acme.com')).toBeInTheDocument());

    fireEvent.change(screen.getByLabelText(/^password$/i), { target: { value: 'password123' } });
    fireEvent.change(screen.getByLabelText(/confirm password/i), { target: { value: 'different456' } });
    fireEvent.submit(screen.getByLabelText(/^password$/i).closest('form')!);

    expect(screen.getByText('Passwords do not match')).toBeInTheDocument();
  });

  it('shows error when password is too short', async () => {
    const mockFetch = vi.fn().mockResolvedValueOnce({
      ok: true,
      json: () => Promise.resolve({ email: 'user@acme.com', expiresAt: '2099-06-01T00:00:00Z', tenantName: 'Acme' }),
    });
    vi.stubGlobal('fetch', mockFetch);
    renderSignup('?invitation=valid-token');
    await waitFor(() => expect(screen.getByText('user@acme.com')).toBeInTheDocument());

    fireEvent.change(screen.getByLabelText(/^password$/i), { target: { value: 'short' } });
    fireEvent.change(screen.getByLabelText(/confirm password/i), { target: { value: 'short' } });
    fireEvent.submit(screen.getByLabelText(/^password$/i).closest('form')!);

    expect(screen.getByText('Password must be at least 8 characters')).toBeInTheDocument();
  });

  it('uses email username as displayName when left blank', async () => {
    const mockFetch = vi.fn()
      .mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve({ email: 'alice@acme.com', expiresAt: '2099-06-01T00:00:00Z', tenantName: 'Acme' }),
      })
      .mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve({ success: true }),
      });
    vi.stubGlobal('fetch', mockFetch);
    renderSignup('?invitation=valid-token');
    await waitFor(() => expect(screen.getByText('alice@acme.com')).toBeInTheDocument());

    fireEvent.change(screen.getByLabelText(/^password$/i), { target: { value: 'password123' } });
    fireEvent.change(screen.getByLabelText(/confirm password/i), { target: { value: 'password123' } });
    fireEvent.submit(screen.getByLabelText(/^password$/i).closest('form')!);

    await waitFor(() => expect(screen.getByText(/account created/i)).toBeInTheDocument());
    // Submitted without displayName → email username used as fallback
    expect(mockFetch).toHaveBeenCalledWith(
      expect.any(String),
      expect.objectContaining({
        body: expect.stringContaining('alice'),
      }),
    );
  });

  it('submits account creation form', async () => {
    const mockFetch = vi.fn()
      .mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve({ email: 'user@acme.com', expiresAt: '2099-06-01T00:00:00Z', tenantName: 'Acme' }),
      })
      .mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve({ success: true }),
      });
    vi.stubGlobal('fetch', mockFetch);

    renderSignup('?invitation=valid-token');

    await waitFor(() => {
      expect(screen.getByText('user@acme.com')).toBeInTheDocument();
    });

    fireEvent.change(screen.getByLabelText(/^password$/i), { target: { value: 'password123' } });
    fireEvent.change(screen.getByLabelText(/confirm password/i), { target: { value: 'password123' } });
    fireEvent.submit(screen.getByLabelText(/^password$/i).closest('form')!);

    await waitFor(() => {
      expect(screen.getByText(/account created/i)).toBeInTheDocument();
    });
  });

  it('post-create "Sign In" redirects to the BFF login with email pre-filled', async () => {
    const mockFetch = vi.fn()
      .mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve({ email: 'user@acme.com', expiresAt: '2099-06-01T00:00:00Z', tenantName: 'Acme' }),
      })
      .mockResolvedValueOnce({ ok: true, json: () => Promise.resolve({ success: true }) });
    vi.stubGlobal('fetch', mockFetch);

    renderSignup('?invitation=valid-token');
    await waitFor(() => expect(screen.getByText('user@acme.com')).toBeInTheDocument());

    fireEvent.change(screen.getByLabelText(/^password$/i), { target: { value: 'password123' } });
    fireEvent.change(screen.getByLabelText(/confirm password/i), { target: { value: 'password123' } });
    fireEvent.submit(screen.getByLabelText(/^password$/i).closest('form')!);

    await waitFor(() => expect(screen.getByText(/account created/i)).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: /sign in/i }));

    expect(window.location.href).toContain('/api/auth/bff/login?returnTo=');
    expect(window.location.href).toContain(encodeURIComponent('http://localhost:5173/login?auto=1'));
    expect(window.location.href).toContain('login_hint=user%40acme.com');
  });

  it('"Go to Sign In" on an invalid invitation goes to /login?auto=1 (no BFF, no hint)', async () => {
    vi.stubGlobal('fetch', vi.fn(() =>
      Promise.resolve({
        ok: false,
        status: 400,
        statusText: 'Bad Request',
        json: () => Promise.resolve({ error: 'Invitation expired' }),
        content: { readAsStringAsync: () => Promise.resolve('') },
      }),
    ));

    renderSignup('?invitation=expired-token');
    await waitFor(() => expect(screen.getByText(/Invitation expired/)).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: /go to sign in/i }));

    expect(window.location.href).toBe('/login?auto=1');
  });
});
