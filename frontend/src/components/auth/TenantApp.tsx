/**
 * TenantApp — Shared SPA wrapper for tenant subdomains ({slug}.orkyo.com).
 *
 * Standard React Router app with RequireAuth guards for shared, product-agnostic routes.
 * If the session is invalid, the machine redirects to the BFF login endpoint.
 * Does NOT include apex-only routes (TOS, onboarding, tenant-select).
 * Does NOT include SaaS-specific routes (AdminPage — defined in SaaS composition).
 *
 * A product's own site-administration page plugs into the `renderAdminPage` slot — the same
 * slot ApexGateway takes — so neither shell branches on the pathname itself.
 */

import { useEffect, lazy, Suspense, type ReactNode } from 'react';
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
import { AUTH_STAGES, AUTH_EVENTS, ROUTE_ABOUT, ROUTE_ACCOUNT, ROUTE_ASSETS,
  ROUTE_CONFIGURATION, ROUTE_HOME, ROUTE_INSIGHTS, ROUTE_INSIGHTS_BOTTLENECKS,
  ROUTE_INSIGHTS_CONFLICTS, ROUTE_INSIGHTS_OVERVIEW, ROUTE_INSIGHTS_UTILIZATION,
  ROUTE_LOGIN, ROUTE_MESSAGES, ROUTE_ORGANIZATION, ROUTE_REQUEST_PLAN, ROUTE_REQUESTS,
  ROUTE_SETTINGS, ROUTE_SITE_ADMIN, ROUTE_STATIONS, ROUTE_STATIONS_FLOORPLAN,
  ROUTE_TENANT_ADMIN,
  ROUTE_SETTINGS_CRITERIA, ROUTE_SETTINGS_TEMPLATES, ROUTE_SETTINGS_ROUTINGS,
  ROUTE_SETTINGS_PRESETS, ROUTE_SETTINGS_SCHEDULING,
  ROUTE_TENANT_ADMIN_SITES, ROUTE_TENANT_ADMIN_USERS, ROUTE_TENANT_ADMIN_ORGANIZATION,
  ROUTE_TENANT_ADMIN_CONFIGURATION, ROUTE_TENANT_ADMIN_INTEGRATIONS,
  ROUTE_TENANT_ADMIN_API_ACCESS, ROUTE_TENANT_ADMIN_AI_ASSISTANT,
  ROUTE_TENANT_ADMIN_AUDIT_LOG, ROUTE_TENANT_ADMIN_USAGE_LIMITS,
  ROUTE_CONFIGURATION_RESOURCE_TYPES, ROUTE_CONFIGURATION_CATALOG,
  ROUTE_CONFIGURATION_LIST_DEFINITIONS,
  isBlockedTenantState } from '@foundation/src/constants/auth';
import { RESOURCE_TYPE_TAB } from '@foundation/src/constants/resource-class';
import type { AccountPageExtraTab } from '@foundation/src/pages/AccountPage';

// Lazy-loaded pages — split into separate chunks to reduce initial bundle size
const AccountPage = lazy(() => import('@foundation/src/pages/AccountPage').then(m => ({ default: m.AccountPage })));
const AboutPage = lazy(() => import('@foundation/src/pages/AboutPage').then(m => ({ default: m.AboutPage })));
const UtilizationPage = lazy(() => import('@foundation/src/pages/UtilizationPage').then(m => ({ default: m.UtilizationPage })));
const RequestPlanPage = lazy(() => import('@foundation/src/pages/RequestPlanPage').then(m => ({ default: m.RequestPlanPage })));
const InsightsPage = lazy(() => import('@foundation/src/pages/InsightsPage').then(m => ({ default: m.InsightsPage })));
const OverviewTab = lazy(() => import('@foundation/src/components/insights/OverviewTab').then(m => ({ default: m.OverviewTab })));
const UtilizationTab = lazy(() => import('@foundation/src/components/insights/UtilizationTab').then(m => ({ default: m.UtilizationTab })));
const ConflictsTab = lazy(() => import('@foundation/src/components/insights/ConflictsTab').then(m => ({ default: m.ConflictsTab })));
const BottlenecksTab = lazy(() => import('@foundation/src/components/insights/BottlenecksTab').then(m => ({ default: m.BottlenecksTab })));
const RequestsPage = lazy(() => import('@foundation/src/pages/RequestsPage').then(m => ({ default: m.RequestsPage })));
const SettingsPage = lazy(() => import('@foundation/src/pages/SettingsPage').then(m => ({ default: m.SettingsPage })));
const TenantAdminPage = lazy(() => import('@foundation/src/pages/TenantAdminPage').then(m => ({ default: m.TenantAdminPage })));
const MessagesPage = lazy(() => import('@foundation/src/pages/MessagesPage').then(m => ({ default: m.MessagesPage })));

// Lazy-loaded route-leaf components — split out of the entry chunk. Most sessions
// only visit the utilization/requests routes; settings/admin/people/spaces leaves
// load on demand. All are rendered as route elements under the <Suspense> below.
const CriteriaSettings = lazy(() => import('@foundation/src/components/settings/CriteriaSettings').then(m => ({ default: m.CriteriaSettings })));
const ResourceTypeSettings = lazy(() => import('@foundation/src/components/settings/ResourceTypeSettings').then(m => ({ default: m.ResourceTypeSettings })));
const ListDefinitionSettings = lazy(() => import('@foundation/src/components/settings/ListDefinitionSettings').then(m => ({ default: m.ListDefinitionSettings })));
const TypeCatalogSettings = lazy(() => import('@foundation/src/components/settings/TypeCatalogSettings').then(m => ({ default: m.TypeCatalogSettings })));
const ConfigurationPage = lazy(() => import('@foundation/src/pages/ConfigurationPage').then(m => ({ default: m.ConfigurationPage })));
const OrganizationPage = lazy(() => import('@foundation/src/pages/OrganizationPage').then(m => ({ default: m.OrganizationPage })));
const LegacyTypeRedirect = lazy(() => import('@foundation/src/components/auth/LegacyTypeRedirect').then(m => ({ default: m.LegacyTypeRedirect })));
const ResourceClassPage = lazy(() => import('@foundation/src/pages/ResourceClassPage').then(m => ({ default: m.ResourceClassPage })));
const ResourceListsTab = lazy(() => import('@foundation/src/components/resources/ResourceListsTab').then(m => ({ default: m.ResourceListsTab })));
const ResourceListTab = lazy(() => import('@foundation/src/components/resources/ResourceTypeTabs').then(m => ({ default: m.ResourceListTab })));
const ResourceGroupsTab = lazy(() => import('@foundation/src/components/resources/ResourceTypeTabs').then(m => ({ default: m.ResourceGroupsTab })));
const SiteSettings = lazy(() => import('@foundation/src/components/settings/SiteSettings').then(m => ({ default: m.SiteSettings })));
const TemplateSettings = lazy(() => import('@foundation/src/components/settings/TemplateSettings').then(m => ({ default: m.TemplateSettings })));
const RoutingSettings = lazy(() => import('@foundation/src/components/settings/RoutingSettings').then(m => ({ default: m.RoutingSettings })));
const PresetSettings = lazy(() => import('@foundation/src/components/settings/PresetSettings').then(m => ({ default: m.PresetSettings })));
const UserSettings = lazy(() => import('@foundation/src/components/settings/UserSettings').then(m => ({ default: m.UserSettings })));
const OrganizationSettings = lazy(() => import('@foundation/src/components/settings/OrganizationSettings').then(m => ({ default: m.OrganizationSettings })));
const TenantConfigSettings = lazy(() => import('@foundation/src/components/settings/TenantConfigSettings').then(m => ({ default: m.TenantConfigSettings })));
const SchedulingSettings = lazy(() => import('@foundation/src/components/settings/SchedulingSettings').then(m => ({ default: m.SchedulingSettings })));
const ReportingApiSettings = lazy(() => import('@foundation/src/components/settings/ReportingApiSettings').then(m => ({ default: m.ReportingApiSettings })));
const PlatformApiSettings = lazy(() => import('@foundation/src/components/settings/PlatformApiSettings').then(m => ({ default: m.PlatformApiSettings })));
const AiAssistantSettings = lazy(() => import('@foundation/src/components/settings/AiAssistantSettings').then(m => ({ default: m.AiAssistantSettings })));
const AuditLogTab = lazy(() => import('@foundation/src/components/admin/AuditLogTab').then(m => ({ default: m.AuditLogTab })));
const UsageLimitsSettings = lazy(() => import('@foundation/src/components/settings/UsageLimitsSettings').then(m => ({ default: m.UsageLimitsSettings })));
const FloorplanView = lazy(() => import('@foundation/src/components/spaces/FloorplanView').then(m => ({ default: m.FloorplanView })));

/** Route prefixes where the AppLayout TopBar (with its own ThemeToggle) is rendered. */
const APP_LAYOUT_PREFIXES = ["/", "/floorplan", "/spaces", "/people", "/organization", "/stations", "/assets", "/resources", "/requests", "/insights", "/conflicts", "/settings", "/tenant-admin", "/configuration"];

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

  /**
   * Where to send someone whose plan does not include the AI assistant. Optional and
   * separate from the reporting prop so an edition can point the two at different pages;
   * falls back to it when omitted.
   */
  aiAssistantUnavailableRedirectTo?: string;

  /**
   * The product's own site-administration surface, rendered at `ROUTE_SITE_ADMIN` for a
   * site admin. The same slot `ApexGateway` takes, so a product wires one admin page into
   * both shells and neither has to branch on the pathname itself. Omit it and the route
   * does not exist.
   */
  renderAdminPage?: () => ReactNode;
}

export function TenantApp({
  accountTabs,
  reportingApiUnavailableRedirectTo,
  aiAssistantUnavailableRedirectTo,
  renderAdminPage,
}: TenantAppProps = {}) {
  const { authStage, membership, sessionData, send, canAccessAdminPage } = useAuth();

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
        <Route path={ROUTE_LOGIN} element={<LoginPage />} />
        {/* Site administration renders bare, outside AppLayout — the same shape ApexGateway
            gives it. Not registered without the slot or the right, so it falls through to
            NotFound exactly as an unknown path does. */}
        {renderAdminPage && canAccessAdminPage && (
          <Route path={`${ROUTE_SITE_ADMIN}/*`} element={<>{renderAdminPage()}</>} />
        )}
        <Route path={ROUTE_ABOUT} element={<RequireAuth><RouteErrorBoundary label="page"><AboutPage /></RouteErrorBoundary></RequireAuth>} />
        <Route path={ROUTE_ACCOUNT} element={<RequireAuth requireMembership={false}><RouteErrorBoundary label="page"><AccountPage accountTabs={accountTabs} /></RouteErrorBoundary></RequireAuth>} />
        <Route path={ROUTE_MESSAGES} element={<RequireAuth><RouteErrorBoundary label="page"><MessagesPage /></RouteErrorBoundary></RequireAuth>} />
        <Route path={ROUTE_HOME} element={<RequireAuth><AppLayout upgradeHref={reportingApiUnavailableRedirectTo} /></RequireAuth>}>
          <Route index element={<UtilizationPage />} />
          <Route path={ROUTE_REQUESTS} element={<RequestsPage />} />
          {/* The dependency planner for one parent's children. Its own route, not a dialog:
              a graph needs more width than the widest sanctioned dialog. */}
          <Route path={ROUTE_REQUEST_PLAN} element={<RequestPlanPage />} />
          <Route path={ROUTE_INSIGHTS} element={<InsightsPage />}>
            <Route index element={<Navigate to={ROUTE_INSIGHTS_OVERVIEW} replace />} />
            <Route path={ROUTE_INSIGHTS_OVERVIEW} element={<OverviewTab />} />
            <Route path={ROUTE_INSIGHTS_UTILIZATION} element={<UtilizationTab />} />
            <Route path={ROUTE_INSIGHTS_CONFLICTS} element={<ConflictsTab />} />
            <Route path={ROUTE_INSIGHTS_BOTTLENECKS} element={<BottlenecksTab />} />
          </Route>
          {/* Back-compat: the old top-level Conflicts page is now the Insights → Conflicts tab. */}
          <Route path="conflicts" element={<Navigate to={ROUTE_INSIGHTS_CONFLICTS} replace />} />

          {/* The two resource classes. Type is a selector inside the page, never a route of its
              own, so a new resource type costs no navigation. */}
          <Route path={ROUTE_STATIONS} element={<ResourceClassPage resourceClass="station" />} />
          {/* The plan holds every placeable type at once, so it sits beside the type-scoped tabs
              rather than inside one of them — a type selector would contradict it. The class page
              renders the surrounding header and tab strip; the canvas is the whole body. */}
          <Route path={ROUTE_STATIONS_FLOORPLAN} element={<ResourceClassPage resourceClass="station" surface="floorplan" />}>
            <Route index element={<FloorplanView />} />
          </Route>
          <Route path={`${ROUTE_STATIONS}/:typeKey`} element={<ResourceClassPage resourceClass="station" />}>
            <Route index element={<Navigate to={RESOURCE_TYPE_TAB.INSTANCES} replace />} />
            <Route path={RESOURCE_TYPE_TAB.INSTANCES} element={<ResourceListTab />} />
            <Route path={RESOURCE_TYPE_TAB.GROUPS} element={<ResourceGroupsTab />} />
            <Route path={RESOURCE_TYPE_TAB.LISTS} element={<ResourceListsTab />} />
          </Route>
          <Route path={ROUTE_ASSETS} element={<ResourceClassPage resourceClass="asset" />} />
          <Route path={`${ROUTE_ASSETS}/:typeKey`} element={<ResourceClassPage resourceClass="asset" />}>
            <Route index element={<Navigate to={RESOURCE_TYPE_TAB.INSTANCES} replace />} />
            <Route path={RESOURCE_TYPE_TAB.INSTANCES} element={<ResourceListTab />} />
            <Route path={RESOURCE_TYPE_TAB.GROUPS} element={<ResourceGroupsTab />} />
            <Route path={RESOURCE_TYPE_TAB.LISTS} element={<ResourceListsTab />} />
          </Route>

          {/* Every previous per-type location. The class is not in the old URL, so a small
              component resolves the type and forwards. */}
          <Route path="resources/:typeKey" element={<LegacyTypeRedirect tab="instances" />} />
          <Route path="resources/:typeKey/list" element={<LegacyTypeRedirect tab="instances" />} />
          <Route path="resources/:typeKey/groups" element={<LegacyTypeRedirect tab="groups" />} />

          {/* Settings — editor-open content. Viewers are redirected to root. */}
          <Route path={ROUTE_SETTINGS} element={<RequireEditor><SettingsPage /></RequireEditor>}>
            <Route index element={<Navigate to={ROUTE_SETTINGS_CRITERIA} replace />} />
            <Route path={ROUTE_SETTINGS_CRITERIA} element={<CriteriaSettings />} />
            <Route path={ROUTE_SETTINGS_TEMPLATES} element={<TemplateSettings entityType="request" />} />
            <Route path={ROUTE_SETTINGS_ROUTINGS} element={<RoutingSettings />} />
            <Route path={ROUTE_SETTINGS_PRESETS} element={<PresetSettings />} />
            <Route path={ROUTE_SETTINGS_SCHEDULING} element={<SchedulingSettings />} />
          </Route>

          {/* Administration — tenant-admin-only governance. Default = sites. */}
          <Route
            path={ROUTE_TENANT_ADMIN}
            element={<RequireTenantAdmin><TenantAdminPage /></RequireTenantAdmin>}
          >
            <Route index element={<Navigate to={ROUTE_TENANT_ADMIN_SITES} replace />} />
            <Route path={ROUTE_TENANT_ADMIN_SITES} element={<SiteSettings />} />
            <Route path={ROUTE_TENANT_ADMIN_USERS} element={<UserSettings />} />
            <Route path={ROUTE_TENANT_ADMIN_ORGANIZATION} element={<OrganizationSettings upgradeHref={reportingApiUnavailableRedirectTo} />} />
            <Route path={ROUTE_TENANT_ADMIN_CONFIGURATION} element={<TenantConfigSettings scope="tenant" />} />
            <Route path={ROUTE_TENANT_ADMIN_INTEGRATIONS} element={<ReportingApiSettings upgradeHref={reportingApiUnavailableRedirectTo} />} />
            <Route path={ROUTE_TENANT_ADMIN_API_ACCESS} element={<PlatformApiSettings upgradeHref={reportingApiUnavailableRedirectTo} />} />
            <Route path={ROUTE_TENANT_ADMIN_AI_ASSISTANT} element={<AiAssistantSettings upgradeHref={aiAssistantUnavailableRedirectTo ?? reportingApiUnavailableRedirectTo} />} />
            <Route path={ROUTE_TENANT_ADMIN_AUDIT_LOG} element={<AuditLogTab upgradeHref={reportingApiUnavailableRedirectTo} />} />
            <Route path={ROUTE_TENANT_ADMIN_USAGE_LIMITS} element={<UsageLimitsSettings />} />
          </Route>

          {/* Resources — how the tenant shapes what it schedules. Admin-gated like
              Administration, but a different question: not who is in the tenant, but what
              its resources are and what they carry. Default = resource types. */}
          <Route
            path={ROUTE_CONFIGURATION}
            element={<RequireTenantAdmin><ConfigurationPage /></RequireTenantAdmin>}
          >
            <Route index element={<Navigate to={ROUTE_CONFIGURATION_RESOURCE_TYPES} replace />} />
            <Route path={ROUTE_CONFIGURATION_RESOURCE_TYPES} element={<ResourceTypeSettings />} />
            <Route path={ROUTE_CONFIGURATION_CATALOG} element={<TypeCatalogSettings />} />
            <Route path={ROUTE_CONFIGURATION_LIST_DEFINITIONS} element={<ListDefinitionSettings />} />
          </Route>

          {/* People was the last per-type page. Person is an ordinary asset type now, so these
              forward rather than render. */}
          <Route path="people" element={<LegacyTypeRedirect typeKey="person" tab="instances" />} />
          <Route path="people/list" element={<LegacyTypeRedirect typeKey="person" tab="instances" />} />
          <Route path="people/teams" element={<LegacyTypeRedirect typeKey="person" tab="groups" />} />
          <Route path="people/groups" element={<LegacyTypeRedirect typeKey="person" tab="groups" />} />
          <Route path="people/departments" element={<Navigate to={ROUTE_ORGANIZATION} replace />} />
          <Route path="people/job-titles" element={<Navigate to={ROUTE_ORGANIZATION} replace />} />

          {/* Organization master data — the tenant's own structure, kept as organization lists. */}
          <Route path={ROUTE_ORGANIZATION} element={<OrganizationPage />} />

          {/* Every address the plan has had. It is a station surface now, so it lives under
              /stations rather than beside it. */}
          <Route path="floorplan" element={<Navigate to={ROUTE_STATIONS_FLOORPLAN} replace />} />
          <Route path="floorplan/floorplan" element={<Navigate to={ROUTE_STATIONS_FLOORPLAN} replace />} />
          <Route path="floorplan/stations" element={<Navigate to={ROUTE_STATIONS} replace />} />
          <Route path="spaces" element={<Navigate to={ROUTE_STATIONS_FLOORPLAN} replace />} />
          <Route path="spaces/floorplan" element={<Navigate to={ROUTE_STATIONS_FLOORPLAN} replace />} />
          <Route path="spaces/list" element={<LegacyTypeRedirect typeKey="space" tab="instances" />} />
          <Route path="spaces/groups" element={<LegacyTypeRedirect typeKey="space" tab="groups" />} />

          {/* Backward-compatible redirects: resource-domain master data moved
              out of Settings into the owning resource page. */}
          <Route path="settings/groups"      element={<LegacyTypeRedirect typeKey="space" tab="groups" />} />

          {/* Backward-compatible redirects: governance tabs moved out of Settings
              into the tenant-admin Administration page. */}
          <Route path="settings/sites"         element={<Navigate to={ROUTE_TENANT_ADMIN_SITES} replace />} />
          <Route path="settings/users"         element={<Navigate to={ROUTE_TENANT_ADMIN_USERS} replace />} />
          <Route path="settings/organization"  element={<Navigate to={ROUTE_TENANT_ADMIN_ORGANIZATION} replace />} />
          <Route path="settings/configuration" element={<Navigate to={ROUTE_TENANT_ADMIN_CONFIGURATION} replace />} />
          <Route path="settings/resource-types" element={<Navigate to={ROUTE_CONFIGURATION_RESOURCE_TYPES} replace />} />
          {/* Resource types moved out of Administration into Resources. */}
          <Route path="tenant-admin/resource-types" element={<Navigate to={ROUTE_CONFIGURATION_RESOURCE_TYPES} replace />} />
          <Route path="settings/integrations"  element={<Navigate to={ROUTE_TENANT_ADMIN_INTEGRATIONS} replace />} />
          <Route path="settings/usage-limits"  element={<Navigate to={ROUTE_TENANT_ADMIN_USAGE_LIMITS} replace />} />
        </Route>

        {/* Catch-all: unknown URLs render a recoverable 404 instead of a blank screen. */}
        <Route path="*" element={<NotFound />} />
      </Routes>
      </Suspense>
      <Toaster />
    </>
  );
}
