import { createCrudHooks } from "./useMutations";
import { createSite, deleteSite, getSites, updateSite } from "@/lib/api/site-api";
import type { Site, CreateSiteRequest, UpdateSiteRequest } from "@/types/site";

const sitesHooks = createCrudHooks<Site, CreateSiteRequest, UpdateSiteRequest>({
  queryKey: () => ["sites"],
  queryFn: () => getSites(),
  createFn: (data) => createSite(data),
  updateFn: (id, data) => updateSite(id, data),
  deleteFn: (id) => deleteSite(id),
  // Deleting a site cascades to spaces and requests; over-invalidating on
  // create/update is harmless and keeps the factory contract simple.
  invalidateKeys: () => [["spaces"], ["requests"]],
});

export const useSites = () => sitesHooks.useQuery(undefined, { enabled: true });
export const useCreateSite = () => sitesHooks.useCreate();
export const useUpdateSite = () => sitesHooks.useUpdate();
export const useDeleteSite = () => sitesHooks.useDelete();
