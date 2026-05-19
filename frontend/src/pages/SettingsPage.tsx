import { useEffect, useMemo } from 'react';
import { Outlet, useLocation, useNavigate, useSearchParams } from 'react-router-dom';

import { useAuth } from '@foundation/src/contexts/AuthContext';
import { useSites } from '@foundation/src/hooks/useSites';
import { PageLayout, PageHeader, PageTabs, type PageTab } from '@foundation/src/components/layout';

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
};

export function SettingsPage() {
  const { membership } = useAuth();
  const isAdmin = membership?.isTenantAdmin === true;
  const tier = membership?.tier ?? 'Free';
  const { data: sites = [] } = useSites();
  const showSites = tier !== 'Free' || sites.length > 1;

  const { pathname } = useLocation();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();

  // Backward-compat: redirect legacy ?tab= to the path-based route.
  useEffect(() => {
    const legacy = searchParams.get('tab');
    if (legacy && LEGACY_TAB_TO_PATH[legacy]) {
      navigate(LEGACY_TAB_TO_PATH[legacy], { replace: true });
    }
  }, [searchParams, navigate]);

  // Active tab = path segment after /settings/...
  const active = pathname.split('/')[2] ?? 'criteria';

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
        onChange={(v) => navigate(`/settings/${v}`)}
      >
        <Outlet />
      </PageTabs>
    </PageLayout>
  );
}
