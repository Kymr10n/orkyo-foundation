import { createCrudHooks } from "./useMutations";
import { createSpaceGroup, deleteSpaceGroup, getSpaceGroups, updateSpaceGroup } from "@/lib/api/space-groups-api";
import type { SpaceGroup, CreateSpaceGroupRequest, UpdateSpaceGroupRequest } from "@/types/spaceGroup";

const spaceGroupHooks = createCrudHooks<SpaceGroup, CreateSpaceGroupRequest, UpdateSpaceGroupRequest, string | null>({
  queryKey: (siteId) => ["space-groups", siteId],
  queryFn: () => getSpaceGroups(),
  createFn: (data) => createSpaceGroup(data),
  updateFn: (id, data) => updateSpaceGroup(id, data),
  deleteFn: (id) => deleteSpaceGroup(id),
  invalidateKeys: (siteId) => [["spaces", siteId], ["requests"]],
});

// siteId drives the queryKey, enabled state, and cache invalidation scope.
export const useSpaceGroups = (siteId: string | null) => spaceGroupHooks.useQuery(siteId);
export const useCreateSpaceGroup = (siteId: string) => spaceGroupHooks.useCreate(siteId);
export const useUpdateSpaceGroup = (siteId: string) => spaceGroupHooks.useUpdate(siteId);
export const useDeleteSpaceGroup = (siteId: string) => spaceGroupHooks.useDelete(siteId);
