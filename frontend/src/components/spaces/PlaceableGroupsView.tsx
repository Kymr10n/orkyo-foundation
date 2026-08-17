import { Box } from 'lucide-react';
import { useResourceTypes } from '@foundation/src/hooks/useResourceTypes';
import { ResourceGroupList } from '@foundation/src/components/resource-groups/ResourceGroupList';

/**
 * The Floorplan page's Groups tab. The page's other tabs are cross-type — the plan and the
 * stations list hold every placeable type — but groups are typed, so "the floorplan's groups"
 * is one group list per placeable type, not one list filtered to the space key (which is what
 * this replaces: machine cells simply never appeared).
 *
 * With a single placeable type this renders exactly one list, heading-free — identical to the
 * old behaviour for a tenant who never defined machine types.
 */
export function PlaceableGroupsView() {
  const { data: resourceTypes = [] } = useResourceTypes(true);
  const placeableTypes = resourceTypes.filter((t) => t.hasGeometry);

  if (placeableTypes.length === 0) return null;

  if (placeableTypes.length === 1) {
    return (
      <ResourceGroupList
        resourceTypeKey={placeableTypes[0].key}
        membersIcon={Box}
      />
    );
  }

  return (
    <div className="space-y-8">
      {placeableTypes.map((type) => (
        <section key={type.id} className="space-y-2">
          <h3 className="text-sm font-semibold text-muted-foreground">
            {type.displayNamePlural}
          </h3>
          <ResourceGroupList resourceTypeKey={type.key} membersIcon={Box} />
        </section>
      ))}
    </div>
  );
}
