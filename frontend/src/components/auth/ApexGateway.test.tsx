import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { AUTH_STAGES, AUTH_MESSAGES } from '@/constants/auth';

// ── Mock page components ──────────────────────────────────────────────────────

vi.mock('@/pages/LoginPage', () => ({
  LoginPage: () => <div data-testid="login-page" />,
}));
vi.mock('@/pages/TosPage', () => ({
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  TosPage: (props: any) => <div data-testid="tos-page" data-tos-version={props.tosVersion} />,
}));
vi.mock('@/pages/AdminPage', () => ({
  AdminPage: () => <div data-testid="admin-page" />,
}));
vi.mock('@/pages/AccountPage', () => ({
  AccountPage: () => <div data-testid="account-page" />,
}));
vi.mock('@/pages/OnboardingPage', () => ({
  OnboardingPage: () => <div data-testid="onboarding-page" />,
}));
vi.mock('@/pages/TenantSelectPage', () => ({
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  TenantSelectPage: (props: any) => (
    <div
      data-testid="tenant-select-page"
      data-tenant-count={props.tenants?.length ?? 0}
      data-has-admin-link={!!props.onAdminPage}
    />
  ),
}));
vi.mock('@/pages/RequestAccessPage', () => ({
  RequestAccessPage: () => <div data-testid="request-access-page" />,
}));
vi.mock('@/pages/SignupPage', () => ({
  SignupPage: () => <div data-testid="signup-page" />,
}));

// ── Mock useAuth ──────────────────────────────────────────────────────────────

const mockSend = vi.fn();
const mockUseAuth = vi.fn();

vi.mock('@/contexts/AuthContext', () => ({
  useAuth: () => mockUseAuth(),
}));

import { ApexGateway } from './ApexGateway';

// ── Helpers ───────────────────────────────────────────────────────────────────

function authState(overrides: Record<string, unknown> = {}) {
  return {
    authStage: AUTH_STAGES.INITIALIZING,
    sessionData: null,
    canAccessAdminPage: false,
    canAccessAccountPage: false,
    send: mockSend,
    ...overrides,
  };
}

function renderGateway(path = '/') {
  return render(
    <MemoryRouter initialEntries={[path]} future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
      <ApexGateway />
    </MemoryRouter>,
  );
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe('ApexGateway', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  // ── Pipeline stages: loading spinners ────────────────────────────────────

  it.each([
    [AUTH_STAGES.INITIALIZING,        AUTH_MESSAGES.LOADING],
    [AUTH_STAGES.REDIRECTING_LOGIN,   AUTH_MESSAGES.REDIRECTING_LOGIN],
    [AUTH_STAGES.REDIRECTING_TO_TENANT, AUTH_MESSAGES.REDIRECTING],
    [AUTH_STAGES.LOGGING_OUT,         AUTH_MESSAGES.SIGNING_OUT],
    [AUTH_STAGES.READY,               AUTH_MESSAGES.REDIRECTING],
  ])('shows correct spinner for %s stage', (authStage, expectedText) => {
    mockUseAuth.mockReturnValue(authState({ authStage }));
    renderGateway();
    expect(screen.getByText(expectedText)).toBeInTheDocument();
  });

  // ── Pipeline stages: page renders ────────────────────────────────────────

  it.each([
    [AUTH_STAGES.UNAUTHENTICATED,   'login-page'],
    [AUTH_STAGES.NO_TENANTS,        'onboarding-page'],
    [AUTH_STAGES.NO_TENANTS_ADMIN,  'admin-page'],
  ])('renders correct page for %s stage', (authStage, testId) => {
    mockUseAuth.mockReturnValue(authState({ authStage }));
    renderGateway();
    expect(screen.getByTestId(testId)).toBeInTheDocument();
  });

  // ── TOS stage ────────────────────────────────────────────────────────────

  it('renders TosPage for tos_required stage', () => {
    mockUseAuth.mockReturnValue(authState({
      authStage: AUTH_STAGES.TOS_REQUIRED,
      sessionData: { requiredTosVersion: '2026-03', tenants: [] },
    }));
    renderGateway();
    expect(screen.getByTestId('tos-page')).toBeInTheDocument();
  });

  it('passes tosVersion to TosPage', () => {
    mockUseAuth.mockReturnValue(authState({
      authStage: AUTH_STAGES.TOS_REQUIRED,
      sessionData: { requiredTosVersion: '2026-03', tenants: [] },
    }));
    renderGateway();
    expect(screen.getByTestId('tos-page')).toHaveAttribute('data-tos-version', '2026-03');
  });

  // ── Tenant selection stage ────────────────────────────────────────────────

  it('renders TenantSelectPage for selecting_tenant stage', () => {
    mockUseAuth.mockReturnValue(authState({
      authStage: AUTH_STAGES.SELECTING_TENANT,
      sessionData: { tenants: [{ slug: 'a' }, { slug: 'b' }] },
    }));
    renderGateway();
    expect(screen.getByTestId('tenant-select-page')).toBeInTheDocument();
  });

  it('passes tenants to TenantSelectPage', () => {
    mockUseAuth.mockReturnValue(authState({
      authStage: AUTH_STAGES.SELECTING_TENANT,
      sessionData: { tenants: [{ slug: 'a' }, { slug: 'b' }] },
    }));
    renderGateway();
    expect(screen.getByTestId('tenant-select-page')).toHaveAttribute('data-tenant-count', '2');
  });

  it('passes onAdminPage to TenantSelectPage when canAccessAdminPage is true', () => {
    mockUseAuth.mockReturnValue(authState({
      authStage: AUTH_STAGES.SELECTING_TENANT,
      sessionData: { tenants: [{ slug: 'a' }] },
      canAccessAdminPage: true,
    }));
    renderGateway();
    expect(screen.getByTestId('tenant-select-page')).toHaveAttribute('data-has-admin-link', 'true');
  });

  it('does not pass onAdminPage to TenantSelectPage when canAccessAdminPage is false', () => {
    mockUseAuth.mockReturnValue(authState({
      authStage: AUTH_STAGES.SELECTING_TENANT,
      sessionData: { tenants: [{ slug: 'a' }] },
      canAccessAdminPage: false,
    }));
    renderGateway();
    expect(screen.getByTestId('tenant-select-page')).toHaveAttribute('data-has-admin-link', 'false');
  });

  // ── Error states ─────────────────────────────────────────────────────────

  it('renders backend error screen for error_backend stage', () => {
    mockUseAuth.mockReturnValue(authState({ authStage: AUTH_STAGES.ERROR_BACKEND }));
    renderGateway();
    expect(screen.getByText(AUTH_MESSAGES.BACKEND_ERROR_TITLE)).toBeInTheDocument();
    expect(screen.getByText(AUTH_MESSAGES.BACKEND_ERROR_DETAIL)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /try again/i })).toBeInTheDocument();
  });

  it('renders network error screen for error_network stage', () => {
    mockUseAuth.mockReturnValue(authState({ authStage: AUTH_STAGES.ERROR_NETWORK }));
    renderGateway();
    expect(screen.getByText(AUTH_MESSAGES.NETWORK_ERROR_TITLE)).toBeInTheDocument();
    expect(screen.getByText(AUTH_MESSAGES.NETWORK_ERROR_DETAIL)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /try again/i })).toBeInTheDocument();
  });

  // ── /account direct URL access ────────────────────────────────────────────

  it('renders AccountPage at /account when canAccessAccountPage is true', () => {
    mockUseAuth.mockReturnValue(authState({
      authStage: AUTH_STAGES.READY,
      canAccessAccountPage: true,
    }));
    renderGateway('/account');
    expect(screen.getByTestId('account-page')).toBeInTheDocument();
  });

  it('renders AccountPage at /account for selecting_tenant when canAccessAccountPage is true', () => {
    mockUseAuth.mockReturnValue(authState({
      authStage: AUTH_STAGES.SELECTING_TENANT,
      canAccessAccountPage: true,
      sessionData: { tenants: [] },
    }));
    renderGateway('/account');
    expect(screen.getByTestId('account-page')).toBeInTheDocument();
  });

  it('does not render AccountPage at /account when canAccessAccountPage is false', () => {
    mockUseAuth.mockReturnValue(authState({
      authStage: AUTH_STAGES.UNAUTHENTICATED,
      canAccessAccountPage: false,
    }));
    renderGateway('/account');
    expect(screen.queryByTestId('account-page')).not.toBeInTheDocument();
    expect(screen.getByTestId('login-page')).toBeInTheDocument();
  });

  it('does not render AccountPage at /account while initializing', () => {
    mockUseAuth.mockReturnValue(authState({
      authStage: AUTH_STAGES.INITIALIZING,
      canAccessAccountPage: false,
    }));
    renderGateway('/account');
    expect(screen.queryByTestId('account-page')).not.toBeInTheDocument();
    expect(screen.getByText(AUTH_MESSAGES.LOADING)).toBeInTheDocument();
  });

  // ── /admin direct URL access ──────────────────────────────────────────────

  it('renders AdminPage at /admin when canAccessAdminPage is true', () => {
    mockUseAuth.mockReturnValue(authState({
      authStage: AUTH_STAGES.READY,
      canAccessAdminPage: true,
    }));
    renderGateway('/admin');
    expect(screen.getByTestId('admin-page')).toBeInTheDocument();
  });

  it('does not render AdminPage at /admin when canAccessAdminPage is false', () => {
    mockUseAuth.mockReturnValue(authState({
      authStage: AUTH_STAGES.UNAUTHENTICATED,
      canAccessAdminPage: false,
    }));
    renderGateway('/admin');
    expect(screen.getByTestId('login-page')).toBeInTheDocument();
  });

  it('does not render AdminPage at /admin while initializing', () => {
    mockUseAuth.mockReturnValue(authState({
      authStage: AUTH_STAGES.INITIALIZING,
      canAccessAdminPage: false,
    }));
    renderGateway('/admin');
    expect(screen.getByText(AUTH_MESSAGES.LOADING)).toBeInTheDocument();
  });

  it('renders AccountPage at /account/settings when canAccessAccountPage is true', () => {
    mockUseAuth.mockReturnValue(authState({
      authStage: AUTH_STAGES.READY,
      canAccessAccountPage: true,
    }));
    renderGateway('/account/settings');
    expect(screen.getByTestId('account-page')).toBeInTheDocument();
  });

  it('renders AdminPage at /admin/users when canAccessAdminPage is true', () => {
    mockUseAuth.mockReturnValue(authState({
      authStage: AUTH_STAGES.READY,
      canAccessAdminPage: true,
    }));
    renderGateway('/admin/users');
    expect(screen.getByTestId('admin-page')).toBeInTheDocument();
  });

  // ── Public routes (no auth required) ──────────────────────────────────────

  it('renders RequestAccessPage at /create-account regardless of auth stage', () => {
    mockUseAuth.mockReturnValue(authState({ authStage: AUTH_STAGES.INITIALIZING }));
    renderGateway('/create-account');
    expect(screen.getByTestId('request-access-page')).toBeInTheDocument();
  });

  it('renders SignupPage at /signup regardless of auth stage', () => {
    mockUseAuth.mockReturnValue(authState({ authStage: AUTH_STAGES.INITIALIZING }));
    renderGateway('/signup');
    expect(screen.getByTestId('signup-page')).toBeInTheDocument();
  });

});
