/**
 * ApexGateway — pure renderer for the auth pipeline.
 *
 * Reads authStage from the machine (via useAuth) and renders the matching
 * page. No logic lives here — pages send events to the machine directly.
 *
 * Stage → Page mapping:
 *   initializing           → LoadingSpinner
 *   unauthenticated        → LoginPage  (shows error + retry)
 *   redirecting_login      → LoadingSpinner  (machine entry action redirects to BFF)
 *   error_backend          → AuthErrorScreen
 *   error_network          → AuthErrorScreen
 *   tos_required           → TosPage
 *   no_tenants             → OnboardingPage
 *   no_tenants_admin       → AdminPage  (site admin with no tenants)
 *   selecting_tenant       → TenantSelectPage  (multi-tenant, suspended, deleting, site admin hub)
 *   redirecting_to_tenant  → LoadingSpinner  (machine entry action handles redirect)
 *   logging_out            → LoadingSpinner
 *   ready                  → LoadingSpinner  (LocalDevShell swaps to TenantApp)
 *
 * Public routes (no auth required):
 *   /create-account          → RequestAccessPage
 *   /signup                  → SignupPage
 *
 * Direct URL overrides (canAccessAdminPage / canAccessAccountPage):
 *   These are computed in AuthContext from authStage + isSiteAdmin, so the
 *   access logic stays in the context layer rather than in this renderer.
 */

import { useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '@/contexts/AuthContext';
import { AUTH_STAGES, AUTH_EVENTS, AUTH_MESSAGES } from '@/constants/auth';
import { LoginPage } from '@/pages/LoginPage';
import { TosPage } from '@/pages/TosPage';
import { AdminPage } from '@/pages/AdminPage';
import { AccountPage } from '@/pages/AccountPage';
import { OnboardingPage } from '@/pages/OnboardingPage';
import { TenantSelectPage } from '@/pages/TenantSelectPage';
import { RequestAccessPage } from '@/pages/RequestAccessPage';
import { SignupPage } from '@/pages/SignupPage';
import { LoadingSpinner } from '@/components/ui/LoadingSpinner';
import { AuthErrorScreen } from '@/components/ui/AuthErrorScreen';

export function ApexGateway() {
  const { authStage, sessionData, send, canAccessAdminPage, canAccessAccountPage } = useAuth();
  const { pathname } = useLocation();
  const navigate = useNavigate();

  // Public routes — no auth required, render before the pipeline.
  if (pathname === '/create-account') {
    return <RequestAccessPage />;
  }
  if (pathname === '/signup') {
    return <SignupPage />;
  }

  const isAdminRoute   = pathname === '/admin'   || pathname.startsWith('/admin/');
  const isAccountRoute = pathname === '/account' || pathname.startsWith('/account/');

  // Direct URL access overrides the pipeline for authenticated users.
  // Access conditions are computed in AuthContext (isSiteAdmin + authStage check).
  if (isAdminRoute && canAccessAdminPage) {
    return <AdminPage />;
  }

  if (isAccountRoute && canAccessAccountPage) {
    return <AccountPage />;
  }

  switch (authStage) {
    case AUTH_STAGES.INITIALIZING:
      return <LoadingSpinner message={AUTH_MESSAGES.LOADING} />;

    case AUTH_STAGES.UNAUTHENTICATED:
      return <LoginPage />;

    case AUTH_STAGES.REDIRECTING_LOGIN:
      return <LoadingSpinner message={AUTH_MESSAGES.REDIRECTING_LOGIN} />;

    case AUTH_STAGES.ERROR_BACKEND:
      return (
        <AuthErrorScreen
          variant="backend"
          title={AUTH_MESSAGES.BACKEND_ERROR_TITLE}
          detail={AUTH_MESSAGES.BACKEND_ERROR_DETAIL}
          onRetry={() => send({ type: AUTH_EVENTS.RETRY })}
        />
      );

    case AUTH_STAGES.ERROR_NETWORK:
      return (
        <AuthErrorScreen
          variant="network"
          title={AUTH_MESSAGES.NETWORK_ERROR_TITLE}
          detail={AUTH_MESSAGES.NETWORK_ERROR_DETAIL}
          onRetry={() => send({ type: AUTH_EVENTS.RETRY })}
        />
      );

    case AUTH_STAGES.TOS_REQUIRED:
      return (
        <TosPage
          tosVersion={sessionData?.requiredTosVersion ?? '2026-02'}
          onAccept={() => send({ type: AUTH_EVENTS.TOS_ACCEPTED })}
          onCancel={() => send({ type: AUTH_EVENTS.LOGOUT })}
        />
      );

    case AUTH_STAGES.NO_TENANTS:
      return (
        <OnboardingPage
          onComplete={() => send({ type: AUTH_EVENTS.TENANT_CREATED })}
          onCancel={() => send({ type: AUTH_EVENTS.LOGOUT })}
        />
      );

    case AUTH_STAGES.NO_TENANTS_ADMIN:
      return <AdminPage />;

    case AUTH_STAGES.SELECTING_TENANT:
      return (
        <TenantSelectPage
          tenants={sessionData?.tenants ?? []}
          onSelect={(m) => send({ type: AUTH_EVENTS.TENANT_SELECTED, membership: m })}
          onCancel={() => send({ type: AUTH_EVENTS.LOGOUT })}
          onAdminPage={canAccessAdminPage ? () => navigate('/admin') : undefined}
        />
      );

    case AUTH_STAGES.REDIRECTING_TO_TENANT:
      return <LoadingSpinner message={AUTH_MESSAGES.REDIRECTING} />;

    case AUTH_STAGES.LOGGING_OUT:
      return <LoadingSpinner message={AUTH_MESSAGES.SIGNING_OUT} />;

    case AUTH_STAGES.READY:
      return <LoadingSpinner message={AUTH_MESSAGES.REDIRECTING} />;
  }
}
