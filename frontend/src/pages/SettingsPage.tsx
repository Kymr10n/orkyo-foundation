import { useMemo } from 'react';
import { Outlet, useNavigate } from 'react-router-dom';

import { useAuth } from '@foundation/src/contexts/AuthContext';
import { useSites } from '@foundation/src/hooks/useSites';
import { PageLayout, PageHeader, PageTabs, type PageTab } from '@foundation/src/components/layout';
import { useActiveTab } from '@foundation/src/hooks/useActiveTab';
import { useLegacyTabRedirect } from '@foundation/src/hooks/useLegacyTabRedirect';

// Map legacy ?tab= values (pre nested-route routing) to the new path segment.
// Includes both same-domain renames and out-of-domain redirects retained from the
// earlier migration. TODO 2026-08-17: remove after one release cycle.
const LEGACY_TAB_TO_PATH: Record<string, string> = {
  // moved out of Settings into the owning resource page
  jobTitles: '/people/job-titles',
  departments: '/people/departments',
  // current settings tabs (query-param → path)
  criteria: '/settings/criteria',
  sites: '/settings/sites',
  templates: '/settings/templates',
  presets: '/settings/presets',
  users: '/settings/users',
  organization: '/settings/organization',
  scheduling: '/settings/scheduling',
  configuration: '/settings/configuration',
  integrations: '/settings/integrations',
};

export function SettingsPage() {
  const { membership } = useAuth();
  const isAdmin = membership?.isTenantAdmin === true;
  const tier = membership?.tier ?? 'free';
  const { data: sites = [] } = useSites();
  const showSites = tier !== 'free' || sites.length > 1;

  const active = useActiveTab('criteria');
  const navigate = useNavigate();

  // Backward-compat: redirect legacy ?tab= to the path-based route.
  useLegacyTabRedirect(LEGACY_TAB_TO_PATH);

  const tabs = useMemo<PageTab[]>(() => {
    const all: PageTab[] = [
      { value: 'criteria', label: 'Criteria' },
      ...(showSites ? [{ value: 'sites', label: 'Sites' }] : []),
      { value: 'templates', label: 'Templates' },
      { value: 'presets', label: 'Presets' },
      ...(isAdmin ? [{ value: 'users', label: 'Users' }] : []),
      { value: 'organization', label: 'Organization' },
      { value: 'scheduling', label: 'Scheduling' },
      { value: 'configuration', label: 'Configuration' },
      ...(isAdmin ? [{ value: 'integrations', label: 'Integrations' }] : []),
      ...(isAdmin ? [{ value: 'usage-limits', label: 'Usage & Limits' }] : []),
    ];
    return all;
  }, [showSites, isAdmin]);

  return (
    <PageLayout>
      <PageHeader
        title="Settings"
        description="Manage tenant-wide configurations and definitions"
      />
      <PageTabs
        tabs={tabs}
        value={active}
        onChange={(v) => navigate(`/settings/${v}`, { replace: true })}
      >
        <Outlet />
      </PageTabs>
    </PageLayout>
  );
}
