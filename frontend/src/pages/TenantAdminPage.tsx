import { useMemo } from 'react';
import { Outlet, useNavigate } from 'react-router-dom';

import { useSites } from '@foundation/src/hooks/useSites';
import { useAuth } from '@foundation/src/contexts/AuthContext';
import { PageLayout, PageHeader, PageTabs, type PageTab } from '@foundation/src/components/layout';
import { useActiveTab } from '@foundation/src/hooks/useActiveTab';
import { useLegacyTabRedirect } from '@foundation/src/hooks/useLegacyTabRedirect';

// Map legacy ?tab= values and pre-split /settings/<tab> bookmarks to the new
// /tenant-admin path segment. The corresponding /settings/<tab> → /tenant-admin
// redirects live in TenantApp routing. TODO 2026-09-07: remove after one release.
const LEGACY_TAB_TO_PATH: Record<string, string> = {
  sites: '/tenant-admin/sites',
  users: '/tenant-admin/users',
  organization: '/tenant-admin/organization',
  configuration: '/tenant-admin/configuration',
  integrations: '/tenant-admin/integrations',
  'usage-limits': '/tenant-admin/usage-limits',
};

export function TenantAdminPage() {
  const { membership } = useAuth();
  const tier = membership?.tier ?? 'free';
  const { data: sites = [] } = useSites();
  const showSites = tier !== 'free' || sites.length > 1;

  const active = useActiveTab('sites');
  const navigate = useNavigate();

  // Backward-compat: redirect legacy ?tab= to the path-based route.
  useLegacyTabRedirect(LEGACY_TAB_TO_PATH);

  const tabs = useMemo<PageTab[]>(() => {
    return [
      ...(showSites ? [{ value: 'sites', label: 'Sites' }] : []),
      { value: 'users', label: 'Users' },
      { value: 'organization', label: 'Organization' },
      { value: 'configuration', label: 'Configuration' },
      { value: 'integrations', label: 'Integrations' },
      { value: 'usage-limits', label: 'Usage & Limits' },
    ];
  }, [showSites]);

  return (
    <PageLayout>
      <PageHeader
        title="Administration"
        description="Manage tenant governance: sites, users, organization, and integrations"
      />
      <PageTabs
        tabs={tabs}
        value={active}
        onChange={(v) => navigate(`/tenant-admin/${v}`, { replace: true })}
      >
        <Outlet />
      </PageTabs>
    </PageLayout>
  );
}
