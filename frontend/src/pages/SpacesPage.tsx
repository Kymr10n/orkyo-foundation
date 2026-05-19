import { Outlet, useLocation, useNavigate } from 'react-router-dom';
import { PageLayout, PageHeader, PageTabs, type PageTab } from '@foundation/src/components/layout';

const TABS: PageTab[] = [
  { value: 'floorplan', label: 'Floorplan' },
  { value: 'groups', label: 'Groups' },
  { value: 'capabilities', label: 'Capabilities' },
];

export function SpacesPage() {
  const { pathname } = useLocation();
  const navigate = useNavigate();
  const active = pathname.split('/')[2] ?? 'floorplan';

  return (
    <PageLayout>
      <PageHeader
        title="Spaces"
        description="Manage spaces, floorplan, groups, and capabilities"
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
