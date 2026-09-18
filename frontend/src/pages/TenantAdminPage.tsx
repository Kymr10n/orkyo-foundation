import { useMemo } from 'react';
import { Outlet, useNavigate } from 'react-router';

import { useSites } from '@foundation/src/hooks/useSites';
import { useAuth } from '@foundation/src/contexts/AuthContext';
import { PlanCodes } from '@foundation/contracts/plans';
import { PageLayout, PageHeader, PageTabs, type PageTab } from '@foundation/src/components/layout';
import { ROUTE_TENANT_ADMIN } from '@foundation/src/constants/auth';
import { useActiveTab } from '@foundation/src/hooks/useActiveTab';
import { usePageTitle } from '@foundation/src/hooks/usePageTitle';

export function TenantAdminPage() {
  usePageTitle('Administration');
  const { membership } = useAuth();
  const tier = membership?.tier ?? PlanCodes.Free;
  const { data: sites = [] } = useSites();
  const showSites = tier !== PlanCodes.Free || sites.length > 1;

  const active = useActiveTab('sites');
  const navigate = useNavigate();

  const tabs = useMemo<PageTab[]>(() => {
    return [
      ...(showSites ? [{ value: 'sites', label: 'Sites' }] : []),
      { value: 'users', label: 'Users' },
      { value: 'organization', label: 'Organization' },
      { value: 'configuration', label: 'Configuration' },
      { value: 'integrations', label: 'Integrations' },
      { value: 'api-access', label: 'API & AI Access' },
      { value: 'ai-assistant', label: 'AI Assistant' },
      { value: 'audit-log', label: 'Audit Log' },
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
        onChange={(v) => navigate(`${ROUTE_TENANT_ADMIN}/${v}`, { replace: true })}
      >
        <Outlet />
      </PageTabs>
    </PageLayout>
  );
}
