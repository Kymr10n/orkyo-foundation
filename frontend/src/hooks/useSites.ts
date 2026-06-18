import { createCrudHooks } from "./useMutations";
import { createSite, deleteSite, getSites, updateSite } from "@foundation/src/lib/api/site-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import type { Site, CreateSiteRequest, UpdateSiteRequest } from "@foundation/src/types/site";

const sitesHooks = createCrudHooks<Site, CreateSiteRequest, UpdateSiteRequest>({
  queryKey: () => ["sites"],
  queryFn: () => getSites(),
  createFn: (data) => createSite(data),
  updateFn: (id, data) => updateSite(id, data),
  deleteFn: (id) => deleteSite(id),
  // Deleting a site cascades to spaces and requests; over-invalidating on
  // create/update is harmless and keeps the factory contract simple.
  invalidateKeys: () => [qk.spaces.all(), qk.requests.all()],
  entityLabel: "Site",
});

export const useSites = () => sitesHooks.useQuery(undefined, { enabled: true });
export const useCreateSite = () => sitesHooks.useCreate();
export const useUpdateSite = () => sitesHooks.useUpdate();
export const useDeleteSite = () => sitesHooks.useDelete();

/**
 * Whether the tenant has more than one site. Single source of truth for the
 * site-model's progressive disclosure: when false, all site UI (request Site
 * picker, people home/current/cross-site fields, candidate site badges) is
 * hidden so single-site / free-tier tenants never see the concept.
 */
export const useIsMultiSite = (): boolean => {
  const { data: sites } = useSites();
  return (sites?.length ?? 0) > 1;
};
