import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type * as ReactRouterDom from 'react-router-dom';
import { AUTH_MESSAGES } from '@foundation/src/constants/auth';

const mockLogin = vi.fn();
const mockNavigate = vi.fn();
const mockUseAuth = vi.fn();

vi.mock('@foundation/src/contexts/AuthContext', () => ({
  useAuth: () => mockUseAuth(),
  debugAuth: vi.fn(),
}));

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof ReactRouterDom>('react-router-dom');
  return { ...actual, useNavigate: () => mockNavigate };
});

import { LoginPage } from './LoginPage';

function authState(overrides: Record<string, unknown> = {}) {
  return {
    isAuthenticated: false,
    isLoading: false,
    login: mockLogin,
    error: null,
    ...overrides,
  };
}

function renderLoginPage(path = '/login') {
  return render(
      <MemoryRouter>
      <LoginPage />
    </MemoryRouter>,
  );
}

describe('LoginPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockLogin.mockReturnValue(undefined);
    mockUseAuth.mockReturnValue(authState());
  });

  it('shows spinner while auth is loading', () => {
    mockUseAuth.mockReturnValue(authState({ isLoading: true }));
    renderLoginPage();
    expect(screen.getByText(AUTH_MESSAGES.REDIRECTING_LOGIN)).toBeInTheDocument();
  });

  it('auto-redirects to BFF login when unauthenticated with no error', async () => {
    renderLoginPage();
    await waitFor(() => {
      expect(mockLogin).toHaveBeenCalledTimes(1);
    });
  });

  it('shows redirecting spinner while auto-redirect is pending', () => {
    renderLoginPage();
    expect(screen.getByText(AUTH_MESSAGES.REDIRECTING_LOGIN)).toBeInTheDocument();
  });

  it('navigates to "/" when user is already authenticated', async () => {
    mockUseAuth.mockReturnValue(authState({ isAuthenticated: true }));
    renderLoginPage();
    await waitFor(() => {
      expect(mockNavigate).toHaveBeenCalledWith('/');
    });
    expect(mockLogin).not.toHaveBeenCalled();
  });

  it('does not call login() when already authenticated', async () => {
    mockUseAuth.mockReturnValue(authState({ isAuthenticated: true }));
    renderLoginPage();
    await waitFor(() => expect(mockNavigate).toHaveBeenCalled());
    expect(mockLogin).not.toHaveBeenCalled();
  });

  // ── Error display ──────────────────────────────────────────────────────

  it('shows error from auth context with retry button', () => {
    mockUseAuth.mockReturnValue(authState({ error: 'Authentication error: identity link failed' }));
    renderLoginPage();
    expect(screen.getByText(AUTH_MESSAGES.AUTH_ERROR_TITLE)).toBeInTheDocument();
    expect(screen.getByText('Authentication error: identity link failed')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /try again/i })).toBeInTheDocument();
  });

  it('does not auto-redirect when there is an error', () => {
    mockUseAuth.mockReturnValue(authState({ error: 'Some error' }));
    renderLoginPage();
    expect(mockLogin).not.toHaveBeenCalled();
  });

  it('calls login() when retry button is clicked', async () => {
    mockUseAuth.mockReturnValue(authState({ error: 'Some error' }));
    renderLoginPage();
    fireEvent.click(screen.getByRole('button', { name: /try again/i }));
    await waitFor(() => {
      expect(mockLogin).toHaveBeenCalledTimes(1);
    });
  });
});
