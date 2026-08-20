import { useAppStore } from '@foundation/src/store/app-store';
import { useResourceTypes } from '@foundation/src/hooks/useResourceTypes';
import { usePlaceableResources } from '@foundation/src/hooks/usePlaceableResources';
import { useResourceTransfer } from '@foundation/src/hooks/useResourceTransfer';
import type { ResourceTypeInfo } from '@foundation/src/lib/api/resource-types-api';
import type { ResourceInfo } from '@foundation/src/lib/api/resources-api';
import { SpaceManagementPanel } from './SpaceManagementPanel';

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

/**
 * The floorplan — a *site* surface rather than a page about one resource type. The plan holds
 * every placeable type at once, which is why it has one typeless URL and no type selector; which
 * type a drawn shape becomes is the canvas toolbar's dropdown to answer, not the address bar's.
 *
 * One CSV registration per placeable type, because a file's columns are that type's own custom
 * fields. They live here rather than on the page above: this is the only thing on the surface.
 */
export function FloorplanView() {
  const selectedSiteId = useAppStore((state) => state.selectedSiteId);
  const { data: resourceTypes = [] } = useResourceTypes(true);
  const { data: placeableResources = [] } = usePlaceableResources(selectedSiteId);
  const placeableTypes = resourceTypes.filter((t) => t.hasGeometry);

  return (
    <>
      {placeableTypes.map((t) => (
        <PlaceableTransfer key={t.id} resourceType={t} resources={placeableResources} />
      ))}
      {selectedSiteId ? (
        <div className="h-full flex flex-col">
          <SpaceManagementPanel siteId={selectedSiteId} className="flex-1" />
        </div>
      ) : (
        <div className="rounded-2xl border bg-card p-6">
          <p className="text-muted-foreground">Please select a site to manage the floorplan.</p>
        </div>
      )}
    </>
  );
}
