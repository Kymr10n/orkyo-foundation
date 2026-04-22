import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { SignupPage } from './SignupPage';

vi.mock('@/lib/utils/tenant-navigation', () => ({
  navigateToApex: vi.fn(),
}));

vi.mock('@/lib/core/api-utils', () => ({
  API_BASE_URL: 'http://localhost:5000',
}));

vi.mock('@/lib/core/api-paths', () => ({
  API_PATHS: { INVITATION_VALIDATE: '/api/invitations/validate' },
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
        json: () => Promise.resolve({ error: 'Invitation expired' }),
      }),
    ));

    renderSignup('?invitation=expired-token');
    await waitFor(() => {
      expect(screen.getByText('Invitation expired')).toBeInTheDocument();
    });
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
});
