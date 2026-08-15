import { useMemo } from 'react';
import { Outlet, useNavigate } from 'react-router';

import { PageLayout, PageHeader, PageTabs, type PageTab } from '@foundation/src/components/layout';
import { ROUTE_CONFIGURATION } from '@foundation/src/constants/auth';
import { useActiveTab } from '@foundation/src/hooks/useActiveTab';
import { usePageTitle } from '@foundation/src/hooks/usePageTitle';

/**
 * Resources — how a tenant shapes the things it schedules.
 *
 * Separate from Administration because the two answer different questions. Administration is
 * governance of the tenant itself (who is in it, which sites, what it pays for); this is the
 * shape of its data: what kinds of resource exist, what properties they carry, and what lists
 * hang off them. Resource Types moved here from Administration for that reason, and
 * Administration's own "Configuration" tab — tenant settings — is untouched.
 *
 * The route is /configuration rather than /resources, which the per-type resource pages already
 * claim. A label that differs from its path has precedent: Administration lives at /tenant-admin.
 */
export function ConfigurationPage() {
  usePageTitle('Resources');
  const active = useActiveTab('resource-types');
  const navigate = useNavigate();

  const tabs = useMemo<PageTab[]>(
    () => [
      { value: 'resource-types', label: 'Resource Types' },
      { value: 'list-definitions', label: 'List definitions' },
    ],
    [],
  );

  return (
    <PageLayout>
      <PageHeader
        title="Resources"
        description="Define the kinds of resource this tenant schedules, and the lists they carry"
      />
      <PageTabs
        tabs={tabs}
        value={active}
        onChange={(v) => navigate(`${ROUTE_CONFIGURATION}/${v}`, { replace: true })}
      >
        <Outlet />
      </PageTabs>
    </PageLayout>
  );
}
