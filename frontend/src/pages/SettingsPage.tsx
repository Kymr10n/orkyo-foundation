import { useMemo } from 'react';
import { Outlet, useNavigate } from 'react-router-dom';

import { PageLayout, PageHeader, PageTabs, type PageTab } from '@foundation/src/components/layout';
import { ROUTE_SETTINGS } from '@foundation/src/constants/auth';
import { useActiveTab } from '@foundation/src/hooks/useActiveTab';
import { usePageTitle } from '@foundation/src/hooks/usePageTitle';
import { useLegacyTabRedirect } from '@foundation/src/hooks/useLegacyTabRedirect';

// Map legacy ?tab= values (pre nested-route routing) to the new path segment.
// Governance tabs moved to /tenant-admin (see TenantAdminPage); editor tabs stay
// under /settings. Out-of-domain master data redirects retained from the earlier
// migration. TODO 2026-09-07: remove after one release cycle.
const LEGACY_TAB_TO_PATH: Record<string, string> = {
  // moved out of Settings into the owning resource page
  jobTitles: '/people/job-titles',
  departments: '/people/departments',
  // editor-open settings tabs (query-param → path)
  criteria: '/settings/criteria',
  templates: '/settings/templates',
  presets: '/settings/presets',
  scheduling: '/settings/scheduling',
  // governance tabs moved to the tenant-admin Administration page
  sites: '/tenant-admin/sites',
  users: '/tenant-admin/users',
  organization: '/tenant-admin/organization',
  configuration: '/tenant-admin/configuration',
  integrations: '/tenant-admin/integrations',
};

export function SettingsPage() {
  usePageTitle('Settings');
  const active = useActiveTab('criteria');
  const navigate = useNavigate();

  // Backward-compat: redirect legacy ?tab= to the path-based route.
  useLegacyTabRedirect(LEGACY_TAB_TO_PATH);

  const tabs = useMemo<PageTab[]>(() => [
    { value: 'criteria', label: 'Criteria' },
    { value: 'templates', label: 'Templates' },
    { value: 'presets', label: 'Presets' },
    { value: 'scheduling', label: 'Scheduling' },
  ], []);

  return (
    <PageLayout>
      <PageHeader
        title="Settings"
        description="Manage shared definitions: criteria, templates, presets, and scheduling"
      />
      <PageTabs
        tabs={tabs}
        value={active}
        onChange={(v) => navigate(`${ROUTE_SETTINGS}/${v}`, { replace: true })}
      >
        <Outlet />
      </PageTabs>
    </PageLayout>
  );
}
