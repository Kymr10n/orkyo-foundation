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
import { FloorplanView } from '@foundation/src/components/spaces/FloorplanView';
import { SpaceCapabilitiesTab } from '@foundation/src/components/spaces/SpaceCapabilitiesTab';
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
const PeoplePage = lazy(() => import('@foundation/src/pages/PeoplePage').then(m => ({ default: m.PeoplePage })));
const ConflictsPage = lazy(() => import('@foundation/src/pages/ConflictsPage').then(m => ({ default: m.ConflictsPage })));
const RequestsPage = lazy(() => import('@foundation/src/pages/RequestsPage').then(m => ({ default: m.RequestsPage })));
const SettingsPage = lazy(() => import('@foundation/src/pages/SettingsPage').then(m => ({ default: m.SettingsPage })));
const MessagesPage = lazy(() => import('@foundation/src/pages/MessagesPage').then(m => ({ default: m.MessagesPage })));

/** Route prefixes where the AppLayout TopBar (with its own ThemeToggle) is rendered. */
const APP_LAYOUT_PREFIXES = ["/", "/spaces", "/people", "/requests", "/conflicts", "/settings"];

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
          <Route path="requests" element={<RequestsPage />} />
          <Route path="conflicts" element={<ConflictsPage />} />

          {/* Settings — nested sub-routes. Default = criteria. */}
          <Route path="settings" element={<SettingsPage />}>
            <Route index element={<Navigate to="criteria" replace />} />
            <Route path="criteria" element={<CriteriaSettings />} />
            <Route path="sites" element={<SiteSettings />} />
            <Route path="templates" element={<TemplateSettings entityType="request" />} />
            <Route path="presets" element={<PresetSettings />} />
            <Route path="users" element={<UserSettings />} />
            <Route path="organization" element={<OrganizationSettings />} />
            <Route path="scheduling" element={<SchedulingSettings />} />
            <Route path="configuration" element={<TenantConfigSettings scope="tenant" />} />
          </Route>

          {/* People — nested sub-routes. Skills and absences are managed per-person
              via row actions on the People list (no standalone tabs). */}
          <Route path="people" element={<PeoplePage />}>
            <Route index element={<Navigate to="list" replace />} />
            <Route path="list" element={<PersonList />} />
            <Route path="groups" element={<ResourceGroupList resourceTypeKey="person" />} />
            <Route path="departments" element={<DepartmentSettings />} />
            <Route path="job-titles" element={<JobTitleSettings />} />
          </Route>

          {/* Spaces — nested sub-routes. Default = floorplan. */}
          <Route path="spaces" element={<SpacesPage />}>
            <Route index element={<Navigate to="floorplan" replace />} />
            <Route path="list" element={<Navigate to="/spaces/floorplan" replace />} />
            <Route path="floorplan" element={<FloorplanView />} />
            <Route path="groups" element={<ResourceGroupList resourceTypeKey="space" />} />
            <Route path="capabilities" element={<SpaceCapabilitiesTab />} />
          </Route>

          {/* Backward-compatible redirects: resource-domain master data moved
              out of Settings into the owning resource page. */}
          <Route path="settings/departments" element={<Navigate to="/people/departments" replace />} />
          <Route path="settings/job-titles"  element={<Navigate to="/people/job-titles" replace />} />
          <Route path="settings/groups"      element={<Navigate to="/spaces/groups" replace />} />
        </Route>
      </Routes>
      </Suspense>
    </>
  );
}
