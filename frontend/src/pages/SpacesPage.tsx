import { Outlet, useNavigate } from 'react-router';
import { PageLayout, PageHeader, PageTabs, type PageTab } from '@foundation/src/components/layout';
import { useActiveTab } from '@foundation/src/hooks/useActiveTab';
import { usePageTitle } from '@foundation/src/hooks/usePageTitle';
import { useAppStore } from '@foundation/src/store/app-store';
import { useSpaceTransfer } from '@foundation/src/hooks/useSpaceTransfer';

const TABS: PageTab[] = [
  { value: 'floorplan', label: 'Floorplan' },
  { value: 'list',      label: 'Spaces' },
  { value: 'groups',    label: 'Groups' },
];

export function SpacesPage() {
  usePageTitle('Spaces');
  const active = useActiveTab('floorplan');
  const navigate = useNavigate();
  // Mounted by the page, not a tab: every Spaces tab offers import/export.
  useSpaceTransfer(useAppStore((state) => state.selectedSiteId));

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
