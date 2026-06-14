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
import { Routes, Route, Navigate, useLocation } from 'react-router-dom';
import { Box } from 'lucide-react';
import { PersonList } from '@foundation/src/components/people/PersonList';
import { ResourceGroupList } from '@foundation/src/components/resource-groups/ResourceGroupList';
import { JobTitleSettings } from '@foundation/src/components/settings/JobTitleSettings';
import { DepartmentSettings } from '@foundation/src/components/settings/DepartmentSettings';
import { CriteriaSettings } from '@foundation/src/components/settings/CriteriaSettings';
import { SiteSettings } from '@foundation/src/components/settings/SiteSettings';
import { TemplateSettings } from '@foundation/src/components/settings/TemplateSettings';
import { PresetSettings } from '@foundation/src/components/settings/PresetSettings';
import { UserSettings } from '@foundation/src/components/settings/UserSettings';
import { OrganizationSettings } from '@foundation/src/components/settings/OrganizationSettings';
import { TenantConfigSettings } from '@foundation/src/components/settings/TenantConfigSettings';
import { SchedulingSettings } from '@foundation/src/components/settings/SchedulingSettings';
import { ReportingApiPage } from '@foundation/src/components/settings/ReportingApiPage';
import { UsageLimitsSettings } from '@foundation/src/components/settings/UsageLimitsSettings';
import { FloorplanView } from '@foundation/src/components/spaces/FloorplanView';
import { RequireAuth } from '@foundation/src/components/auth/RequireAuth';
import { RequireEditor } from '@foundation/src/components/auth/RequireEditor';
import { RequireTenantAdmin } from '@foundation/src/components/auth/RequireTenantAdmin';
import { AppLayout } from '@foundation/src/components/layout/AppLayout';
import { LoginPage } from '@foundation/src/pages/LoginPage';
import { TosPage } from '@foundation/src/pages/TosPage';
import { TenantSuspendedPage } from '@foundation/src/pages/TenantSuspendedPage';
import { ThemeToggle } from '@foundation/src/components/layout/ThemeToggle';
import { Toaster } from '@foundation/src/components/ui/sonner';
import { LoadingSpinner } from '@foundation/src/components/ui/LoadingSpinner';
import { BreakGlassBanner } from '@foundation/src/components/break-glass/BreakGlassBanner';
import { useAuth } from '@foundation/src/contexts/AuthContext';
import { AUTH_STAGES, AUTH_EVENTS, TENANT_STATUS } from '@foundation/src/constants/auth';
import { RESOURCE_TYPE_KEY } from '@foundation/src/constants/resource-type-key';
import type { AccountPageExtraTab } from '@foundation/src/pages/AccountPage';

// Lazy-loaded pages — split into separate chunks to reduce initial bundle size
const AboutPage = lazy(() => import('@foundation/src/pages/AboutPage').then(m => ({ default: m.AboutPage })));
const AccountPage = lazy(() => import('@foundation/src/pages/AccountPage').then(m => ({ default: m.AccountPage })));
const UtilizationPage = lazy(() => import('@foundation/src/pages/UtilizationPage').then(m => ({ default: m.UtilizationPage })));
const SpacesPage = lazy(() => import('@foundation/src/pages/SpacesPage').then(m => ({ default: m.SpacesPage })));
const PeoplePage = lazy(() => import('@foundation/src/pages/PeoplePage').then(m => ({ default: m.PeoplePage })));
const ConflictsPage = lazy(() => import('@foundation/src/pages/ConflictsPage').then(m => ({ default: m.ConflictsPage })));
const RequestsPage = lazy(() => import('@foundation/src/pages/RequestsPage').then(m => ({ default: m.RequestsPage })));
const SettingsPage = lazy(() => import('@foundation/src/pages/SettingsPage').then(m => ({ default: m.SettingsPage })));
const TenantAdminPage = lazy(() => import('@foundation/src/pages/TenantAdminPage').then(m => ({ default: m.TenantAdminPage })));
const MessagesPage = lazy(() => import('@foundation/src/pages/MessagesPage').then(m => ({ default: m.MessagesPage })));

/** Route prefixes where the AppLayout TopBar (with its own ThemeToggle) is rendered. */
const APP_LAYOUT_PREFIXES = ["/", "/spaces", "/people", "/requests", "/conflicts", "/settings", "/tenant-admin"];

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
  /** Product-specific tabs for the shared account page. */
  accountTabs?: AccountPageExtraTab[];
  /**
   * Optional plans/upgrade link. When the Reporting API is tier-gated for the tenant, its
   * page shows an upsell whose CTA links here (instead of silently redirecting). Name kept
   * stable as it is the public prop consumed by product apps via the published package.
   */
  reportingApiUnavailableRedirectTo?: string;
}

export function TenantApp({ accountTabs, reportingApiUnavailableRedirectTo }: TenantAppProps = {}) {
  const { authStage, membership, sessionData, send } = useAuth();

  // Session expiry on a tenant subdomain — trigger the BFF login redirect.
  // The machine's LOGIN event fires performLogin which navigates to the BFF.
  useEffect(() => {
    if (authStage === AUTH_STAGES.UNAUTHENTICATED) {
      send({ type: AUTH_EVENTS.LOGIN });
    }
  }, [authStage, send]);

  // ToS required on a tenant subdomain — show TosPage directly.
  // Without this guard, RequireAuth redirects to /login → login() → BFF login → loop.
  if (authStage === AUTH_STAGES.TOS_REQUIRED) {
    return (
      <>
        <ThemeToggle variant="floating" />
        <TosPage
          tosVersion={sessionData?.requiredTosVersion ?? '2026-02'}
          onAccept={() => send({ type: AUTH_EVENTS.TOS_ACCEPTED })}
          onCancel={() => send({ type: AUTH_EVENTS.LOGOUT })}
        />
      </>
    );
  }

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
      <Suspense fallback={<LoadingSpinner message="Loading..." />}>
      <Routes>
        {/* /login is intentionally kept for direct navigation recovery when a
            session expires on a tenant subdomain — the user can sign back in
            and be returned to the same subdomain context. */}
        <Route path="/login" element={<LoginPage />} />
        <Route path="/about" element={<RequireAuth><AboutPage /></RequireAuth>} />
        <Route path="/account" element={<RequireAuth requireMembership={false}><AccountPage accountTabs={accountTabs} /></RequireAuth>} />
        <Route path="/messages" element={<RequireAuth><MessagesPage /></RequireAuth>} />
        <Route path="/" element={<RequireAuth><AppLayout /></RequireAuth>}>
          <Route index element={<UtilizationPage />} />
          <Route path="requests" element={<RequestsPage />} />
          <Route path="conflicts" element={<ConflictsPage />} />

          {/* Settings — editor-open content. Viewers are redirected to root. */}
          <Route path="settings" element={<RequireEditor><SettingsPage /></RequireEditor>}>
            <Route index element={<Navigate to="criteria" replace />} />
            <Route path="criteria" element={<CriteriaSettings />} />
            <Route path="templates" element={<TemplateSettings entityType="request" />} />
            <Route path="presets" element={<PresetSettings />} />
            <Route path="scheduling" element={<SchedulingSettings />} />
          </Route>

          {/* Administration — tenant-admin-only governance. Default = sites. */}
          <Route
            path="tenant-admin"
            element={<RequireTenantAdmin><TenantAdminPage /></RequireTenantAdmin>}
          >
            <Route index element={<Navigate to="sites" replace />} />
            <Route path="sites" element={<SiteSettings />} />
            <Route path="users" element={<UserSettings />} />
            <Route path="organization" element={<OrganizationSettings />} />
            <Route path="configuration" element={<TenantConfigSettings scope="tenant" />} />
            <Route path="integrations" element={<ReportingApiPage upgradeHref={reportingApiUnavailableRedirectTo} />} />
            <Route path="usage-limits" element={<UsageLimitsSettings />} />
          </Route>

          {/* People — nested sub-routes. Skills and absences are managed per-person
              via row actions on the People list (no standalone tabs). */}
          <Route path="people" element={<PeoplePage />}>
            <Route index element={<Navigate to="list" replace />} />
            <Route path="list" element={<PersonList />} />
            <Route path="teams" element={<ResourceGroupList resourceTypeKey={RESOURCE_TYPE_KEY.PERSON} entityLabel="Team" />} />
            <Route path="groups" element={<Navigate to="/people/teams" replace />} />
            <Route path="departments" element={<DepartmentSettings />} />
            <Route path="job-titles" element={<JobTitleSettings />} />
          </Route>

          {/* Spaces — nested sub-routes. Default = floorplan. */}
          <Route path="spaces" element={<SpacesPage />}>
            <Route index element={<Navigate to="floorplan" replace />} />
            <Route path="list" element={<Navigate to="/spaces/floorplan" replace />} />
            <Route path="floorplan" element={<FloorplanView />} />
            <Route path="groups" element={<ResourceGroupList resourceTypeKey={RESOURCE_TYPE_KEY.SPACE} membersIcon={Box} />} />
          </Route>

          {/* Backward-compatible redirects: resource-domain master data moved
              out of Settings into the owning resource page. */}
          <Route path="settings/departments" element={<Navigate to="/people/departments" replace />} />
          <Route path="settings/job-titles"  element={<Navigate to="/people/job-titles" replace />} />
          <Route path="settings/groups"      element={<Navigate to="/spaces/groups" replace />} />

          {/* Backward-compatible redirects: governance tabs moved out of Settings
              into the tenant-admin Administration page. */}
          <Route path="settings/sites"         element={<Navigate to="/tenant-admin/sites" replace />} />
          <Route path="settings/users"         element={<Navigate to="/tenant-admin/users" replace />} />
          <Route path="settings/organization"  element={<Navigate to="/tenant-admin/organization" replace />} />
          <Route path="settings/configuration" element={<Navigate to="/tenant-admin/configuration" replace />} />
          <Route path="settings/integrations"  element={<Navigate to="/tenant-admin/integrations" replace />} />
          <Route path="settings/usage-limits"  element={<Navigate to="/tenant-admin/usage-limits" replace />} />
        </Route>
      </Routes>
      </Suspense>
      <Toaster />
    </>
  );
}
