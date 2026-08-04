import { ResourceGroupList } from '../resource-groups/ResourceGroupList';
import { ResourceList } from './ResourceList';
import { useResourceTypeTabContext } from './resourceTypeTabContext';

/**
 * Tab bodies for `/resources/:typeKey/*`. Both are thin: the components they mount are
 * already type-agnostic, so the only job here is handing them the resolved type from the
 * router context.
 */

export function ResourceListTab() {
  const { resourceType } = useResourceTypeTabContext();
  return <ResourceList resourceType={resourceType} />;
}

export function ResourceGroupsTab() {
  const { resourceType } = useResourceTypeTabContext();
  return (
    <ResourceGroupList
      resourceTypeKey={resourceType.key}
      entityLabel="Group"
    />
  );
}
