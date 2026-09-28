import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import type * as ReactRouterDom from 'react-router';
import { AUTH_MESSAGES } from '@foundation/src/constants/auth';

const mockLogin = vi.fn();
const mockNavigate = vi.fn();

vi.mock('@foundation/src/contexts/AuthContext', async (importOriginal) => ({
  ...(await importOriginal<object>()),
  useAuth: vi.fn(),
}));

vi.mock('react-router', async () => {
  const actual = await vi.importActual<typeof ReactRouterDom>('react-router');
  return { ...actual, useNavigate: () => mockNavigate };
});

import { LoginPage } from './LoginPage';
import { useAuth } from '@foundation/src/contexts/AuthContext';
import { mockAuth, type MockAuthOptions } from '@foundation/src/test-utils/auth';

function authState(overrides: MockAuthOptions = {}) {
  return mockAuth({ isAuthenticated: false, login: mockLogin, ...overrides });
}

function renderLoginPage(_path = '/login') {
  return render(
      <MemoryRouter>
      <LoginPage />
    </MemoryRouter>,
  );
}

describe('LoginPage', () => {
  beforeEach(() => {
    mockLogin.mockReturnValue(undefined);
    vi.mocked(useAuth).mockReturnValue(authState());
  });

  it('shows spinner while auth is loading', () => {
    vi.mocked(useAuth).mockReturnValue(authState({ isLoading: true }));
    renderLoginPage();
    expect(screen.getByText(AUTH_MESSAGES.REDIRECTING_LOGIN)).toBeInTheDocument();
  });

  it('auto-redirects to BFF login when unauthenticated with no error', async () => {
    renderLoginPage();
    await waitFor(() => {
      expect(mockLogin).toHaveBeenCalledTimes(1);
    });
    // login() takes no arguments — the prompt/login_hint passthrough was removed.
    expect(mockLogin).toHaveBeenCalledWith();
  });

  it('shows redirecting spinner while auto-redirect is pending', () => {
    renderLoginPage();
    expect(screen.getByText(AUTH_MESSAGES.REDIRECTING_LOGIN)).toBeInTheDocument();
  });

  it('navigates to "/" when user is already authenticated', async () => {
    vi.mocked(useAuth).mockReturnValue(authState({ isAuthenticated: true }));
    renderLoginPage();
    await waitFor(() => {
      expect(mockNavigate).toHaveBeenCalledWith('/', { replace: true });
    });
    expect(mockLogin).not.toHaveBeenCalled();
  });

  it('does not call login() when already authenticated', async () => {
    vi.mocked(useAuth).mockReturnValue(authState({ isAuthenticated: true }));
    renderLoginPage();
    await waitFor(() => expect(mockNavigate).toHaveBeenCalled());
    expect(mockLogin).not.toHaveBeenCalled();
  });

  // ── Error display ──────────────────────────────────────────────────────

  it('shows error from auth context with retry button', () => {
    vi.mocked(useAuth).mockReturnValue(authState({ error: 'Authentication error: identity link failed' }));
    renderLoginPage();
    expect(screen.getByText(AUTH_MESSAGES.AUTH_ERROR_TITLE)).toBeInTheDocument();
    expect(screen.getByText('Authentication error: identity link failed')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /try again/i })).toBeInTheDocument();
  });

  it('does not auto-redirect when there is an error', () => {
    vi.mocked(useAuth).mockReturnValue(authState({ error: 'Some error' }));
    renderLoginPage();
    expect(mockLogin).not.toHaveBeenCalled();
  });

  it('calls login() when retry button is clicked', async () => {
    vi.mocked(useAuth).mockReturnValue(authState({ error: 'Some error' }));
    renderLoginPage();
    fireEvent.click(screen.getByRole('button', { name: /try again/i }));
    await waitFor(() => {
      expect(mockLogin).toHaveBeenCalledTimes(1);
    });
  });
});
