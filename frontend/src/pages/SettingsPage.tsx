import { useMemo } from 'react';
import { Outlet, useNavigate } from 'react-router';

import { PageLayout, PageHeader, PageTabs, type PageTab } from '@foundation/src/components/layout';
import { ROUTE_SETTINGS } from '@foundation/src/constants/auth';
import { useActiveTab } from '@foundation/src/hooks/useActiveTab';
import { usePageTitle } from '@foundation/src/hooks/usePageTitle';

export function SettingsPage() {
  usePageTitle('Settings');
  const active = useActiveTab('criteria');
  const navigate = useNavigate();

  const tabs = useMemo<PageTab[]>(() => [
    { value: 'criteria', label: 'Criteria' },
    { value: 'templates', label: 'Templates' },
    { value: 'routings', label: 'Routings' },
    { value: 'presets', label: 'Presets' },
    { value: 'scheduling', label: 'Scheduling' },
  ], []);

  return (
    <PageLayout>
      <PageHeader
        title="Settings"
        description="Manage shared definitions: criteria, templates, routings, presets, and scheduling"
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
