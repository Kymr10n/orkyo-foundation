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
 *   no_tenants_admin       → renderAdminPage()  (SaaS-injected; site admin with no tenants)
 *   selecting_tenant       → renderTenantSelectPage(...)  (SaaS-injected; multi-tenant hub)
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
 *
 * Composition contract:
 *   `renderAdminPage` and `renderTenantSelectPage` are SaaS-specific concerns
 *   (multi-tenant administration). Foundation does not own those pages — the
 *   composition layer (orkyo-saas) injects them. Community / single-tenant
 *   shells may omit both slots, since their auth flows never reach the
 *   `no_tenants_admin` or `selecting_tenant` stages.
 */

import type { ReactNode } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useAuth, type TenantMembership } from '@/contexts/AuthContext';
import { AUTH_STAGES, AUTH_EVENTS, AUTH_MESSAGES } from '@/constants/auth';
import { LoginPage } from '@/pages/LoginPage';
import { TosPage } from '@/pages/TosPage';
import { AccountPage } from '@/pages/AccountPage';
import { OnboardingPage } from '@/pages/OnboardingPage';
import { RequestAccessPage } from '@/pages/RequestAccessPage';
import { SignupPage } from '@/pages/SignupPage';
import { LoadingSpinner } from '@/components/ui/LoadingSpinner';
import { AuthErrorScreen } from '@/components/ui/AuthErrorScreen';

export interface TenantSelectPageRenderArgs {
  tenants: TenantMembership[];
  onSelect: (membership: TenantMembership) => void;
  onCancel: () => void;
  onAdminPage?: () => void;
}

export interface ApexGatewayProps {
  /** Renders the multi-tenant admin hub. Provided by SaaS composition. */
  renderAdminPage?: () => ReactNode;
  /** Renders the tenant selection page. Provided by SaaS composition. */
  renderTenantSelectPage?: (args: TenantSelectPageRenderArgs) => ReactNode;
}

export function ApexGateway({
  renderAdminPage,
  renderTenantSelectPage,
}: ApexGatewayProps = {}) {
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
  if (isAdminRoute && canAccessAdminPage && renderAdminPage) {
    return <>{renderAdminPage()}</>;
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
      return renderAdminPage
        ? <>{renderAdminPage()}</>
        : <LoadingSpinner message={AUTH_MESSAGES.LOADING} />;

    case AUTH_STAGES.SELECTING_TENANT:
      return renderTenantSelectPage
        ? <>{renderTenantSelectPage({
            tenants: sessionData?.tenants ?? [],
            onSelect: (membership: TenantMembership) =>
              send({ type: AUTH_EVENTS.TENANT_SELECTED, membership }),
            onCancel: () => send({ type: AUTH_EVENTS.LOGOUT }),
            onAdminPage: canAccessAdminPage && renderAdminPage
              ? () => navigate('/admin')
              : undefined,
          })}</>
        : <LoadingSpinner message={AUTH_MESSAGES.LOADING} />;

    case AUTH_STAGES.REDIRECTING_TO_TENANT:
      return <LoadingSpinner message={AUTH_MESSAGES.REDIRECTING} />;

    case AUTH_STAGES.LOGGING_OUT:
      return <LoadingSpinner message={AUTH_MESSAGES.SIGNING_OUT} />;

    case AUTH_STAGES.READY:
      return <LoadingSpinner message={AUTH_MESSAGES.REDIRECTING} />;
  }
}
