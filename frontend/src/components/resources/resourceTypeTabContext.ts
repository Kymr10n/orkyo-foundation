import { useOutletContext } from 'react-router';
import type { ResourceTypeInfo } from '@foundation/src/lib/api/resource-types-api';

/**
 * The resource type a `/resources/:typeKey/*` tab is rendering. Provided once by
 * ResourcesPage via the router `<Outlet context>` so each tab reads the resolved type
 * instead of re-fetching the type list and re-finding it by key.
 */
export interface ResourceTypeTabContext {
  resourceType: ResourceTypeInfo;
}

export const useResourceTypeTabContext = () => useOutletContext<ResourceTypeTabContext>();
