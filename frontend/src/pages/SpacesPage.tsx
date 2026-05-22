import { Outlet, useNavigate } from 'react-router-dom';
import { PageLayout, PageHeader, PageTabs, type PageTab } from '@foundation/src/components/layout';
import { useActiveTab } from '@foundation/src/hooks/useActiveTab';

const TABS: PageTab[] = [
  { value: 'floorplan', label: 'Floorplan' },
  { value: 'groups', label: 'Groups' },
];

export function SpacesPage() {
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
