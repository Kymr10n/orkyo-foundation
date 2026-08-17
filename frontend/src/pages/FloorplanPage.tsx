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

// No Groups tab: groups are typed, and every type — placeable or not — now owns its groups on
// its own page (/resources/<key>/groups). A cross-type groups tab here would be a second place
// to manage the same rows, differing only in which types it happened to include.
const TABS: PageTab[] = [
  { value: 'floorplan', label: 'Floorplan' },
  { value: 'stations',  label: 'Stations' },
];

/**
 * The floorplan: a *site* surface, not a page about one resource type.
 *
 * That is why it survives the retirement of the dedicated space page while /people does not
 * generalize: the plan holds every placeable type at once, and its Stations tab is the
 * site-scoped cross-type list — a different axis from a type page, which lists one type across
 * every site. Which type a drawn shape becomes is the toolbar's dropdown to answer, not the
 * URL's, which is why this is not a tab on /resources/<key>.
 */

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

export function FloorplanPage() {
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
        description="Manage the floorplan and the stations on it"
      />
      <PageTabs
        tabs={TABS}
        value={active}
        onChange={(v) => navigate(`/floorplan/${v}`)}
      >
        <Outlet />
      </PageTabs>
    </PageLayout>
  );
}
