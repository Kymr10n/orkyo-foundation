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
import { Routes, Route, Navigate, useLocation } from 'react-router';
import { RequireAuth } from '@foundation/src/components/auth/RequireAuth';
import { RequireEditor } from '@foundation/src/components/auth/RequireEditor';
import { RequireTenantAdmin } from '@foundation/src/components/auth/RequireTenantAdmin';
import { AppLayout } from '@foundation/src/components/layout/AppLayout';
import { LoginPage } from '@foundation/src/pages/LoginPage';
import { TosPage } from '@foundation/src/pages/TosPage';
import { TenantSuspendedPage } from '@foundation/src/pages/TenantSuspendedPage';
import { TenantNoAccessPage } from '@foundation/src/pages/TenantNoAccessPage';
import { ThemeToggle } from '@foundation/src/components/layout/ThemeToggle';
import { Toaster } from '@foundation/src/components/ui/sonner';
import { LoadingSpinner } from '@foundation/src/components/ui/LoadingSpinner';
import { RouteErrorBoundary } from '@foundation/src/components/ui/RouteErrorBoundary';
import { NotFound } from '@foundation/src/components/layout/NotFound';
import { BreakGlassBanner } from '@foundation/src/components/break-glass/BreakGlassBanner';
import { useAuth } from '@foundation/src/contexts/AuthContext';
import { AUTH_STAGES, AUTH_EVENTS, ROUTE_ACCOUNT, ROUTE_CONFIGURATION,
  ROUTE_TENANT_ADMIN, isBlockedTenantState } from '@foundation/src/constants/auth';
import { RESOURCE_TYPE_KEY } from '@foundation/src/constants/resource-type-key';
import type { AccountPageExtraTab } from '@foundation/src/pages/AccountPage';

// Lazy-loaded pages — split into separate chunks to reduce initial bundle size
const AccountPage = lazy(() => import('@foundation/src/pages/AccountPage').then(m => ({ default: m.AccountPage })));
const AboutPage = lazy(() => import('@foundation/src/pages/AboutPage').then(m => ({ default: m.AboutPage })));
const UtilizationPage = lazy(() => import('@foundation/src/pages/UtilizationPage').then(m => ({ default: m.UtilizationPage })));
const SpacesPage = lazy(() => import('@foundation/src/pages/SpacesPage').then(m => ({ default: m.SpacesPage })));
const PeoplePage = lazy(() => import('@foundation/src/pages/PeoplePage').then(m => ({ default: m.PeoplePage })));
const InsightsPage = lazy(() => import('@foundation/src/pages/InsightsPage').then(m => ({ default: m.InsightsPage })));
const OverviewTab = lazy(() => import('@foundation/src/components/insights/OverviewTab').then(m => ({ default: m.OverviewTab })));
const UtilizationTab = lazy(() => import('@foundation/src/components/insights/UtilizationTab').then(m => ({ default: m.UtilizationTab })));
const ConflictsTab = lazy(() => import('@foundation/src/components/insights/ConflictsTab').then(m => ({ default: m.ConflictsTab })));
const RequestsPage = lazy(() => import('@foundation/src/pages/RequestsPage').then(m => ({ default: m.RequestsPage })));
const SettingsPage = lazy(() => import('@foundation/src/pages/SettingsPage').then(m => ({ default: m.SettingsPage })));
const TenantAdminPage = lazy(() => import('@foundation/src/pages/TenantAdminPage').then(m => ({ default: m.TenantAdminPage })));
const MessagesPage = lazy(() => import('@foundation/src/pages/MessagesPage').then(m => ({ default: m.MessagesPage })));

// Lazy-loaded route-leaf components — split out of the entry chunk. Most sessions
// only visit the utilization/requests routes; settings/admin/people/spaces leaves
// load on demand. All are rendered as route elements under the <Suspense> below.
const PersonList = lazy(() => import('@foundation/src/components/people/PersonList').then(m => ({ default: m.PersonList })));
const ResourceGroupList = lazy(() => import('@foundation/src/components/resource-groups/ResourceGroupList').then(m => ({ default: m.ResourceGroupList })));
const JobTitleSettings = lazy(() => import('@foundation/src/components/settings/JobTitleSettings').then(m => ({ default: m.JobTitleSettings })));
const DepartmentSettings = lazy(() => import('@foundation/src/components/settings/DepartmentSettings').then(m => ({ default: m.DepartmentSettings })));
const CriteriaSettings = lazy(() => import('@foundation/src/components/settings/CriteriaSettings').then(m => ({ default: m.CriteriaSettings })));
const ResourceTypeSettings = lazy(() => import('@foundation/src/components/settings/ResourceTypeSettings').then(m => ({ default: m.ResourceTypeSettings })));
const ListDefinitionSettings = lazy(() => import('@foundation/src/components/settings/ListDefinitionSettings').then(m => ({ default: m.ListDefinitionSettings })));
const ConfigurationPage = lazy(() => import('@foundation/src/pages/ConfigurationPage').then(m => ({ default: m.ConfigurationPage })));
const ResourcesPage = lazy(() => import('@foundation/src/pages/ResourcesPage').then(m => ({ default: m.ResourcesPage })));
const ResourceListTab = lazy(() => import('@foundation/src/components/resources/ResourceTypeTabs').then(m => ({ default: m.ResourceListTab })));
const ResourceGroupsTab = lazy(() => import('@foundation/src/components/resources/ResourceTypeTabs').then(m => ({ default: m.ResourceGroupsTab })));
const SiteSettings = lazy(() => import('@foundation/src/components/settings/SiteSettings').then(m => ({ default: m.SiteSettings })));
const TemplateSettings = lazy(() => import('@foundation/src/components/settings/TemplateSettings').then(m => ({ default: m.TemplateSettings })));
const PresetSettings = lazy(() => import('@foundation/src/components/settings/PresetSettings').then(m => ({ default: m.PresetSettings })));
const UserSettings = lazy(() => import('@foundation/src/components/settings/UserSettings').then(m => ({ default: m.UserSettings })));
const OrganizationSettings = lazy(() => import('@foundation/src/components/settings/OrganizationSettings').then(m => ({ default: m.OrganizationSettings })));
const TenantConfigSettings = lazy(() => import('@foundation/src/components/settings/TenantConfigSettings').then(m => ({ default: m.TenantConfigSettings })));
const SchedulingSettings = lazy(() => import('@foundation/src/components/settings/SchedulingSettings').then(m => ({ default: m.SchedulingSettings })));
const ReportingApiSettings = lazy(() => import('@foundation/src/components/settings/ReportingApiSettings').then(m => ({ default: m.ReportingApiSettings })));
const AuditLogTab = lazy(() => import('@foundation/src/components/admin/AuditLogTab').then(m => ({ default: m.AuditLogTab })));
const UsageLimitsSettings = lazy(() => import('@foundation/src/components/settings/UsageLimitsSettings').then(m => ({ default: m.UsageLimitsSettings })));
const FloorplanView = lazy(() => import('@foundation/src/components/spaces/FloorplanView').then(m => ({ default: m.FloorplanView })));
const SpaceListView = lazy(() => import('@foundation/src/components/spaces/SpaceListView').then(m => ({ default: m.SpaceListView })));
const PlaceableGroupsView = lazy(() => import('@foundation/src/components/spaces/PlaceableGroupsView').then(m => ({ default: m.PlaceableGroupsView })));

/** Route prefixes where the AppLayout TopBar (with its own ThemeToggle) is rendered. */
const APP_LAYOUT_PREFIXES = ["/", "/spaces", "/people", "/resources", "/requests", "/insights", "/conflicts", "/settings", "/tenant-admin", "/configuration"];

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
   * Optional plans/upgrade link. Every tier-gated surface shows an upsell whose CTA links
   * here (instead of silently redirecting): Reporting API, audit log, and calendar
   * subscriptions. Omit it (Community) and those upsells render without a CTA. Name kept
   * stable — despite now covering more than reporting — as it is the public prop consumed
   * by product apps via the published package.
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
          tosText={sessionData?.tosText}
          onAccept={() => send({ type: AUTH_EVENTS.TOS_ACCEPTED })}
          onCancel={() => send({ type: AUTH_EVENTS.LOGOUT })}
        />
      </>
    );
  }

  // Blocked tenant (suspended or scheduled for deletion) on its own subdomain —
  // show the suspension/restore page directly. The same-origin POST to
  // /api/tenant/reactivate works here because the backend resolves the tenant
  // from the subdomain.
  if (authStage === AUTH_STAGES.SELECTING_TENANT && isBlockedTenantState(membership?.state)) {
    return (
      <>
        <ThemeToggle variant="floating" />
        <TenantSuspendedPage />
      </>
    );
  }

  // Authenticated, but not a member of THIS workspace (or a member of none).
  // Terminal here — never fall through to the route tree: RequireAuth would
  // render "Redirecting to sign in…" and send UNAUTHORIZED, which none of these
  // states handle, so the machine would sit there forever (#102).
  if (
    (authStage === AUTH_STAGES.SELECTING_TENANT && !membership) ||
    authStage === AUTH_STAGES.NO_TENANTS ||
    authStage === AUTH_STAGES.NO_TENANTS_ADMIN
  ) {
    return (
      <>
        <ThemeToggle variant="floating" />
        <TenantNoAccessPage />
      </>
    );
  }

  return (
    <>
      <FloatingThemeToggle />
      <BreakGlassBanner />
      <Suspense fallback={<LoadingSpinner message="Loading…" />}>
      <Routes>
        {/* /login is intentionally kept for direct navigation recovery when a
            session expires on a tenant subdomain — the user can sign back in
            and be returned to the same subdomain context. */}
        <Route path="/login" element={<LoginPage />} />
        <Route path="/about" element={<RequireAuth><RouteErrorBoundary label="page"><AboutPage /></RouteErrorBoundary></RequireAuth>} />
        <Route path={ROUTE_ACCOUNT} element={<RequireAuth requireMembership={false}><RouteErrorBoundary label="page"><AccountPage accountTabs={accountTabs} /></RouteErrorBoundary></RequireAuth>} />
        <Route path="/messages" element={<RequireAuth><RouteErrorBoundary label="page"><MessagesPage /></RouteErrorBoundary></RequireAuth>} />
        <Route path="/" element={<RequireAuth><AppLayout upgradeHref={reportingApiUnavailableRedirectTo} /></RequireAuth>}>
          <Route index element={<UtilizationPage />} />
          <Route path="requests" element={<RequestsPage />} />
          <Route path="insights" element={<InsightsPage />}>
            <Route index element={<Navigate to="overview" replace />} />
            <Route path="overview" element={<OverviewTab />} />
            <Route path="utilization" element={<UtilizationTab />} />
            <Route path="conflicts" element={<ConflictsTab />} />
          </Route>
          {/* Back-compat: the old top-level Conflicts page is now the Insights → Conflicts tab. */}
          <Route path="conflicts" element={<Navigate to="/insights/conflicts" replace />} />

          {/* User-defined resource types. Built-in types keep their own pages, but the
              tabbed shape is the same so a custom type is not visibly a lesser citizen. */}
          <Route path="resources/:typeKey" element={<ResourcesPage />}>
            <Route index element={<Navigate to="list" replace />} />
            <Route path="list" element={<ResourceListTab />} />
            <Route path="groups" element={<ResourceGroupsTab />} />
          </Route>

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
            <Route path="organization" element={<OrganizationSettings upgradeHref={reportingApiUnavailableRedirectTo} />} />
            <Route path="configuration" element={<TenantConfigSettings scope="tenant" />} />
            <Route path="integrations" element={<ReportingApiSettings upgradeHref={reportingApiUnavailableRedirectTo} />} />
            <Route path="audit-log" element={<AuditLogTab upgradeHref={reportingApiUnavailableRedirectTo} />} />
            <Route path="usage-limits" element={<UsageLimitsSettings />} />
          </Route>

          {/* Resources — how the tenant shapes what it schedules. Admin-gated like
              Administration, but a different question: not who is in the tenant, but what
              its resources are and what they carry. Default = resource types. */}
          <Route
            path="configuration"
            element={<RequireTenantAdmin><ConfigurationPage /></RequireTenantAdmin>}
          >
            <Route index element={<Navigate to="resource-types" replace />} />
            <Route path="resource-types" element={<ResourceTypeSettings />} />
            <Route path="list-definitions" element={<ListDefinitionSettings />} />
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
            <Route path="list" element={<SpaceListView />} />
            <Route path="floorplan" element={<FloorplanView />} />
            <Route path="groups" element={<PlaceableGroupsView />} />
          </Route>

          {/* Backward-compatible redirects: resource-domain master data moved
              out of Settings into the owning resource page. */}
          <Route path="settings/departments" element={<Navigate to="/people/departments" replace />} />
          <Route path="settings/job-titles"  element={<Navigate to="/people/job-titles" replace />} />
          <Route path="settings/groups"      element={<Navigate to="/spaces/groups" replace />} />

          {/* Backward-compatible redirects: governance tabs moved out of Settings
              into the tenant-admin Administration page. */}
          <Route path="settings/sites"         element={<Navigate to={`${ROUTE_TENANT_ADMIN}/sites`} replace />} />
          <Route path="settings/users"         element={<Navigate to={`${ROUTE_TENANT_ADMIN}/users`} replace />} />
          <Route path="settings/organization"  element={<Navigate to={`${ROUTE_TENANT_ADMIN}/organization`} replace />} />
          <Route path="settings/configuration" element={<Navigate to={`${ROUTE_TENANT_ADMIN}/configuration`} replace />} />
          <Route path="settings/resource-types" element={<Navigate to={`${ROUTE_CONFIGURATION}/resource-types`} replace />} />
          {/* Resource types moved out of Administration into Resources. */}
          <Route path="tenant-admin/resource-types" element={<Navigate to={`${ROUTE_CONFIGURATION}/resource-types`} replace />} />
          <Route path="settings/integrations"  element={<Navigate to={`${ROUTE_TENANT_ADMIN}/integrations`} replace />} />
          <Route path="settings/usage-limits"  element={<Navigate to={`${ROUTE_TENANT_ADMIN}/usage-limits`} replace />} />
        </Route>

        {/* Catch-all: unknown URLs render a recoverable 404 instead of a blank screen. */}
        <Route path="*" element={<NotFound />} />
      </Routes>
      </Suspense>
      <Toaster />
    </>
  );
}
