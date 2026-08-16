import { Outlet, useNavigate } from 'react-router';
import { PageLayout, PageHeader, PageTabs, type PageTab } from '@foundation/src/components/layout';
import { useActiveTab } from '@foundation/src/hooks/useActiveTab';
import { usePageTitle } from '@foundation/src/hooks/usePageTitle';
import { useAppStore } from '@foundation/src/store/app-store';
import { useResourceTypes } from '@foundation/src/hooks/useResourceTypes';
import { usePlaceableResources } from '@foundation/src/hooks/usePlaceableResources';
import { useResourceTransfer } from '@foundation/src/hooks/useResourceTransfer';
import type { ResourceTypeInfo } from '@foundation/src/lib/api/resource-types-api';
import type { ResourceInfo } from '@foundation/src/lib/api/resources-api';

const TABS: PageTab[] = [
  { value: 'floorplan', label: 'Floorplan' },
  { value: 'list',      label: 'Stations' },
  { value: 'groups',    label: 'Groups' },
];

/**
 * Import/export for one placeable type. A component rather than a loop body, because
 * useResourceTransfer is a hook and the number of placeable types is tenant data — calling it in
 * a loop would break the rules of hooks the first time an admin adds or retires a type.
 */
function PlaceableTransfer({
  resourceType,
  resources,
}: {
  resourceType: ResourceTypeInfo;
  resources: ResourceInfo[];
}) {
  useResourceTransfer(
    resourceType,
    resources.filter((r) => r.resourceTypeKey === resourceType.key),
  );
  return null;
}

export function SpacesPage() {
  usePageTitle('Floorplan');
  const active = useActiveTab('floorplan');
  const navigate = useNavigate();
  // Mounted by the page, not a tab: every tab here offers import/export. One registration per
  // placeable type, because a CSV's columns are the type's own custom fields.
  const selectedSiteId = useAppStore((state) => state.selectedSiteId);
  const { data: resourceTypes = [] } = useResourceTypes(true);
  const { data: placeableResources = [] } = usePlaceableResources(selectedSiteId);
  const placeableTypes = resourceTypes.filter((t) => t.hasGeometry);

  return (
    <PageLayout>
      {placeableTypes.map((t) => (
        <PlaceableTransfer key={t.id} resourceType={t} resources={placeableResources} />
      ))}
      <PageHeader
        title="Floorplan"
        description="Manage the floorplan, the stations on it, and their groups"
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
