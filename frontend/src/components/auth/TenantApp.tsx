/**
 * TenantApp — Shared SPA wrapper for tenant subdomains ({slug}.orkyo.com).
 *
 * Standard React Router app with RequireAuth guards for shared, product-agnostic routes.
 * If the session is invalid, the machine redirects to the BFF login endpoint.
 * Does NOT include apex-only routes (TOS, onboarding, tenant-select).
 * Does NOT include SaaS-specific routes (AdminPage — defined in SaaS composition).
 *
 * For SaaS multi-tenant composition: extend with AdminPage and additional admin routes.
 * For Community single-tenant: use shared routes as-is, or add custom admin routes if needed.
 */

import { useEffect, lazy, Suspense } from 'react';
import { Routes, Route, useLocation } from 'react-router-dom';
import { RequireAuth } from '@foundation/src/components/auth/RequireAuth';
import { AppLayout } from '@foundation/src/components/layout/AppLayout';
import { LoginPage } from '@foundation/src/pages/LoginPage';
import { TenantSuspendedPage } from '@foundation/src/pages/TenantSuspendedPage';
import { ThemeToggle } from '@foundation/src/components/layout/ThemeToggle';
import { BreakGlassBanner } from '@foundation/src/components/break-glass/BreakGlassBanner';
import { useAuth } from '@foundation/src/contexts/AuthContext';
import { AUTH_STAGES, AUTH_EVENTS, TENANT_STATUS } from '@foundation/src/constants/auth';

// Lazy-loaded pages — split into separate chunks to reduce initial bundle size
const AboutPage = lazy(() => import('@foundation/src/pages/AboutPage').then(m => ({ default: m.AboutPage })));
const AccountPage = lazy(() => import('@foundation/src/pages/AccountPage').then(m => ({ default: m.AccountPage })));
const UtilizationPage = lazy(() => import('@foundation/src/pages/UtilizationPage').then(m => ({ default: m.UtilizationPage })));
const SpacesPage = lazy(() => import('@foundation/src/pages/SpacesPage').then(m => ({ default: m.SpacesPage })));
const ConflictsPage = lazy(() => import('@foundation/src/pages/ConflictsPage').then(m => ({ default: m.ConflictsPage })));
const RequestsPage = lazy(() => import('@foundation/src/pages/RequestsPage').then(m => ({ default: m.RequestsPage })));
const SettingsPage = lazy(() => import('@foundation/src/pages/SettingsPage').then(m => ({ default: m.SettingsPage })));
const MessagesPage = lazy(() => import('@foundation/src/pages/MessagesPage').then(m => ({ default: m.MessagesPage })));

/** Route prefixes where the AppLayout TopBar (with its own ThemeToggle) is rendered. */
const APP_LAYOUT_PREFIXES = ["/", "/spaces", "/requests", "/conflicts", "/settings"];

function FloatingThemeToggle() {
  const { pathname } = useLocation();
  const hasTopBar = APP_LAYOUT_PREFIXES.some((prefix) => {
    if (prefix === '/') return pathname === '/';
    return pathname === prefix || pathname.startsWith(prefix + '/');
  });
  if (hasTopBar) return null;
  return <ThemeToggle variant="floating" />;
}

export interface TenantAppProps {
  /** Renders plan comparison cards on the /account Plans tab. Provided by SaaS composition. */
  renderPlanCards?: () => React.ReactNode;
}

export function TenantApp({ renderPlanCards }: TenantAppProps = {}) {
  const { authStage, membership, send } = useAuth();

  // Session expiry on a tenant subdomain — trigger the BFF login redirect.
  // The machine's LOGIN event fires performLogin which navigates to the BFF.
  useEffect(() => {
    if (authStage === AUTH_STAGES.UNAUTHENTICATED) {
      send({ type: AUTH_EVENTS.LOGIN });
    }
  }, [authStage, send]);

  // Suspended tenant on its own subdomain — show the suspension page directly.
  // The same-origin POST to /api/tenant/reactivate works here because the
  // backend resolves the tenant from the subdomain.
  if (authStage === AUTH_STAGES.SELECTING_TENANT && membership?.state === TENANT_STATUS.SUSPENDED) {
    return (
      <>
        <ThemeToggle variant="floating" />
        <TenantSuspendedPage />
      </>
    );
  }

  return (
    <>
      <FloatingThemeToggle />
      <BreakGlassBanner />
      <Suspense fallback={<div className="flex-1 flex items-center justify-center h-screen"><div className="text-muted-foreground">Loading...</div></div>}>
      <Routes>
        {/* /login is intentionally kept for direct navigation recovery when a
            session expires on a tenant subdomain — the user can sign back in
            and be returned to the same subdomain context. */}
        <Route path="/login" element={<LoginPage />} />
        <Route path="/about" element={<RequireAuth><AboutPage /></RequireAuth>} />
        <Route path="/account" element={<RequireAuth requireMembership={false}><AccountPage renderPlanCards={renderPlanCards} /></RequireAuth>} />
        <Route path="/messages" element={<RequireAuth><MessagesPage /></RequireAuth>} />
        <Route path="/" element={<RequireAuth><AppLayout /></RequireAuth>}>
          <Route index element={<UtilizationPage />} />
          <Route path="spaces" element={<SpacesPage />} />
          <Route path="requests" element={<RequestsPage />} />
          <Route path="conflicts" element={<ConflictsPage />} />
          <Route path="settings" element={<SettingsPage />} />
        </Route>
      </Routes>
      </Suspense>
    </>
  );
}
