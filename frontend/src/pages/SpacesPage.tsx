import { Outlet, useNavigate } from 'react-router-dom';
import { PageLayout, PageHeader, PageTabs, type PageTab } from '@foundation/src/components/layout';
import { useActiveTab } from '@foundation/src/hooks/useActiveTab';
import { usePageTitle } from '@foundation/src/hooks/usePageTitle';

const TABS: PageTab[] = [
  { value: 'floorplan', label: 'Floorplan' },
  { value: 'list',      label: 'Spaces' },
  { value: 'groups',    label: 'Groups' },
];

export function SpacesPage() {
  usePageTitle('Spaces');
  const active = useActiveTab('floorplan');
  const navigate = useNavigate();

  return (
    <PageLayout>
      <PageHeader
        title="Spaces"
        description="Manage spaces, floorplans and groups"
      />
      <PageTabs
        tabs={TABS}
        value={active}
        onChange={(v) => navigate(`/spaces/${v}`)}
      >
        <Outlet />
      </PageTabs>
    </PageLayout>
  );
}
